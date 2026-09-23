using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>
/// Every case asserts the end state and the edits it took: a synchronizer that cleared and refilled
/// would pass the first and flicker every second in the app.
/// </summary>
public sealed class ChangeListSynchronizerTests
{
    [Test]
    public async Task An_identical_listing_makes_no_edit_at_all()
    {
        var shown = Shown("a", "b", "c");
        var edits = Record(shown);

        ChangeListSynchronizer.Apply(shown, Rows("a", "b", "c"));

        await Assert.That(edits).IsEmpty();
        await Assert.That(Paths(shown)).IsEqualTo("a,b,c");
    }

    [Test]
    public async Task A_new_row_is_inserted_where_it_belongs()
    {
        var shown = Shown("a", "c");
        var edits = Record(shown);

        ChangeListSynchronizer.Apply(shown, Rows("a", "b", "c"));

        await Assert.That(Paths(shown)).IsEqualTo("a,b,c");
        await Assert.That(edits).IsEquivalentTo(new[] { NotifyCollectionChangedAction.Add });
    }

    [Test]
    public async Task A_row_that_went_away_is_removed_and_nothing_else_moves()
    {
        var shown = Shown("a", "b", "c");
        var edits = Record(shown);

        ChangeListSynchronizer.Apply(shown, Rows("a", "c"));

        await Assert.That(Paths(shown)).IsEqualTo("a,c");
        await Assert.That(edits).IsEquivalentTo(new[] { NotifyCollectionChangedAction.Remove });
    }

    /// <summary>A file edited again changes its badge, so the row is replaced — once, in place.</summary>
    [Test]
    public async Task A_changed_row_is_replaced_in_place()
    {
        var shown = Shown("a", "b");
        var edits = Record(shown);
        var changed = ChangeRow.From(Entry("b", NodeStatus.Conflicted));

        ChangeListSynchronizer.Apply(shown, [ChangeRow.From(Entry("a")), changed]);

        await Assert.That(shown[1]).IsEqualTo(changed);
        await Assert.That(edits).IsEquivalentTo(new[] { NotifyCollectionChangedAction.Replace });
    }

    /// <summary>
    /// Equal but not the same instance — what every refresh produces — has to leave the shown row
    /// alone, or the list's selection is dropped once a second.
    /// </summary>
    [Test]
    public async Task An_equal_row_keeps_the_instance_already_shown()
    {
        var shown = Shown("a");
        var original = shown[0];

        ChangeListSynchronizer.Apply(shown, Rows("a"));

        await Assert.That(ReferenceEquals(shown[0], original)).IsTrue();
    }

    [Test]
    public async Task A_row_that_moved_ends_up_in_the_new_order()
    {
        var shown = Shown("a", "b", "c");

        ChangeListSynchronizer.Apply(shown, Rows("c", "a", "b"));

        await Assert.That(Paths(shown)).IsEqualTo("c,a,b");
    }

    [Test]
    public async Task An_empty_listing_empties_the_list()
    {
        var shown = Shown("a", "b");

        ChangeListSynchronizer.Apply(shown, []);

        await Assert.That(shown).IsEmpty();
    }

    [Test]
    public async Task A_first_listing_fills_an_empty_list()
    {
        var shown = Shown();

        ChangeListSynchronizer.Apply(shown, Rows("a", "b"));

        await Assert.That(Paths(shown)).IsEqualTo("a,b");
    }

    private static ObservableCollection<ChangeRow> Shown(params string[] paths) => [.. Rows(paths)];

    private static IReadOnlyList<ChangeRow> Rows(params string[] paths) =>
        [.. paths.Select(path => ChangeRow.From(Entry(path)))];

    private static List<NotifyCollectionChangedAction> Record(ObservableCollection<ChangeRow> rows)
    {
        var edits = new List<NotifyCollectionChangedAction>();
        rows.CollectionChanged += (_, change) => edits.Add(change.Action);
        return edits;
    }

    private static string Paths(IEnumerable<ChangeRow> rows) =>
        string.Join(",", rows.Select(row => row.RelPath));
}
