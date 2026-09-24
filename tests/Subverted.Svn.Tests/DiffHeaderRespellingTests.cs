namespace Subverted.Svn.Tests;

/// <summary>
/// Puts the true names back into a diff whose headers svn wrote in a code page that could not hold
/// them. The spelling below stands in for Windows' own: anything outside ASCII becomes <c>?</c>.
/// </summary>
public sealed class DiffHeaderRespellingTests
{
    private static string AsciiOnly(string name) =>
        new([.. name.Select(character => character < 128 ? character : '?')]);

    private static readonly string Lossy = Lf(
        """
        Index: names/????.txt
        ===================================================================
        --- names/????.txt	(revision 1)
        +++ names/????.txt	(working copy)
        @@ -1 +1 @@
        -one
        +two

        """
    );

    /// <summary>A checkout with autocrlf would otherwise hand these literals back with CRLF.</summary>
    private static string Lf(string text) => text.ReplaceLineEndings("\n");

    [Test]
    public async Task Every_header_line_gets_the_true_name_back()
    {
        var respelled = DiffHeaderRespelling.Respell(Lossy, ["names/ドラゴン.txt"], AsciiOnly);

        await Assert
            .That(respelled)
            .IsEqualTo(Lossy.Replace("names/????.txt", "names/ドラゴン.txt"));
    }

    /// <summary>A revision diff names files relative to the target, which the summary does not.</summary>
    [Test]
    public async Task A_header_can_name_the_tail_of_a_summarised_path()
    {
        var respelled = DiffHeaderRespelling.Respell(
            "Index: ????.txt\n",
            ["repo/names/ドラゴン.txt"],
            AsciiOnly
        );

        await Assert.That(respelled).IsEqualTo("Index: ドラゴン.txt\n");
    }

    [Test]
    public async Task A_header_svn_spelled_correctly_is_left_as_it_is()
    {
        var diff = "Index: names/plain.txt\n";

        await Assert
            .That(DiffHeaderRespelling.Respell(diff, ["names/plain.txt"], AsciiOnly))
            .IsEqualTo(diff);
    }

    /// <summary>
    /// A build that writes UTF-8 prints the true name, which is not the lossy spelling of anything,
    /// so the answer is already right and must stay so.
    /// </summary>
    [Test]
    public async Task A_true_name_already_in_the_header_is_left_as_it_is()
    {
        var diff = "Index: names/ドラゴン.txt\n";

        await Assert
            .That(DiffHeaderRespelling.Respell(diff, ["names/ドラゴン.txt"], AsciiOnly))
            .IsEqualTo(diff);
    }

    /// <summary>Two names svn would print the same way: guessing would name the wrong file.</summary>
    [Test]
    public async Task A_header_two_names_share_is_left_as_svn_wrote_it()
    {
        var respelled = DiffHeaderRespelling.Respell(
            "Index: ????.txt\n",
            ["ドラゴン.txt", "ブラウン.txt"],
            AsciiOnly
        );

        await Assert.That(respelled).IsEqualTo("Index: ????.txt\n");
    }

    [Test]
    public async Task One_name_reached_through_two_summarised_paths_is_still_one_name()
    {
        var respelled = DiffHeaderRespelling.Respell(
            "Index: ????.txt\n",
            ["a/ドラゴン.txt", "b/ドラゴン.txt"],
            AsciiOnly
        );

        await Assert.That(respelled).IsEqualTo("Index: ドラゴン.txt\n");
    }

    /// <summary>
    /// A content line removing <c>-- ????.txt</c> reads <c>--- ????.txt</c>. Only the pair that
    /// follows an Index separator is a header.
    /// </summary>
    [Test]
    public async Task A_content_line_that_looks_like_a_header_is_not_touched()
    {
        var diff = Lf(
            """
            Index: ????.txt
            ===================================================================
            --- ????.txt	(revision 1)
            +++ ????.txt	(working copy)
            @@ -1 +0,0 @@
            --- ????.txt
            +++ ????.txt

            """
        );

        var respelled = DiffHeaderRespelling.Respell(diff, ["ドラゴン.txt"], AsciiOnly);

        await Assert.That(respelled).EndsWith("@@ -1 +0,0 @@\n--- ????.txt\n+++ ????.txt\n");
        await Assert.That(respelled).StartsWith("Index: ドラゴン.txt\n");
        await Assert.That(respelled).Contains("--- ドラゴン.txt\t(revision 1)\n");
        await Assert.That(respelled).Contains("+++ ドラゴン.txt\t(working copy)\n");
    }

    [Test]
    public async Task A_property_section_gets_the_true_name_back()
    {
        var diff = Lf(
            """
            Property changes on: ????.txt
            ___________________________________________________________________
            Added: custom:x
            ## -0,0 +1 ##
            +y

            """
        );

        var respelled = DiffHeaderRespelling.Respell(diff, ["ドラゴン.txt"], AsciiOnly);

        await Assert.That(respelled).StartsWith("Property changes on: ドラゴン.txt\n");
        await Assert.That(respelled).Contains("Added: custom:x\n");
    }

    [Test]
    public async Task Windows_line_endings_survive()
    {
        var respelled = DiffHeaderRespelling.Respell(
            "Index: ????.txt\r\n",
            ["ドラゴン.txt"],
            AsciiOnly
        );

        await Assert.That(respelled).IsEqualTo("Index: ドラゴン.txt\r\n");
    }

    /// <summary>With no Index line above them, a <c>---</c>/<c>+++</c> pair is content, not a header.</summary>
    [Test]
    [Arguments("--- ????.txt\t(revision 1)\n+++ ????.txt\t(working copy)\n")]
    [Arguments("+++ ????.txt\t(working copy)\n")]
    public async Task A_pair_with_no_index_line_above_it_is_left_alone(string diff)
    {
        await Assert
            .That(DiffHeaderRespelling.Respell(diff, ["ドラゴン.txt"], AsciiOnly))
            .IsEqualTo(diff);
    }

    [Test]
    public async Task A_header_naming_no_summarised_path_is_left_alone()
    {
        await Assert
            .That(DiffHeaderRespelling.Respell("Index: ????.txt\n", ["other.txt"], AsciiOnly))
            .IsEqualTo("Index: ????.txt\n");
    }
}
