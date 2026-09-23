namespace Subverted.Daemon.Tests;

/// <summary>
/// The containment rule the commands that write are gated on. Pure, and worth pinning on both
/// platforms: a rule that is too loose commits into the wrong checkout, and one that is too tight
/// refuses <c>sv commit</c> at the root.
/// </summary>
public sealed class WorkingCopyTargetsTests
{
    private const StringComparison Sensitive = StringComparison.Ordinal;

    [Test]
    public async Task Paths_inside_the_root_are_all_accepted()
    {
        await Assert
            .That(WorkingCopyTargets.FirstOutside("/wc", ["/wc/art", "/wc/src/a.txt"], Sensitive))
            .IsNull();
    }

    /// <summary>The root itself is in the working copy — <c>sv commit</c> with no path is this.</summary>
    [Test]
    public async Task The_root_itself_is_inside_the_root()
    {
        await Assert.That(WorkingCopyTargets.FirstOutside("/wc", ["/wc"], Sensitive)).IsNull();
    }

    [Test]
    public async Task A_trailing_separator_on_the_root_does_not_change_the_answer()
    {
        await Assert.That(WorkingCopyTargets.FirstOutside("/wc/", ["/wc/art"], Sensitive)).IsNull();
    }

    [Test]
    public async Task The_first_path_outside_the_root_is_the_one_reported()
    {
        await Assert
            .That(
                WorkingCopyTargets.FirstOutside(
                    "/wc",
                    ["/wc/art", "/elsewhere/a", "/nowhere/b"],
                    Sensitive
                )
            )
            .IsEqualTo("/elsewhere/a");
    }

    /// <summary>
    /// The sibling that shares a prefix. Accepting it would commit a different checkout's files
    /// under this one's revision, and a plain <c>StartsWith</c> does exactly that.
    /// </summary>
    [Test]
    public async Task A_sibling_whose_name_starts_with_the_root_is_outside_it()
    {
        await Assert
            .That(WorkingCopyTargets.FirstOutside("/wc", ["/wc2/art"], Sensitive))
            .IsEqualTo("/wc2/art");
    }

    [Test]
    public async Task A_parent_of_the_root_is_outside_it()
    {
        await Assert.That(WorkingCopyTargets.FirstOutside("/wc", ["/"], Sensitive)).IsEqualTo("/");
    }

    [Test]
    [Arguments(StringComparison.OrdinalIgnoreCase, null)]
    [Arguments(StringComparison.Ordinal, "/WC/art")]
    public async Task Case_is_the_platforms_business_and_not_this_functions(
        StringComparison comparison,
        string? expected
    )
    {
        await Assert
            .That(WorkingCopyTargets.FirstOutside("/wc", ["/WC/art"], comparison))
            .IsEqualTo(expected);
    }

    /// <summary>
    /// Naming nothing is a different complaint, worded where the command the user typed is known.
    /// This function saying "all fine" is what lets that happen.
    /// </summary>
    [Test]
    public async Task No_paths_at_all_is_not_this_functions_complaint()
    {
        await Assert.That(WorkingCopyTargets.FirstOutside("/wc", [], Sensitive)).IsNull();
    }
}
