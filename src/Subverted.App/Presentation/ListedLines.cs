using Subverted.Core;

namespace Subverted.App.Presentation;

/// <summary>Which of a listing's rows the table and the directory tree are drawn from.</summary>
public static class ListedLines
{
    /// <param name="rows">Every row the daemon listed, in the order they should show.</param>
    /// <returns>
    /// For <see cref="ListedNodes.Changes"/>, what <c>svn status</c> prints — the changes, and any file
    /// holding a lock — whatever the listing held. For
    /// <see cref="ListedNodes.All"/>, unmodified files as well; an unmodified folder is not a line,
    /// because the tree already shows it through the files inside it.
    /// </returns>
    public static IReadOnlyList<ChangeRow> Of(IReadOnlyList<ChangeRow> rows, ListedNodes listed) =>
        listed == ListedNodes.All
            ? [.. rows.Where(row => !row.IsClean || row.Entry.Kind != NodeKind.Directory)]
            : [.. rows.Where(row => !row.IsClean)];
}
