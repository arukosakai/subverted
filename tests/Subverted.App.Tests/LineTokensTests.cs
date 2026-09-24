using Subverted.App.Presentation;
using TUnit.Assertions.Enums;

namespace Subverted.App.Tests;

public sealed class LineTokensTests
{
    [Test]
    public async Task An_empty_line_has_no_tokens()
    {
        await Assert.That(LineTokens.Of("")).IsEmpty();
    }

    [Test]
    public async Task Letters_digits_and_underscores_run_together_into_one_word()
    {
        await Assert
            .That(LineTokens.Of("foo_bar1 baz"))
            .IsEquivalentTo(
                [new LineToken(0, 8, false), new LineToken(8, 1, true), new LineToken(9, 3, false)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_hyphen_is_not_part_of_a_word()
    {
        await Assert
            .That(LineTokens.Of("a-b"))
            .IsEquivalentTo(
                [new LineToken(0, 1, false), new LineToken(1, 1, false), new LineToken(2, 1, false)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task Each_symbol_is_a_token_of_its_own_even_beside_the_same_symbol()
    {
        await Assert
            .That(LineTokens.Of("+="))
            .IsEquivalentTo(
                [new LineToken(0, 1, false), new LineToken(1, 1, false)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task Spaces_and_tabs_run_together_into_one_whitespace_token()
    {
        await Assert
            .That(LineTokens.Of(" \t x"))
            .IsEquivalentTo(
                [new LineToken(0, 3, true), new LineToken(3, 1, false)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_surrogate_pair_is_one_token_of_two_code_units()
    {
        await Assert
            .That(LineTokens.Of("a😀😀b"))
            .IsEquivalentTo(
                [
                    new LineToken(0, 1, false),
                    new LineToken(1, 2, false),
                    new LineToken(3, 2, false),
                    new LineToken(5, 1, false),
                ],
                CollectionOrdering.Matching
            );
    }

    /// <summary>𝑥 is a letter outside the BMP; judged by its first code unit alone it would be a symbol.</summary>
    [Test]
    public async Task A_letter_outside_the_basic_plane_is_part_of_a_word()
    {
        await Assert
            .That(LineTokens.Of("𝑥1"))
            .IsEquivalentTo([new LineToken(0, 3, false)], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_combining_accent_stays_with_the_letter_it_sits_on()
    {
        await Assert
            .That(LineTokens.Of("e\u0301.x"))
            .IsEquivalentTo(
                [new LineToken(0, 2, false), new LineToken(2, 1, false), new LineToken(3, 1, false)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_lone_surrogate_is_a_symbol_between_words()
    {
        await Assert
            .That(LineTokens.Of("a\uD83Db"))
            .IsEquivalentTo(
                [new LineToken(0, 1, false), new LineToken(1, 1, false), new LineToken(2, 1, false)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_token_ends_where_its_length_runs_out()
    {
        await Assert.That(new LineToken(2, 5, false).End).IsEqualTo(7);
    }
}
