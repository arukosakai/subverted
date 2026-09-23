using Subverted.Frontend.Diff;

namespace Subverted.Frontend.Tests.Diff;

/// <summary>
/// The first four headers are copied from the captures; the rest are hand-written, because SVN
/// never prints a malformed header and each one pins a different way of refusing one.
/// </summary>
public sealed class HunkRangeTests
{
    [Test]
    [Arguments("@@ -1,6 +1,6 @@", 1, 6, 1, 6)]
    [Arguments("@@ -0,0 +1,2 @@", 0, 0, 1, 2)]
    [Arguments("@@ -1 +0,0 @@", 1, 1, 0, 0)]
    [Arguments("@@ -24,7 +24,7 @@", 24, 7, 24, 7)]
    [Arguments("@@ -3 +4,2 @@ trailing text", 3, 1, 4, 2)]
    public async Task A_content_header_gives_both_ranges_with_an_omitted_count_meaning_one(
        string line,
        int oldStart,
        int oldCount,
        int newStart,
        int newCount
    )
    {
        await Assert
            .That(HunkRange.Parse(line, HunkRange.ContentFence))
            .IsEqualTo(new HunkRange(oldStart, oldCount, newStart, newCount));
    }

    [Test]
    public async Task A_property_header_is_read_under_the_property_fence()
    {
        await Assert
            .That(HunkRange.Parse("## -1,3 +1,3 ##", HunkRange.PropertyFence))
            .IsEqualTo(new HunkRange(1, 3, 1, 3));
    }

    [Test]
    [Arguments("## -1 +1 ##", HunkRange.ContentFence)]
    [Arguments("@@ -1 +1 @@", HunkRange.PropertyFence)]
    public async Task A_header_is_not_read_under_the_other_fence(string line, string fence)
    {
        await Assert.That(HunkRange.Parse(line, fence)).IsNull();
    }

    [Test]
    [Arguments("Index: a.txt")]
    [Arguments("@@ -1 +1")]
    [Arguments("@@ -1 @@")]
    [Arguments("@@ -1 +1 +1 @@")]
    [Arguments("@@ -a +1 @@")]
    [Arguments("@@ -1,b +1 @@")]
    [Arguments("@@ -1 +x @@")]
    [Arguments("@@ -1 +1,y @@")]
    [Arguments("@@ --1 +1 @@")]
    [Arguments("@@ -1 ++1 @@")]
    [Arguments("@@ -99999999999 +1 @@")]
    public async Task A_line_that_is_not_a_well_formed_header_is_no_range(string line)
    {
        await Assert.That(HunkRange.Parse(line, HunkRange.ContentFence)).IsNull();
    }
}
