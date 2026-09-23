using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// A unified diff as the lines to print. SVN's text is passed through unchanged — only the colour
/// is ours, and it is chosen from the line's first character the way every diff viewer does it.
/// </summary>
public static class DiffReport
{
    public static IReadOnlyList<string> Lines(DiffResponse response, PaintDiff paint)
    {
        var unifiedDiff = response.UnifiedDiff;
        if (unifiedDiff.Length == 0)
        {
            return ["no local changes"];
        }

        return
        [
            .. unifiedDiff
                .TrimEnd('\n', '\r')
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Select(line => paint(line, Classify(line))),
        ];
    }

    /// <summary>
    /// Order matters: <c>+++</c> and <c>---</c> are file headers, not an added and a removed line,
    /// and colouring them as content is what makes a diff hard to read.
    /// </summary>
    private static DiffLine Classify(string line) =>
        line switch
        {
            _ when line.StartsWith("+++", StringComparison.Ordinal) => DiffLine.FileHeader,
            _ when line.StartsWith("---", StringComparison.Ordinal) => DiffLine.FileHeader,
            _ when line.StartsWith("Index:", StringComparison.Ordinal) => DiffLine.FileHeader,
            _ when line.StartsWith("===", StringComparison.Ordinal) => DiffLine.FileHeader,
            _ when line.StartsWith("@@", StringComparison.Ordinal) => DiffLine.HunkHeader,
            _ when line.StartsWith('+') => DiffLine.Added,
            _ when line.StartsWith('-') => DiffLine.Removed,
            _ => DiffLine.Context,
        };
}
