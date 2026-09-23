using Subverted.App.Presentation;
using Subverted.Core;

namespace Subverted.App.Tests;

public sealed class RevisionDiffEmptyMessageTests
{
    [Test]
    public async Task An_unedited_copy_says_where_it_came_from()
    {
        var path = ChangedPathRow.From(new ChangedPath("/b.txt", PathChange.Added, "/a.txt", 2));

        await Assert
            .That(RevisionDiffEmptyMessage.For(path))
            .IsEqualTo("Copied from /a.txt@2 without edits, so it matches where it came from.");
    }

    [Test]
    public async Task Anything_else_says_svn_showed_no_change()
    {
        var path = ChangedPathRow.From(new ChangedPath("/dir", PathChange.Added, null, null));

        await Assert
            .That(RevisionDiffEmptyMessage.For(path))
            .IsEqualTo("SVN shows no content or property changes for this path in this revision.");
    }
}
