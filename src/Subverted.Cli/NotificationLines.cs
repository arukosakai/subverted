namespace Subverted.Cli;

/// <summary>
/// SVN's per-path notification text as the lines to print. Its wording is passed through — the
/// letters in column one are the same ones <c>sv st</c> shows, and rewriting them would give the
/// studio two vocabularies for one thing.
/// </summary>
public static class NotificationLines
{
    /// <returns>One line per path, and nothing at all when SVN printed nothing.</returns>
    public static IReadOnlyList<string> Of(string notifications)
    {
        var trimmed = notifications.TrimEnd('\n', '\r');
        return trimmed.Length == 0
            ? []
            : [.. trimmed.Split('\n').Select(line => line.TrimEnd('\r'))];
    }

    /// <param name="whenNothingHappened">
    /// What to say when SVN printed nothing, which it does for a command that had nothing to do.
    /// Silence would leave the user unable to tell that from a command that never ran.
    /// </param>
    public static IReadOnlyList<string> OrElse(string notifications, string whenNothingHappened) =>
        Of(notifications) is { Count: > 0 } lines ? lines : [whenNothingHappened];
}
