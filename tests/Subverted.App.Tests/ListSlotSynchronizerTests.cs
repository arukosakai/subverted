using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Subverted.App.Presentation;

namespace Subverted.App.Tests;

/// <summary>
/// Every case asserts the end state, the collection edits and which slots survived: one that
/// rebuilt its slots would pass the first and drop the list's selection every second in the app.
/// </summary>
public sealed class ListSlotSynchronizerTests
{
    [Test]
    public async Task An_identical_layout_makes_no_edit_and_updates_no_slot()
    {
        var shown = Shown("a:1", "b:1");
        var slots = shown.ToList();
        var edits = Record(shown);

        Apply(shown, "a:1", "b:1");

        await Assert.That(edits).IsEmpty();
        await Assert.That(slots.Sum(slot => slot.Updates)).IsEqualTo(0);
        await Assert.That(Contents(shown)).IsEqualTo("a:1,b:1");
    }

    [Test]
    public async Task A_changed_line_keeps_its_slot_and_only_its_content_moves()
    {
        var shown = Shown("a:1", "b:1");
        var b = shown[1];
        var edits = Record(shown);

        Apply(shown, "a:1", "b:2");

        await Assert.That(edits).IsEmpty();
        await Assert.That(shown[1]).IsSameReferenceAs(b);
        await Assert.That(b.Updates).IsEqualTo(1);
        await Assert.That(shown[0].Updates).IsEqualTo(0);
        await Assert.That(Contents(shown)).IsEqualTo("a:1,b:2");
    }

    [Test]
    public async Task A_new_key_gets_a_new_slot_where_it_belongs()
    {
        var shown = Shown("a:1", "c:1");
        var edits = Record(shown);

        Apply(shown, "a:1", "b:1", "c:1");

        await Assert.That(Contents(shown)).IsEqualTo("a:1,b:1,c:1");
        await Assert.That(edits).IsEquivalentTo(new[] { NotifyCollectionChangedAction.Add });
    }

    [Test]
    public async Task A_key_that_went_away_loses_its_slot_and_nothing_else_moves()
    {
        var shown = Shown("a:1", "b:1", "c:1");
        var edits = Record(shown);

        Apply(shown, "a:1", "c:1");

        await Assert.That(Contents(shown)).IsEqualTo("a:1,c:1");
        await Assert.That(edits).IsEquivalentTo(new[] { NotifyCollectionChangedAction.Remove });
    }

    /// <summary>A line pinned to the top moves; it is the same slot afterwards, with its new content.</summary>
    [Test]
    public async Task A_moved_key_takes_its_slot_with_it()
    {
        var shown = Shown("a:1", "b:1", "c:1");
        var c = shown[2];

        Apply(shown, "c:2", "a:1", "b:1");

        await Assert.That(Contents(shown)).IsEqualTo("c:2,a:1,b:1");
        await Assert.That(shown[0]).IsSameReferenceAs(c);
        await Assert.That(c.Updates).IsEqualTo(1);
    }

    [Test]
    public async Task An_empty_layout_empties_the_list_and_an_empty_list_fills()
    {
        var shown = Shown("a:1");

        Apply(shown);
        var emptied = shown.Count;
        Apply(shown, "x:1", "y:1");

        await Assert.That(emptied).IsEqualTo(0);
        await Assert.That(Contents(shown)).IsEqualTo("x:1,y:1");
    }

    [Test]
    public async Task A_reversed_layout_ends_reversed_with_every_slot_kept()
    {
        var shown = Shown("a:1", "b:1", "c:1");
        var slots = shown.ToList();

        Apply(shown, "c:1", "b:1", "a:1");

        await Assert.That(Contents(shown)).IsEqualTo("c:1,b:1,a:1");
        await Assert.That(shown).IsEquivalentTo(slots);
    }

    private static void Apply(ObservableCollection<Slot> shown, params string[] fresh) =>
        ListSlotSynchronizer.Apply(shown, fresh, KeyOf, content => new Slot(content));

    private static string KeyOf(string content) => content.Split(':')[0];

    private static ObservableCollection<Slot> Shown(params string[] contents) =>
        new(contents.Select(content => new Slot(content)));

    private static string Contents(IEnumerable<Slot> slots) =>
        string.Join(",", slots.Select(slot => slot.Content));

    private static List<NotifyCollectionChangedAction> Record(ObservableCollection<Slot> shown)
    {
        var edits = new List<NotifyCollectionChangedAction>();
        shown.CollectionChanged += (_, change) => edits.Add(change.Action);
        return edits;
    }

    private sealed class Slot(string content) : IListSlot<string>
    {
        private string _content = content;

        public int Updates { get; private set; }

        public string Content
        {
            get => _content;
            set
            {
                _content = value;
                Updates++;
            }
        }
    }
}
