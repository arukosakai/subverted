using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// The in-process diff at three lines against <c>svn diff</c> itself, live, on the same files: where
/// it answers, the text must be svn's to the character — which makes the parsed documents equal too
/// — and where svn's answer is not two texts compared, it must decline.
/// </summary>
public sealed class ContextDiffEquivalenceIntegrationTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly SvnCommand Svn = new("svn");

    [After(HookType.Class)]
    public static void RemoveTheCopy() => ContextDiffCopy.Dispose();

    [Test]
    [Arguments("plain.txt")]
    [Arguments("gap5.txt")]
    [Arguments("gap6.txt")]
    [Arguments("crlf-noprop.txt")]
    [Arguments("mixed-noprop.txt")]
    [Arguments("eol-only.txt")]
    [Arguments("lone-cr.txt")]
    [Arguments("native.txt")]
    [Arguments("lf-style.txt")]
    [Arguments("crlf-style.txt")]
    [Arguments("cr-style.txt")]
    [Arguments("native-lf-on-disk.txt")]
    [Arguments("text-mime.txt")]
    [Arguments("html-mime.txt")]
    [Arguments("empty.txt")]
    [Arguments("becomes-empty.txt")]
    [Arguments("no-eol.txt")]
    [Arguments("gains-no-eol.txt")]
    [Arguments("no-eol-context.txt")]
    [Arguments("ambiguous.txt")]
    [Arguments("swap.txt")]
    [Arguments("brace.txt")]
    [Arguments("binary-no-mime.dat")]
    public async Task A_local_edit_at_three_lines_is_svn_diffs_own_text(string name)
    {
        var copy = ContextDiffCopy.Copy;
        var path = copy.Absolute(name);

        var svn = await new SvnDiffCommand(Svn).ReadAsync(copy.Root, path, None);
        var written = await WorkingCopyDiff().ReadAsync(copy.Root, path, DiffContext.Default, None);

        await Assert.That(svn).StartsWith($"Index: {name}");
        await Assert.That(written).IsEqualTo(svn);
    }

    [Test]
    public async Task An_unchanged_file_is_as_empty_as_svn_diff_prints_it()
    {
        var copy = ContextDiffCopy.Copy;
        var path = copy.Absolute("unchanged.txt");

        var svn = await new SvnDiffCommand(Svn).ReadAsync(copy.Root, path, None);
        var written = await WorkingCopyDiff().ReadAsync(copy.Root, path, DiffContext.Default, None);

        await Assert.That(svn).IsEmpty();
        await Assert.That(written).IsEqualTo(string.Empty);
    }

    [Test]
    [Arguments("keywords.txt")]
    [Arguments("binary.dat")]
    [Arguments("props.txt")]
    [Arguments("props-and-text.txt")]
    [Arguments("copy-source.txt")]
    [Arguments("missing.txt")]
    [Arguments("copied.txt")]
    [Arguments("added.txt")]
    [Arguments("")]
    public async Task A_local_change_svn_does_not_diff_as_two_texts_is_left_to_svn(string name)
    {
        var copy = ContextDiffCopy.Copy;

        var written = await WorkingCopyDiff()
            .ReadAsync(copy.Root, copy.Absolute(name), DiffContext.Default, None);

        await Assert.That(written).IsNull();
    }

    [Test]
    [Arguments("plain.txt")]
    [Arguments("gap5.txt")]
    [Arguments("gap6.txt")]
    [Arguments("crlf-noprop.txt")]
    [Arguments("mixed-noprop.txt")]
    [Arguments("eol-only.txt")]
    [Arguments("lone-cr.txt")]
    [Arguments("native.txt")]
    [Arguments("lf-style.txt")]
    [Arguments("crlf-style.txt")]
    [Arguments("cr-style.txt")]
    [Arguments("native-lf-on-disk.txt")]
    [Arguments("text-mime.txt")]
    [Arguments("html-mime.txt")]
    [Arguments("empty.txt")]
    [Arguments("becomes-empty.txt")]
    [Arguments("no-eol.txt")]
    [Arguments("gains-no-eol.txt")]
    [Arguments("no-eol-context.txt")]
    [Arguments("ambiguous.txt")]
    [Arguments("swap.txt")]
    [Arguments("brace.txt")]
    [Arguments("binary-no-mime.dat")]
    public async Task A_committed_edit_at_three_lines_is_svn_diff_cs_own_text(string name)
    {
        var (svn, written) = await RevisionTwo(name);

        await Assert.That(svn).StartsWith($"Index: {name}");
        await Assert.That(written).IsEqualTo(svn);
    }

    [Test]
    [Arguments("keywords.txt")]
    [Arguments("binary.dat")]
    [Arguments("props.txt")]
    [Arguments("props-and-text.txt")]
    [Arguments("added-in-r2.txt")]
    [Arguments("deleted-in-r2.txt")]
    [Arguments("unchanged.txt")]
    public async Task A_committed_change_svn_does_not_diff_as_two_texts_is_left_to_svn(string name)
    {
        var (_, written) = await RevisionTwo(name);

        await Assert.That(written).IsNull();
    }

    [Test]
    public async Task The_first_revision_is_left_to_svn_since_nothing_came_before_it()
    {
        var copy = ContextDiffCopy.Copy;

        var written = await RevisionDiff()
            .ReadAsync(
                copy.Root,
                ContextDiffCopy.RepositoryRoot,
                "/plain.txt",
                1,
                DiffContext.Default,
                None
            );

        await Assert.That(written).IsNull();
    }

    private static WorkingCopyContextDiff WorkingCopyDiff() =>
        new(Svn.Spelling, Environment.NewLine);

    private static RevisionContextDiff RevisionDiff() => new(Svn, Environment.NewLine);

    private static async Task<(string Svn, string? Written)> RevisionTwo(string name)
    {
        var copy = ContextDiffCopy.Copy;
        var repositoryRoot = ContextDiffCopy.RepositoryRoot;

        var svn = await new SvnRevisionDiffCommand(Svn).ReadAsync(
            copy.Root,
            repositoryRoot,
            $"/{name}",
            2,
            None
        );
        var written = await RevisionDiff()
            .ReadAsync(copy.Root, repositoryRoot, $"/{name}", 2, DiffContext.Default, None);
        return (svn, written);
    }
}
