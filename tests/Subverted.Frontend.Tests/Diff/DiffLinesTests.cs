using Subverted.Frontend.Diff;
using TUnit.Assertions.Enums;

namespace Subverted.Frontend.Tests.Diff;

/// <summary>
/// Hand-written: each case is one line-ending shape, including ones no capture ends in (a lone
/// <c>\r</c> as the very last character, no final line ending at all).
/// </summary>
public sealed class DiffLinesTests
{
    [Test]
    [Arguments("", new string[0])]
    [Arguments("a", new[] { "a" })]
    [Arguments("a\n", new[] { "a" })]
    [Arguments("a\r\n", new[] { "a" })]
    [Arguments("a\r", new[] { "a" })]
    [Arguments("a\rb", new[] { "a", "b" })]
    [Arguments("a\r\nb\rc\nd", new[] { "a", "b", "c", "d" })]
    [Arguments("a\n\n", new[] { "a", "" })]
    [Arguments("a\r\r\n", new[] { "a", "" })]
    [Arguments("a\n\rb", new[] { "a", "", "b" })]
    public async Task Crlf_lone_cr_and_lone_lf_each_end_one_line_and_none_is_kept(
        string text,
        string[] expected
    )
    {
        await Assert
            .That(DiffLines.Split(text))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }
}
