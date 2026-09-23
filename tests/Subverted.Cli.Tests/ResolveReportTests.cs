using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli.Tests;

/// <summary>
/// <c>svn resolve</c> says nothing and exits zero for a path it had no conflict to settle, so every
/// rule here is about making "resolved it" and "found nothing" read differently.
/// </summary>
public sealed class ResolveReportTests
{
    [Test]
    public async Task Every_resolved_node_is_named()
    {
        var lines = ResolveReport.Lines(
            new ResolveResponse(["src/a.txt", "art/hero.png"], []),
            ConflictResolution.Mine
        );

        await Assert.That(lines).Contains("resolved  src/a.txt");
        await Assert.That(lines).Contains("resolved  art/hero.png");
    }

    /// <summary>
    /// The line that stops a silent no-op reading as success. SVN prints nothing at all in this
    /// case, so without it the whole output is empty and indistinguishable from work having
    /// happened.
    /// </summary>
    [Test]
    public async Task Nothing_conflicted_says_so_rather_than_printing_nothing()
    {
        var lines = ResolveReport.Lines(new ResolveResponse([], []), ConflictResolution.Mine);

        await Assert.That(lines).IsEquivalentTo(["nothing was conflicted"]);
    }

    [Test]
    public async Task The_count_and_the_version_kept_are_both_stated()
    {
        var lines = ResolveReport.Lines(
            new ResolveResponse(["src/a.txt", "art/hero.png"], []),
            ConflictResolution.Theirs
        );

        await Assert.That(lines).Contains("2 node(s) resolved, keeping the incoming version");
    }

    [Test]
    [Arguments(ConflictResolution.Working, "the files as they are on disk")]
    [Arguments(ConflictResolution.Mine, "this working copy's version")]
    [Arguments(ConflictResolution.Theirs, "the incoming version")]
    [Arguments(ConflictResolution.Base, "the revision both sides started from")]
    public async Task Each_resolution_says_in_words_which_version_it_kept(
        ConflictResolution kept,
        string expected
    )
    {
        var lines = ResolveReport.Lines(new ResolveResponse(["src/a.txt"], []), kept);

        await Assert.That(lines).Contains($"1 node(s) resolved, keeping {expected}");
    }

    /// <summary>
    /// Keeping the working version is the one resolution SVN does not read the file to check, so
    /// it will mark a node finished with the merge markers still in it. This warning is the only
    /// thing standing between that and a commit full of <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>.
    /// </summary>
    [Test]
    public async Task Keeping_the_working_version_warns_that_nothing_looked_inside_the_files()
    {
        var lines = ResolveReport.Lines(
            new ResolveResponse(["src/a.txt"], []),
            ConflictResolution.Working
        );

        await Assert.That(lines.Any(line => line.Contains("<<<<<<<"))).IsTrue();
    }

    /// <summary>
    /// The other side of it: every other resolution rewrote the file from one whole side, so there
    /// are no markers to warn about and saying so would train people to ignore the line.
    /// </summary>
    [Test]
    [Arguments(ConflictResolution.Mine)]
    [Arguments(ConflictResolution.Theirs)]
    [Arguments(ConflictResolution.Base)]
    public async Task No_other_resolution_warns_about_markers(ConflictResolution kept)
    {
        var lines = ResolveReport.Lines(new ResolveResponse(["src/a.txt"], []), kept);

        await Assert.That(lines.Any(line => line.Contains("<<<<<<<"))).IsFalse();
    }

    /// <summary>
    /// Nothing was resolved, so there is nothing to check for markers. Printing the warning here
    /// would send somebody looking through files that were never touched.
    /// </summary>
    [Test]
    public async Task Resolving_nothing_does_not_warn_about_markers()
    {
        var lines = ResolveReport.Lines(new ResolveResponse([], []), ConflictResolution.Working);

        await Assert.That(lines.Any(line => line.Contains("<<<<<<<"))).IsFalse();
    }

    [Test]
    public async Task Svns_own_wording_for_a_refusal_is_passed_through()
    {
        var refusal =
            "svn: warning: W155027: Tree conflict can only be resolved to 'working' state; "
            + "'C:\\wc\\src\\a.txt' not resolved";

        var lines = ResolveReport.Lines(
            new ResolveResponse([], [refusal]),
            ConflictResolution.Theirs
        );

        await Assert.That(lines).Contains(refusal);
    }

    /// <summary>
    /// A warning scrolls past exactly as easily as an exit code hides, and a node that was refused
    /// is still conflicted. The count is what a person actually sees.
    /// </summary>
    [Test]
    public async Task A_refusal_is_counted_at_the_end()
    {
        var lines = ResolveReport.Lines(
            new ResolveResponse([], ["svn: warning: W155027: nope"]),
            ConflictResolution.Theirs
        );

        await Assert
            .That(lines)
            .Contains("1 node(s) were NOT resolved — see the warning(s) above.");
    }

    [Test]
    public async Task No_refusals_means_no_refusal_line()
    {
        var lines = ResolveReport.Lines(
            new ResolveResponse(["src/a.txt"], []),
            ConflictResolution.Mine
        );

        await Assert.That(lines.Any(line => line.Contains("NOT resolved"))).IsFalse();
    }

    /// <summary>
    /// Resolve is per-path, so some settled and some refused is the normal mixed outcome — both
    /// counts have to survive, or the report is right about half of what happened.
    /// </summary>
    [Test]
    public async Task Some_resolved_and_some_refused_reports_both()
    {
        var lines = ResolveReport.Lines(
            new ResolveResponse(["src/a.txt"], ["svn: warning: W155010: nope"]),
            ConflictResolution.Mine
        );

        await Assert.That(lines).Contains("resolved  src/a.txt");
        await Assert
            .That(lines)
            .Contains("1 node(s) resolved, keeping this working copy's version");
        await Assert
            .That(lines)
            .Contains("1 node(s) were NOT resolved — see the warning(s) above.");
    }

    /// <summary>
    /// A refusal with nothing resolved is not "nothing was conflicted" — there was a conflict and
    /// it is still there. Saying the former would report a failure as an all-clear.
    /// </summary>
    [Test]
    public async Task A_refusal_alone_is_not_reported_as_nothing_to_do()
    {
        var lines = ResolveReport.Lines(
            new ResolveResponse([], ["svn: warning: W155027: nope"]),
            ConflictResolution.Theirs
        );

        await Assert.That(lines).DoesNotContain("nothing was conflicted");
    }
}
