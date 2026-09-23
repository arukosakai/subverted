using Microsoft.Data.Sqlite;

namespace Subverted.Svn.Tests;

/// <summary>
/// Puts a real working copy into the two states <c>svn cleanup</c> exists to clear, by writing the
/// rows a dead client leaves behind.
/// </summary>
/// <remarks>
/// These rows are not invented. Killing an <c>svn commit</c> parked in a blocking pre-commit hook
/// leaves <c>WC_LOCK = ('', -1)</c> and nothing else — <c>SvnCleanupIntegrationTests</c> does
/// exactly that and asserts the shape. Writing them directly is what makes the <em>depth</em> cases
/// reachable, since a crash only ever produces the one at every level.
/// </remarks>
internal static class WorkingCopyWedge
{
    /// <param name="relPath">The locked directory, slash-separated, root as the empty string.</param>
    /// <param name="lockedLevels">How far below it the lock reaches; -1 for every level.</param>
    public static void TakeWriteLock(SvnWorkingCopy copy, string relPath, int lockedLevels) =>
        Execute(
            copy,
            "INSERT INTO wc_lock (wc_id, local_dir_relpath, locked_levels) "
                + "VALUES (1, $path, $levels);",
            ("$path", relPath),
            ("$levels", lockedLevels)
        );

    /// <summary>
    /// Queues one step of an interrupted operation — the state in which <c>svn</c> refuses to read
    /// the working copy at all, <c>E155037</c>, <c>svn status</c> included.
    /// </summary>
    /// <remarks>
    /// A real work item in the skel form SVN writes, because cleanup <em>runs</em> what it finds: a
    /// blob it could not parse would fail the command rather than be finished by it.
    /// </remarks>
    public static void QueueInterruptedWork(SvnWorkingCopy copy, string relPath) =>
        Execute(
            copy,
            "INSERT INTO work_queue (work) VALUES ($work);",
            ("$work", System.Text.Encoding.UTF8.GetBytes($"(file-install {relPath} 1 0 1 1)"))
        );

    private static void Execute(
        SvnWorkingCopy copy,
        string sql,
        params (string Name, object Value)[] parameters
    )
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(copy.Root, ".svn", "wc.db"),
                Pooling = false,
            }.ToString()
        );
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }
}
