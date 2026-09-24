namespace Subverted.Svn;

/// <summary>
/// Tells a <c>lock</c> or <c>unlock</c> that only refused paths from one that failed. svn 1.14
/// exits one when any path is refused — 1.8.15 exits zero — and follows the warnings with a single
/// <c>E200009</c> summarising them; the paths it did not refuse were still locked or released.
/// </summary>
public static class SvnRefusalSummary
{
    private const string ErrorPrefix = "svn: E";
    private const string SummaryPrefix = "svn: E200009:";

    /// <returns>
    /// <see langword="true"/> when stderr holds at least one refusal and no error but the summary,
    /// so stdout and <see cref="SvnWarnings"/> say what happened exactly as after a zero exit.
    /// </returns>
    public static bool IsAllThatFailed(string standardError)
    {
        var lines = standardError.Split('\n').Select(line => line.Trim()).ToList();
        var errors = lines
            .Where(line => line.StartsWith(ErrorPrefix, StringComparison.Ordinal))
            .ToList();

        var onlyTheSummaryFailed =
            errors.Count > 0
            && errors.All(error => error.StartsWith(SummaryPrefix, StringComparison.Ordinal));

        return onlyTheSummaryFailed && SvnWarnings.From(standardError).Count > 0;
    }
}
