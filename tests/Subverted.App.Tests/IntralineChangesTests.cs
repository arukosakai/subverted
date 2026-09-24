using Subverted.App.Presentation;
using TUnit.Assertions.Enums;

namespace Subverted.App.Tests;

/// <summary>
/// Expectations are written as the line with its changed spans bracketed in ⟦ ⟧, which reads as the
/// highlight looks and still pins every offset exactly.
/// </summary>
public sealed class IntralineChangesTests
{
    [Test]
    public async Task Identical_lines_mark_nothing_on_either_side()
    {
        var changes = IntralineChanges.Between("same line", "same line");

        await Assert.That(changes.Old).IsEmpty();
        await Assert.That(changes.New).IsEmpty();
    }

    [Test]
    [Arguments("", "")]
    [Arguments("", "value = 1;")]
    [Arguments("value = 1;", "")]
    [Arguments("alpha", "omega")]
    [Arguments("   ", "\t")]
    public async Task Lines_that_share_no_text_mark_nothing_because_the_whole_line_changed(
        string oldText,
        string newText
    )
    {
        await Assert
            .That(IntralineChanges.Between(oldText, newText))
            .IsSameReferenceAs(IntralineChanges.None);
    }

    [Test]
    public async Task Sharing_only_whitespace_is_sharing_nothing()
    {
        await Assert
            .That(IntralineChanges.Between("foo bar", "baz qux"))
            .IsSameReferenceAs(IntralineChanges.None);
    }

    [Test]
    public async Task Sharing_one_word_is_enough_to_mark_the_rest()
    {
        await Assert
            .That(Marked("foo bar", "foo qux"))
            .IsEqualTo(("foo ⟦bar⟧", "foo ⟦qux⟧"));
    }

    [Test]
    public async Task Sharing_one_symbol_is_enough_to_mark_the_rest()
    {
        await Assert.That(Marked("a;", "b;")).IsEqualTo(("⟦a⟧;", "⟦b⟧;"));
    }

    [Test]
    public async Task Text_added_in_the_middle_is_marked_on_the_new_side_only()
    {
        await Assert
            .That(Marked("position += velocity;", "position += velocity * delta;"))
            .IsEqualTo(("position += velocity;", "position += velocity⟦ * delta⟧;"));
    }

    [Test]
    public async Task Text_removed_from_the_middle_is_marked_on_the_old_side_only()
    {
        await Assert
            .That(Marked("position += velocity * delta;", "position += velocity;"))
            .IsEqualTo(("position += velocity⟦ * delta⟧;", "position += velocity;"));
    }

    [Test]
    public async Task A_change_at_the_start_of_the_line_is_marked()
    {
        await Assert
            .That(Marked("var x = 1;", "int x = 1;"))
            .IsEqualTo(("⟦var⟧ x = 1;", "⟦int⟧ x = 1;"));
    }

    [Test]
    public async Task A_change_at_the_end_of_the_line_is_marked()
    {
        await Assert
            .That(Marked("count = 1", "count = 2"))
            .IsEqualTo(("count = ⟦1⟧", "count = ⟦2⟧"));
    }

    [Test]
    public async Task A_whitespace_only_change_marks_the_whitespace()
    {
        await Assert.That(Marked("a = b", "a  = b")).IsEqualTo(("a⟦ ⟧= b", "a⟦  ⟧= b"));
    }

    [Test]
    public async Task A_tab_turned_into_spaces_marks_the_indentation()
    {
        await Assert
            .That(Marked("\tJump();", "    Jump();"))
            .IsEqualTo(("⟦\t⟧Jump();", "⟦    ⟧Jump();"));
    }

    [Test]
    public async Task Trailing_whitespace_added_is_marked_at_the_end()
    {
        await Assert.That(Marked("end", "end  ")).IsEqualTo(("end", "end⟦  ⟧"));
    }

    [Test]
    public async Task Changes_separated_only_by_whitespace_read_as_one()
    {
        await Assert
            .That(Marked("let a b;", "let c d;"))
            .IsEqualTo(("let ⟦a b⟧;", "let ⟦c d⟧;"));
    }

