namespace Subverted.App.Tests;

/// <summary>
/// <c>svn diff</c> output as 1.8.15 printed it under <c>LC_ALL=C</c>, which is how the daemon runs
/// it — taken off the <c>subverted-props</c> and <c>subverted-copy-e2e3</c> fixtures.
/// </summary>
internal static class SvnDiffs
{
    /// <summary><c>M       sub\child.txt</c> in <c>subverted-props</c>.</summary>
    public const string EditedText = """
        Index: sub/child.txt
        ===================================================================
        --- sub/child.txt	(revision 1)
        +++ sub/child.txt	(working copy)
        @@ -1 +1,2 @@
         child
        +edited too

        """;

    /// <summary><c>MM      both.txt</c> in <c>subverted-props</c>: content and a property.</summary>
    public const string EditedTextAndProperty = """
        Index: both.txt
        ===================================================================
        --- both.txt	(revision 1)
        +++ both.txt	(working copy)
        @@ -1 +1 @@
        -content of both
        +changed

        Property changes on: both.txt
        ___________________________________________________________________
        Added: svn:mime-type
        ## -0,0 +1 ##
        +text/plain
        \ No newline at end of property

        """;

    /// <summary><c>M       gfx\hero.png</c> in <c>subverted-copy-e2e3</c>.</summary>
    public const string EditedBinary = """
        Index: gfx/hero.png
        ===================================================================
        Cannot display: file marked as a binary type.
        svn:mime-type = application/octet-stream

        """;
}
