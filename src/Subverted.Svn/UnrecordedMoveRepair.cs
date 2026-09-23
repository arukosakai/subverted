namespace Subverted.Svn;

/// <summary>
/// Records a rename SVN was never told about, so the file keeps its history.
/// </summary>
/// <remarks>
/// <c>svn move</c> cannot do this on its own — its source has to be on disk, and after a rename in
/// Explorer it is not. So the renamed file is put back at the old path and <c>svn move</c> carries it
/// to the new one. Nothing is restored or compared along the way, so a file renamed and edited ends
/// up with the edit, and the route works for a node inside an uncommitted copy — which restoring
/// the source with <c>svn revert</c> did not.
/// </remarks>
public sealed class UnrecordedMoveRepair(SvnMoveCommand move)
{
    /// <param name="source">Absolute path of the versioned node, missing from disk.</param>
    /// <param name="destination">Absolute path of the unversioned file holding its content.</param>
    /// <returns>SVN's notification text for the recorded move.</returns>
    /// <exception cref="SvnCommandException">
    /// The move failed. The file is put back at the destination first, so the working copy is as it
    /// was found; a process killed part-way leaves the file at its old name, where it came from.
    /// </exception>
    public async Task<string> RepairAsync(
        string workingCopyRoot,
        string source,
        string destination,
        CancellationToken cancellationToken
    )
    {
        var recreated = RecreateMissingParents(source);
        try
        {
            File.Move(destination, source);
            try
            {
                return await move.MoveAsync(
                    workingCopyRoot,
                    source,
                    destination,
                    cancellationToken
                );
            }
            catch
            {
                if (File.Exists(source) && !File.Exists(destination))
                {
                    File.Move(source, destination);
                }

                throw;
            }
        }
        finally
        {
            RemoveIfEmpty(recreated);
        }
    }

    /// <returns>The directories created, outermost first.</returns>
    private static List<string> RecreateMissingParents(string path)
    {
        var missing = new List<string>();
        for (
            var directory = Path.GetDirectoryName(path);
            directory is not null && !Directory.Exists(directory);
            directory = Path.GetDirectoryName(directory)
        )
        {
            missing.Insert(0, directory);
        }

        foreach (var directory in missing)
        {
            Directory.CreateDirectory(directory);
        }

        return missing;
    }

    /// <summary>
    /// The folders were gone when the repair started, and the person removed them; leaving them
    /// behind would undo a deletion nobody asked to undo. Only empty ones, innermost first.
    /// </summary>
    private static void RemoveIfEmpty(List<string> recreated)
    {
        for (var index = recreated.Count - 1; index >= 0; index--)
        {
            var directory = recreated[index];
            if (
                Directory.Exists(directory)
                && !Directory.EnumerateFileSystemEntries(directory).Any()
            )
            {
                Directory.Delete(directory);
            }
        }
    }
}