    [Test]
    public async Task Changes_separated_by_unchanged_text_stay_apart()
    {
        await Assert
            .That(Marked("let a,b;", "let c,d;"))
            .IsEqualTo(("let ⟦a⟧,⟦b⟧;", "let ⟦c⟧,⟦d⟧;"));
    }

    /// <summary>Words are compared whole: <c>velocity</c> to <c>speed</c>, not letter by letter.</summary>
    [Test]
    public async Task A_renamed_word_is_marked_whole_even_where_letters_coincide()
    {
        await Assert
            .That(Marked("x = velocity;", "x = speed;"))
            .IsEqualTo(("x = ⟦velocity⟧;", "x = ⟦speed⟧;"));
    }

    [Test]
    public async Task Separate_insertions_between_shared_tokens_are_each_marked()
    {
        await Assert
            .That(Marked("Draw(sprite, x, y)", "Draw(sprite, x + 1, y, layer)"))
            .IsEqualTo(("Draw(sprite, x, y)", "Draw(sprite, x⟦ + 1⟧, y⟦, layer⟧)"));
    }

    [Test]
    public async Task Text_removed_before_the_end_is_marked_up_to_the_shared_end()
    {
        await Assert.That(Marked("f(a, b)", "f(a)")).IsEqualTo(("f(a⟦, b⟧)", "f(a)"));
    }

    /// <summary>The shared start and the shared end never claim the same token twice.</summary>
    [Test]
    public async Task A_repeated_symbol_added_at_the_end_is_marked_once()
    {
        await Assert.That(Marked("x..", "x...")).IsEqualTo(("x..", "x..⟦.⟧"));
    }

    [Test]
    public async Task A_repeated_symbol_removed_from_the_start_is_marked_once()
    {
        await Assert.That(Marked("...x", "..x")).IsEqualTo(("..⟦.⟧x", "..x"));
    }

    /// <summary>Either word could be the one kept; the old side's is the one given up first.</summary>
    [Test]
    public async Task Swapped_words_keep_one_and_mark_the_other_on_each_side()
    {
        await Assert.That(Marked("x a b", "x b a")).IsEqualTo(("x ⟦a ⟧b", "x b⟦ a⟧"));
    }

