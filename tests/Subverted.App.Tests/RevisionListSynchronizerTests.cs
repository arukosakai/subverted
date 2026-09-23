using System.Collections.ObjectModel;
using Subverted.App.Presentation;

namespace Subverted.App.Tests;

public sealed class RevisionListSynchronizerTests
{
    [Test]
    public async Task A_page_arriving_at_the_bottom_leaves_every_shown_row_where_it_was()
    {
        var first = Items(9, 8);
        var shown = new ObservableCollection<RevisionListItem>(first);
        var edits = Edits(shown);

        RevisionListSynchronizer.Apply(shown, [.. first, .. Items(7, 6)]);

        await Assert.That(shown.Select(item => item.Row.Revision)).IsEquivalentTo([9L, 8L, 7L, 6L]);
        await Assert.That(shown[0]).IsSameReferenceAs(first[0]);
        await Assert.That(shown[1]).IsSameReferenceAs(first[1]);
        await Assert.That(edits).IsEquivalentTo(["Add", "Add"]);
    }

    [Test]
    public async Task A_narrowing_search_removes_rows_and_widening_it_puts_them_back_in_order()
    {
        var all = Items(9, 8, 7, 6);
        var shown = new ObservableCollection<RevisionListItem>(all);

        RevisionListSynchronizer.Apply(shown, [all[1], all[3]]);
        await Assert.That(shown.Select(item => item.Row.Revision)).IsEquivalentTo([8L, 6L]);

        RevisionListSynchronizer.Apply(shown, all);
        await Assert
            .That(shown.Select(item => item.Row.Revision))
            .IsEquivalentTo([9L, 8L, 7L, 6L], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_row_whose_marker_moved_is_replaced_in_place()
    {
        var before = Items(9, 8);
        var shown = new ObservableCollection<RevisionListItem>(before);
        var marked = before[1] with { BaseMarkerAbove = "Your copy is at r8" };
        var edits = Edits(shown);

        RevisionListSynchronizer.Apply(shown, [before[0], marked]);

        await Assert.That(shown[1]).IsSameReferenceAs(marked);
        await Assert.That(shown[0]).IsSameReferenceAs(before[0]);
        await Assert.That(edits).IsEquivalentTo(["Replace"]);
    }

    [Test]
    public async Task An_empty_list_fills_and_empties()
    {
        var shown = new ObservableCollection<RevisionListItem>();

        RevisionListSynchronizer.Apply(shown, Items(3, 2));
        await Assert.That(shown.Count).IsEqualTo(2);

        RevisionListSynchronizer.Apply(shown, []);
        await Assert.That(shown).IsEmpty();
    }

    private static List<RevisionListItem> Items(params long[] revisions) =>
        [
            .. revisions.Select(revision => new RevisionListItem(
                Revisions.Row(revision),
                null,
                null
            )),
        ];

    private static List<string> Edits(ObservableCollection<RevisionListItem> shown)
    {
        var edits = new List<string>();
        shown.CollectionChanged += (_, e) => edits.Add(e.Action.ToString());
        return edits;
    }
}
