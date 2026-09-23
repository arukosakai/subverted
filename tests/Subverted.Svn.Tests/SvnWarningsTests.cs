namespace Subverted.Svn.Tests;

/// <summary>
/// The warning lines are the only thing separating a lock that was granted from one that was not —
/// <c>svn lock</c> and <c>svn unlock</c> exit zero either way — and the only thing naming
/// <em>which</em> node <c>svn resolve</c> would not resolve. Every blob below is what SVN 1.8.15
/// actually wrote to stderr under <c>LC_ALL=C</c>, copied verbatim.
/// </summary>
public sealed class SvnWarningsTests
{
    private const string AlreadyLocked =
        "svn: warning: W160035: Path '/art/hero.png' is already locked by user 'ada' "
        + "in filesystem 'fd30000e-d860-f740-9f86-e40d00f5e82a'\n";

    private const string OutOfDate =
        "svn: warning: W160042: Lock failed: newer version of '/art/hero.png' exists\n";

    private const string NoLockThere =
        "svn: warning: W160040: No lock on path '/art/hero.png' "
        + "in filesystem 'fd30000e-d860-f740-9f86-e40d00f5e82a'\n";

    [Test]
    public async Task Nothing_on_stderr_is_nothing_refused()
    {
        await Assert.That(SvnWarnings.From(string.Empty)).IsEmpty();
    }

    [Test]
    public async Task A_lock_somebody_else_holds_is_a_refusal_and_keeps_svns_wording()
    {
        var refusals = SvnWarnings.From(AlreadyLocked);

        await Assert.That(refusals.Count).IsEqualTo(1);
        await Assert.That(refusals[0]).IsEqualTo(AlreadyLocked.TrimEnd('\n'));
    }

    /// <summary>
    /// The user holding the lock is in SVN's own text and nowhere else. Rewording the line would
    /// lose the one fact an artist needs to know who to go and ask.
    /// </summary>
    [Test]
    public async Task The_user_holding_a_lock_survives_into_the_refusal()
    {
        await Assert.That(SvnWarnings.From(AlreadyLocked)[0]).Contains("user 'ada'");
    }

    private const string TreeConflictWorkingOnly =
        "svn: warning: W155027: Tree conflict can only be resolved to 'working' state; "
        + "'C:\\wc\\sub\\nested.txt' not resolved\n";

    private const string NodeNotFound =
        "svn: warning: W155010: The node 'C:\\wc\\nosuch.txt' was not found.\n";

    private const string NotAWorkingCopy =
        "svn: warning: W155007: 'C:\\repo' is not a working copy\n";

    [Test]
    [Arguments(AlreadyLocked)]
    [Arguments(OutOfDate)]
    [Arguments(NoLockThere)]
    [Arguments(TreeConflictWorkingOnly)]
    [Arguments(NodeNotFound)]
    [Arguments(NotAWorkingCopy)]
    public async Task Every_warning_svn_prints_for_these_commands_counts_as_a_refusal(
        string standardError
    )
    {
        await Assert.That(SvnWarnings.From(standardError).Count).IsEqualTo(1);
    }

    /// <summary>
    /// Resolve's refusals arrive alongside a non-zero exit, unlike lock's. The parser must not care:
    /// the exit code says something was refused and only these lines say which node.
    /// </summary>
    [Test]
    public async Task A_resolve_refusal_keeps_the_node_it_names()
    {
        var refusals = SvnWarnings.From(TreeConflictWorkingOnly);

        await Assert.That(refusals.Count).IsEqualTo(1);
        await Assert.That(refusals[0]).Contains("nested.txt");
        await Assert.That(refusals[0]).Contains("'working'");
    }

    [Test]
    public async Task Several_refused_paths_are_reported_one_by_one_and_in_order()
    {
        var refusals = SvnWarnings.From(AlreadyLocked + OutOfDate);

        await Assert.That(refusals.Count).IsEqualTo(2);
        await Assert.That(refusals[0]).Contains("W160035");
        await Assert.That(refusals[1]).Contains("W160042");
    }

    /// <summary>
    /// An error is not a refusal. It comes with a non-zero exit code, which the caller has already
    /// turned into a failure — counting it here as well would report the same thing twice.
    /// </summary>
    [Test]
    public async Task An_error_svn_exited_non_zero_on_is_not_counted_as_a_refusal()
    {
        var standardError =
            "svn: E195013: 'C:\\wc\\art\\hero.png' is not locked in this working copy\n";

        await Assert.That(SvnWarnings.From(standardError)).IsEmpty();
    }

    /// <summary>
    /// The prefix has to start the line. A path that merely <em>contains</em> the words — SVN
    /// quotes the path it was given, so a file can — would otherwise invent a refusal that never
    /// happened, and the caller would report a lock it does hold as refused.
    /// </summary>
    [Test]
    public async Task A_line_that_only_mentions_a_warning_is_not_one()
    {
        var standardOutputStyleLine = "'svn: warning: notes.txt' locked by user 'ada'.\n";

        await Assert.That(SvnWarnings.From(standardOutputStyleLine)).IsEmpty();
    }

    [Test]
    public async Task Windows_line_endings_leave_no_carriage_return_on_the_refusal()
    {
        var refusals = SvnWarnings.From(AlreadyLocked.Replace("\n", "\r\n"));

        await Assert.That(refusals.Count).IsEqualTo(1);
        await Assert.That(refusals[0]).DoesNotContain("\r");
    }

    [Test]
    public async Task A_trailing_newline_does_not_add_an_empty_refusal()
    {
        await Assert.That(SvnWarnings.From(AlreadyLocked + "\n").Count).IsEqualTo(1);
    }
}