    /// <summary>😀 and 😃 share their high surrogate; marking one code unit would split a character.</summary>
    [Test]
    public async Task A_changed_emoji_is_marked_whole_not_by_its_differing_code_unit()
    {
        var changes = IntralineChanges.Between("icon = 😀;", "icon = 😃;");

        await Assert
            .That(changes.Old)
            .IsEquivalentTo([new ChangedSpan(7, 2)], CollectionOrdering.Matching);
        await Assert
            .That(changes.New)
            .IsEquivalentTo([new ChangedSpan(7, 2)], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_combining_accent_is_marked_with_the_word_it_sits_on()
    {
        await Assert
            .That(Marked("name = cafe\u0301;", "name = cafe;"))
            .IsEqualTo(("name = ⟦cafe\u0301⟧;", "name = ⟦cafe⟧;"));
    }

    /// <summary>
    /// 100 × 200 differing tokens: exactly the cap, so the shared dots stay unmarked. The old side's
    /// 98 dots match the new side's first 98; the new side's other 100 are what was inserted.
    /// </summary>
    [Test]
    public async Task At_the_cap_the_lines_are_still_compared_token_by_token()
    {
        var (oldMiddleTokens, newMiddleTokens) = (100, 200);
        var dots = new string('.', oldMiddleTokens - 2);
        var inserted = new string('.', newMiddleTokens - oldMiddleTokens);
        await Assert
            .That(oldMiddleTokens * newMiddleTokens)
            .IsEqualTo(IntralineChanges.MaxComparedTokenPairs);

        await Assert
            .That(Marked($"x=A{dots}B", $"x=C{dots}{inserted}D"))
            .IsEqualTo(($"x=⟦A⟧{dots}⟦B⟧", $"x=⟦C⟧{dots}⟦{inserted}D⟧"));
    }

    /// <summary>177 × 113 differing tokens is one pair past the cap: the whole middle is marked.</summary>
    [Test]
    public async Task One_pair_past_the_cap_marks_everything_between_the_shared_ends()
    {
        var (oldMiddleTokens, newMiddleTokens) = (177, 113);
        var oldDots = new string('.', oldMiddleTokens - 2);
        var newDots = new string('.', newMiddleTokens - 2);
        await Assert
            .That(oldMiddleTokens * newMiddleTokens)
            .IsEqualTo(IntralineChanges.MaxComparedTokenPairs + 1);

        await Assert
            .That(Marked($"x=A{oldDots}B", $"x=C{newDots}D"))
            .IsEqualTo(($"x=⟦A{oldDots}B⟧", $"x=⟦C{newDots}D⟧"));
    }

    /// <summary>Past the cap only the shared ends count as kept, so with none the line is simply changed.</summary>
    [Test]
    public async Task Past_the_cap_with_no_shared_ends_nothing_is_marked()
    {
        var oldDots = new string('.', 175);
        var newDots = new string('.', 111);

        await Assert
            .That(IntralineChanges.Between($"A{oldDots}B", $"C{newDots}D"))
            .IsSameReferenceAs(IntralineChanges.None);
    }

    [Test]
    public async Task At_the_cap_with_no_shared_ends_the_shared_middle_still_counts()
    {
        var dots = new string('.', 98);
        var inserted = new string('.', 100);

        await Assert
            .That(Marked($"A{dots}B", $"C{dots}{inserted}D"))
            .IsEqualTo(($"⟦A⟧{dots}⟦B⟧", $"⟦C⟧{dots}⟦{inserted}D⟧"));
    }

    [Test]
    public async Task An_old_line_exactly_at_the_length_cap_is_still_compared()
    {
        var run = new string('a', IntralineChanges.MaxComparedLineLength - 2);

        await Assert.That(Marked($"k={run}", "k=b")).IsEqualTo(($"k=⟦{run}⟧", "k=⟦b⟧"));
    }

    [Test]
    public async Task An_old_line_one_character_past_the_length_cap_is_not_compared()
    {
        var run = new string('a', IntralineChanges.MaxComparedLineLength - 1);

        await Assert
            .That(IntralineChanges.Between($"k={run}", "k=b"))
            .IsSameReferenceAs(IntralineChanges.None);
    }

    [Test]
    public async Task A_new_line_exactly_at_the_length_cap_is_still_compared()
    {
        var run = new string('a', IntralineChanges.MaxComparedLineLength - 2);

        await Assert.That(Marked("k=b", $"k={run}")).IsEqualTo(("k=⟦b⟧", $"k=⟦{run}⟧"));
    }

    [Test]
    public async Task A_new_line_one_character_past_the_length_cap_is_not_compared()
    {
        var run = new string('a', IntralineChanges.MaxComparedLineLength - 1);

        await Assert
            .That(IntralineChanges.Between("k=b", $"k={run}"))
            .IsSameReferenceAs(IntralineChanges.None);
    }

    [Test]
    public async Task No_changes_are_nothing_on_either_side()
    {
        await Assert.That(IntralineChanges.None.Old).IsEmpty();
        await Assert.That(IntralineChanges.None.New).IsEmpty();
    }

    [Test]
    public async Task A_span_ends_where_its_length_runs_out()
    {
        await Assert.That(new ChangedSpan(3, 4).End).IsEqualTo(7);
    }

    private static (string Old, string New) Marked(string oldText, string newText)
    {
        var changes = IntralineChanges.Between(oldText, newText);
        return (Mark(oldText, changes.Old), Mark(newText, changes.New));
    }

    private static string Mark(string text, IReadOnlyList<ChangedSpan> spans)
    {
        var marked = text;
        foreach (var span in spans.Reverse())
        {
            marked = marked.Insert(span.End, "⟧").Insert(span.Start, "⟦");
        }

        return marked;
    }
}
