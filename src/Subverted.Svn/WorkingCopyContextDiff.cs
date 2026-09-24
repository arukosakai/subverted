using Microsoft.Data.Sqlite;
using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// A file's local changes with any amount of context, written in-process from its pristine and
/// its working file, for the cases where that is provably the diff <c>svn diff</c> would print.
/// Everything else is declined, and the caller asks <see cref="SvnDiffCommand"/> instead.
/// </summary>
/// <param name="newline">What ends SVN's own lines here — the platform's newline.</param>
public sealed class WorkingCopyContextDiff(ISvnTextSpelling spelling, string newline)
{
    /// <param name="path">Absolute. Only a single file is ever answered; a folder is declined.</param>
    /// <returns>
    /// The diff, empty when the file is unchanged; <see langword="null"/> when this file's diff has to
    /// come from <c>svn diff</c> — see <see cref="PristineBaseRow.ComparableLineEndings"/>, plus a
    /// working file that is missing, not a plain file, mixes line endings, or changed too much, and
    /// a wc.db this build cannot read.
    /// </returns>
    public async Task<string?> ReadAsync(
        string workingCopyRoot,
        string path,
        DiffContext context,
        CancellationToken cancellationToken
    )
    {
        var relPath = Path.GetRelativePath(workingCopyRoot, path).Replace('\\', '/');
        if (relPath == ".")
        {
            return null;
        }

        var row = ReadRow(workingCopyRoot, relPath);
        if (
            row?.ComparableLineEndings is not { } lineEndings
            || SvnChecksum.TryParseSha1(row.Checksum) is not { } sha1
        )
        {
            return null;
        }

        var pristine = await ReadPlainFileAsync(PristinePath(workingCopyRoot, sha1), cancellationToken);
        var working = await ReadPlainFileAsync(path, cancellationToken);
        if (
            pristine is null
            || pristine.Length != row.PristineSize
            || working is null
            || LineEndingNormalForm.Of(working, lineEndings) is not { } normalWorking
        )
        {
            return null;
        }

        var header = new DiffSectionHeader(relPath, $"revision {row.Revision}", "working copy");
        var diff = SvnStyleDiff.Write(header, pristine, normalWorking, context.LinesAround, newline);
        return diff is null ? null : SvnOutputText.Decode(diff, spelling.LinesThatAreNotUtf8);
    }

    /// <remarks>A wc.db this build cannot read is <c>svn diff</c>'s to answer, as status falls back (D14).</remarks>
    private static PristineBaseRow? ReadRow(string workingCopyRoot, string relPath)
    {
        try
        {
            using var reader = WcDbReader.Open(workingCopyRoot);
            return reader.ReadPristineBase(relPath);
        }
        catch (WcDbException)
        {
            return null;
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    /// <remarks>The layout of formats 29 to 31: one folder per first two hex digits.</remarks>
    private static string PristinePath(string workingCopyRoot, string sha1) =>
        Path.Combine(workingCopyRoot, ".svn", "pristine", sha1[..2], $"{sha1}.svn-base");

    /// <returns>Null for anything but a readable, plain file: gone, a folder, a link, or held.</returns>
    private static async Task<byte[]?> ReadPlainFileAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                return null;
            }

            return await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
