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
    public async Task An_empty_diff_is_explained_by_why_the_row_is_listed(
        NodeStatus status,
        bool copied,
        string message
    )
    {
        var row = ChangeRow.From(Entry("a.png", status, isCopied: copied));

        await Assert.That(EmptyDiffMessage.For(row)).IsEqualTo(message);
    }

    /// <summary>
    /// Listing all, a file with nothing to report is said to be unchanged — against the revision
    /// it was updated to, or against its copy's source when a copied folder carried it.
    /// </summary>
    [Test]
    [Arguments(false, "Unchanged: it matches the revision you last updated to.")]
    [Arguments(true, "Unchanged since the copy that carried it, so it matches its source.")]
    public async Task An_unmodified_file_is_said_to_be_unchanged(bool copied, string message)
    {
        var row = ChangeRow.From(Entry("a.png", NodeStatus.Unmodified, isCopied: copied));

        await Assert.That(EmptyDiffMessage.For(row)).IsEqualTo(message);
    }

    /// <summary>Unchanged content with changed properties is a change, and is not called unchanged.</summary>
    [Test]
    public async Task A_file_whose_properties_alone_changed_is_not_called_unchanged()
    {
        var propsOnly = ChangeRow.From(
            Entry("a.png", NodeStatus.Unmodified, PropertyStatus.Modified)
        );

        await Assert.That(EmptyDiffMessage.For(propsOnly)).IsEqualTo(Otherwise);
    }

    /// <summary>A lock changes nothing in the file, so a lock alone still reads as unchanged.</summary>
    [Test]
    public async Task A_file_listed_only_for_its_lock_is_said_to_be_unchanged()
    {
        var locked = ChangeRow.From(Entry("a.png", NodeStatus.Unmodified, hasLockToken: true));

        await Assert
            .That(EmptyDiffMessage.For(locked))
            .IsEqualTo("Unchanged: it matches the revision you last updated to.");
    }
}
