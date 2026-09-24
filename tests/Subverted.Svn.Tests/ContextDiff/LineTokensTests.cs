using TUnit.Assertions.Enums;

namespace Subverted.Svn.Tests.ContextDiff;

public sealed class LineTokensTests
{
    [Test]
    public async Task Equal_lines_on_either_side_share_a_number_and_different_ones_do_not()
    {
        byte[] old = "a\nb\na\n"u8.ToArray();
        byte[] @new = "b\nc\n"u8.ToArray();

        var (oldTokens, newTokens) = LineTokens.Of(
            old,
            TextLines.Split(old),
            @new,
            TextLines.Split(@new)
        );

        await Assert.That(oldTokens).IsEquivalentTo([0, 1, 0], CollectionOrdering.Matching);
        await Assert.That(newTokens).IsEquivalentTo([1, 2], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_line_ending_is_part_of_what_makes_two_lines_equal()
    {
        byte[] old = "a\n"u8.ToArray();
        byte[] @new = "a\r\n"u8.ToArray();

        var (oldTokens, newTokens) = LineTokens.Of(
            old,
            TextLines.Split(old),
            @new,
            TextLines.Split(@new)
        );

        await Assert.That(newTokens[0]).IsNotEqualTo(oldTokens[0]);
    }
}
