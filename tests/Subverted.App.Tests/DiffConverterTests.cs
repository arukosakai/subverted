using System.Globalization;
using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.App.Views;
using Subverted.Frontend.Diff;
using TUnit.Assertions.Enums;

namespace Subverted.App.Tests;

public sealed class DiffConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static IEnumerable<Func<DiffLayout>> Layouts() =>
        [() => DiffLayout.Split, () => DiffLayout.Unified];

    [Test]
    [MethodDataSource(nameof(Layouts))]
    public async Task A_document_converts_to_the_rows_of_the_layout_it_is_given(DiffLayout layout)
    {
        var rows =
            (IReadOnlyList<DiffRow>)
                FlatDiff.Rows.Convert(
                    [Diffs.Modified, layout, "src/Player.cs"],
                    typeof(object),
                    null,
                    Culture
                )!;

        await Assert
            .That(rows)
            .IsEquivalentTo(
                layout.RowsOf(Diffs.Modified, "src/Player.cs"),
                CollectionOrdering.Matching
            );
    }

    public static IEnumerable<Func<object?[]>> NotADocumentAndALayout() =>
        [
            () => [null, DiffLayout.Split, "a.txt"],
            () => ["not a document", DiffLayout.Split, "a.txt"],
            () => [Diffs.Modified, null, "a.txt"],
            () => [Diffs.Modified, "not a layout", "a.txt"],
            () => [Diffs.Modified, DiffLayout.Split, null],
            () => [Diffs.Modified, DiffLayout.Split],
            () => [Diffs.Modified, DiffLayout.Split, "a.txt", "a.txt"],
        ];

    [Test]
    [MethodDataSource(nameof(NotADocumentAndALayout))]
    public async Task Anything_but_a_document_and_a_layout_lists_no_rows(object?[] values)
    {
        var rows =
            (IReadOnlyList<DiffRow>)FlatDiff.Rows.Convert(values, typeof(object), null, Culture)!;

        await Assert.That(rows).IsEmpty();
    }

    [Test]
    [Arguments("Split", true, false)]
    [Arguments("Unified", false, true)]
    public async Task A_layout_takes_its_own_style_class_only(string name, bool split, bool unified)
    {
        var layout = name == "Split" ? DiffLayout.Split : DiffLayout.Unified;

        await Assert
            .That(LayoutIs.Split.Convert(layout, typeof(bool), null, Culture))
            .IsEqualTo(split);
        await Assert
            .That(LayoutIs.Unified.Convert(layout, typeof(bool), null, Culture))
            .IsEqualTo(unified);
    }

    [Test]
    public async Task Anything_but_a_layout_takes_no_layout_style_class()
    {
        await Assert
            .That((bool)LayoutIs.Split.Convert("Split", typeof(bool), null, Culture)!)
            .IsFalse();
    }

    [Test]
    public async Task A_size_converts_to_its_readable_text()
    {
        await Assert
            .That(FileSizeText.Human.Convert(1536L, typeof(string), null, Culture))
            .IsEqualTo("1.5 KB");
    }

    [Test]
    public async Task The_size_is_written_in_the_bindings_culture()
    {
        await Assert
            .That(
                FileSizeText.Human.Convert(
                    1536L,
                    typeof(string),
                    null,
                    FileSizeTests.DecimalComma()
                )
            )
            .IsEqualTo("1,5 KB");
    }

    /// <summary>A file that is not on disk has no size, and the card leaves the line out.</summary>
    [Test]
    [Arguments(null)]
    [Arguments(1536)]
    public async Task Anything_but_a_byte_count_has_no_size_text(object? value)
    {
        await Assert
            .That(FileSizeText.Human.Convert(value, typeof(string), null, Culture))
            .IsNull();
    }

    public static IEnumerable<Func<(DiffPaneStateIs, DiffPaneState)>> PaneStates() =>
        [
            () => (DiffPaneStateIs.NothingSelected, DiffPaneState.NothingSelected),
            () => (DiffPaneStateIs.Loading, DiffPaneState.Loading),
            () => (DiffPaneStateIs.Ready, DiffPaneState.Ready),
            () => (DiffPaneStateIs.NothingToShow, DiffPaneState.NothingToShow),
            () => (DiffPaneStateIs.Unreachable, DiffPaneState.Unreachable),
            () => (DiffPaneStateIs.Failed, DiffPaneState.Failed),
        ];

    [Test]
    [MethodDataSource(nameof(PaneStates))]
    public async Task Each_part_of_the_diff_pane_shows_in_its_own_state_and_no_other(
        DiffPaneStateIs converter,
        DiffPaneState own
    )
    {
        var shownIn = Enum.GetValues<DiffPaneState>()
            .Where(state => (bool)converter.Convert(state, typeof(bool), null, Culture)!)
            .ToList();

        await Assert.That(shownIn).IsEquivalentTo([own], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Anything_but_a_pane_state_shows_nothing()
    {
        await Assert
            .That((bool)DiffPaneStateIs.Ready.Convert("Ready", typeof(bool), null, Culture)!)
            .IsFalse();
    }

    [Test]
    [Arguments(DiffLineKind.Added, true, false)]
    [Arguments(DiffLineKind.Removed, false, true)]
    [Arguments(DiffLineKind.Context, false, false)]
    public async Task A_line_takes_the_style_class_of_its_kind_only(
        DiffLineKind kind,
        bool added,
        bool removed
    )
    {
        await Assert
            .That(LineKindIs.Added.Convert(kind, typeof(bool), null, Culture))
            .IsEqualTo(added);
        await Assert
            .That(LineKindIs.Removed.Convert(kind, typeof(bool), null, Culture))
            .IsEqualTo(removed);
    }

    [Test]
    public async Task Anything_but_a_line_kind_takes_no_style_class()
    {
        await Assert
            .That((bool)LineKindIs.Added.Convert("Added", typeof(bool), null, Culture)!)
            .IsFalse();
    }

    [Test]
    public async Task No_diff_converter_pretends_to_convert_back()
    {
        await Assert
            .That(() => LayoutIs.Split.ConvertBack(true, typeof(DiffLayout), null, Culture))
            .Throws<NotSupportedException>();
        await Assert
            .That(() => FileSizeText.Human.ConvertBack("1 KB", typeof(long), null, Culture))
            .Throws<NotSupportedException>();
        await Assert
            .That(() =>
                DiffPaneStateIs.Ready.ConvertBack(true, typeof(DiffPaneState), null, Culture)
            )
            .Throws<NotSupportedException>();
        await Assert
            .That(() => LineKindIs.Added.ConvertBack(true, typeof(DiffLineKind), null, Culture))
            .Throws<NotSupportedException>();
    }
}
