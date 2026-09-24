using System.Text;
using TUnit.Assertions.Enums;

namespace Subverted.Svn.Tests.ContextDiff;

public sealed class TextLinesTests
{
    [Test]
    public async Task An_empty_text_has_no_lines()
    {
        await Assert.That(TextLines.Split([])).IsEmpty();
    }

    [Test]
    [Arguments("a\n", 2)]
    [Arguments("a\r", 2)]
    [Arguments("a\r\n", 3)]
    public async Task Each_ending_ends_a_line_and_belongs_to_it(string text, int length)
    {
        var lines = TextLines.Split(Encoding.ASCII.GetBytes(text));

        await Assert.That(lines).IsEquivalentTo([new TextLine(0, length, HasEnding: true)]);
    }

    [Test]
    public async Task A_lone_cr_ends_a_line_and_a_cr_before_a_cr_is_not_a_crlf()
    {
        var lines = TextLines.Split("a\r\rb\n"u8.ToArray());

        await Assert
            .That(lines)
            .IsEquivalentTo(
                [new TextLine(0, 2, true), new TextLine(2, 1, true), new TextLine(3, 2, true)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_last_line_without_an_ending_is_a_line_that_says_so()
    {
        var lines = TextLines.Split("a\nbc"u8.ToArray());

        await Assert
            .That(lines)
            .IsEquivalentTo(
                [new TextLine(0, 2, true), new TextLine(2, 2, false)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_final_ending_does_not_start_an_empty_line()
    {
        await Assert.That(TextLines.Split("a\n\n"u8.ToArray())).Count().IsEqualTo(2);
    }
}
