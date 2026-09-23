using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>What the pane says when SVN printed nothing for a listed row.</summary>
public sealed class EmptyDiffMessageTests
{
    private const string Unversioned =
        "Not under version control yet, so SVN has nothing to compare it with.";
    private const string Missing =
        "Missing from disk, so there is nothing here to compare with the committed version.";
    private const string Copied =
        "Copied or moved without edits since, so it matches where it came from.";
    private const string Added = "Added, with no content of its own to show.";
    private const string Otherwise = "SVN reports no differences here.";

    [Test]
    [Arguments(NodeStatus.Unversioned, false, Unversioned)]
    [Arguments(NodeStatus.Missing, false, Missing)]
    [Arguments(NodeStatus.Missing, true, Missing)]
    [Arguments(NodeStatus.Obstructed, false, Missing)]
    [Arguments(NodeStatus.Added, true, Copied)]
    [Arguments(NodeStatus.Added, false, Added)]
    [Arguments(NodeStatus.Modified, true, Otherwise)]
    [Arguments(NodeStatus.Unmodified, false, Otherwise)]
    public async Task An_empty_diff_is_explained_by_why_the_row_is_listed(
        NodeStatus status,
        bool copied,
        string message
    )
    {
        var row = ChangeRow.From(Entry("a.png", status, isCopied: copied));

        await Assert.That(EmptyDiffMessage.For(row)).IsEqualTo(message);
    }
}
