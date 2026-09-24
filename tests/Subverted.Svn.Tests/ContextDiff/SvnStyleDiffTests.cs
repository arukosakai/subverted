using System.Text;

namespace Subverted.Svn.Tests.ContextDiff;

public sealed class SvnStyleDiffTests
{
    private static readonly DiffSectionHeader Header = new("a.txt", "revision 4", "working copy");

    private const string Headers =
        "Index: a.txt\n"
        + "===================================================================\n"
        + "--- a.txt\t(revision 4)\n"
        + "+++ a.txt\t(working copy)\n";

    [Test]
    public async Task Equal_texts_write_nothing_at_all()
    {
        var written = Write("a\nb\n", "a\nb\n", 3);

        await Assert.That(written).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Removals_come_before_additions_and_context_lines_keep_their_own_endings()
    {
        var written = Write("a\r\nb\nc\n", "a\r\nB\nc\n", 3);

        await Assert.That(written).IsEqualTo(Headers + "@@ -1,3 +1,3 @@\n a\r\n-b\n+B\n c\n");
    }

    [Test]
    public async Task A_count_of_one_is_left_out_of_the_hunk_header()
    {
        var written = Write("a\n", "b\n", 3);

        await Assert.That(written).IsEqualTo(Headers + "@@ -1 +1 @@\n-a\n+b\n");
    }

    [Test]
    public async Task A_side_with_no_lines_starts_at_the_line_before_it()
    {
        var written = Write("", "a\nb\n", 3);

        await Assert.That(written).IsEqualTo(Headers + "@@ -0,0 +1,2 @@\n+a\n+b\n");
    }

    [Test]
    public async Task A_last_line_without_an_ending_is_given_one_and_marked()
    {
        var written = Write("a\nb", "a\nB", 3);

        await Assert
            .That(written)
            .IsEqualTo(
                Headers
                    + "@@ -1,2 +1,2 @@\n a\n-b\n\\ No newline at end of file\n+B\n\\ No newline at end of file\n"
            );
    }

    [Test]
    public async Task Svns_own_lines_end_in_the_newline_it_is_given()
    {
        var written = Write("a\n", "b\n", 3, newline: "\r\n");

        await Assert
            .That(written)
            .IsEqualTo(
                "Index: a.txt\r\n"
                    + "===================================================================\r\n"
                    + "--- a.txt\t(revision 4)\r\n"
                    + "+++ a.txt\t(working copy)\r\n"
                    + "@@ -1 +1 @@\r\n-a\n+b\n"
            );
    }

    [Test]
    public async Task More_context_shows_more_of_the_unchanged_lines()
    {
        var written = Write(Numbered(12), Numbered(12).Replace("6\n", "six\n"), 4);

        await Assert
            .That(written)
            .IsEqualTo(Headers + "@@ -2,9 +2,9 @@\n 2\n 3\n 4\n 5\n-6\n+six\n 7\n 8\n 9\n 10\n");
    }

    [Test]
    public async Task No_context_limit_shows_every_line_of_the_file()
    {
        var written = Write("1\n2\n3\n4\n5\n", "1\n2\nthree\n4\n5\n", context: null);

        await Assert.That(written).IsEqualTo(Headers + "@@ -1,5 +1,5 @@\n 1\n 2\n-3\n+three\n 4\n 5\n");
    }

    [Test]
    public async Task Two_hunks_are_written_in_order_each_with_its_own_header()
    {
        var written = Write(Numbered(20), Numbered(20).Replace("\n2\n", "\ntwo\n").Replace("19\n", "nineteen\n"), 1);

        await Assert
            .That(written)
            .IsEqualTo(Headers + "@@ -1,3 +1,3 @@\n 1\n-2\n+two\n 3\n@@ -18,3 +18,3 @@\n 18\n-19\n+nineteen\n 20\n");
    }

    [Test]
    public async Task A_change_too_large_for_the_budget_writes_nothing_and_says_so_with_null()
    {
        var written = SvnStyleDiff.Write(
            Header,
            "1\n2\n1\n2\n"u8.ToArray(),
            "2\n1\n2\n1\n"u8.ToArray(),
            3,
            "\n",
            budget: 0
        );

        await Assert.That(written).IsNull();
    }

    private static string Numbered(int count) =>
        string.Concat(Enumerable.Range(1, count).Select(i => $"{i}\n"));

    private static string? Write(string old, string @new, int? context, string newline = "\n") =>
        SvnStyleDiff.Write(Header, Encoding.ASCII.GetBytes(old), Encoding.ASCII.GetBytes(@new), context, newline)
            is { } bytes
            ? Encoding.ASCII.GetString(bytes)
            : null;
}
