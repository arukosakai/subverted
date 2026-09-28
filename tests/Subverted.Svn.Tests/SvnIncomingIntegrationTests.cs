using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// <c>svn status -u</c> against a real repository, with a teammate's checkout committing. The case
/// that decided the query is the mixed copy: counting revisions past BASE counts work a folder
/// already has, and asking the server per node does not.
/// </summary>
public sealed class SvnIncomingIntegrationTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly SvnCommand Svn = new("svn");

    [Test]
    public async Task A_current_copy_has_nothing_incoming()
    {
        using var copy = OneCommit();

        var incoming = await new SvnIncomingCommand(Svn).ReadAsync(copy.Root, copy.Root, None);

        await Assert.That(incoming.AgainstRevision).IsEqualTo(1L);
        await Assert.That(incoming.Changes).IsEmpty();
    }

    [Test]
    public async Task A_teammates_edit_is_incoming_and_this_copys_own_edit_is_not()
    {
        using var copy = OneCommit();
        using var teammate = copy.AnotherCheckout();
        teammate.Write("src/a.txt", "theirs\n");
        teammate.Svn("commit", "--quiet", "-m", "their work");
        copy.Write("art/hero.txt", "mine\n");

        var incoming = await new SvnIncomingCommand(Svn).ReadAsync(copy.Root, copy.Root, None);

        await Assert.That(incoming.AgainstRevision).IsEqualTo(2L);
        await Assert
            .That(incoming.Changes)
            .IsEquivalentTo([new IncomingChange("src/a.txt", PathChange.Modified, false)]);
    }

    /// <summary>
    /// <c>art</c> is updated to r3 and the root left at r1, so <c>svnversion</c> reads 1:3. Only
    /// <c>src/a.txt</c>'s r2 edit is still to come; <c>art/hero.txt</c>'s r3 is already here.
    /// </summary>
    [Test]
    public async Task A_folder_already_updated_past_the_root_brings_nothing_it_already_has()
    {
        using var copy = OneCommit();
        using var teammate = copy.AnotherCheckout();
        teammate.Write("src/a.txt", "theirs\n");
        teammate.Svn("commit", "--quiet", "-m", "r2");
        teammate.Write("art/hero.txt", "theirs\n");
        teammate.Svn("commit", "--quiet", "-m", "r3");
        copy.Svn("update", "--quiet", "art");

        var incoming = await new SvnIncomingCommand(Svn).ReadAsync(copy.Root, copy.Root, None);

        await Assert
            .That(incoming.Changes)
            .IsEquivalentTo([new IncomingChange("src/a.txt", PathChange.Modified, false)]);
    }

    [Test]
    public async Task A_folder_asks_only_about_what_is_beneath_it_with_paths_from_the_root()
    {
        using var copy = OneCommit();
        using var teammate = copy.AnotherCheckout();
        teammate.Write("src/a.txt", "theirs\n");
        teammate.Write("art/hero.txt", "theirs\n");
        teammate.Svn("commit", "--quiet", "-m", "both");

        var incoming = await new SvnIncomingCommand(Svn).ReadAsync(
            copy.Root,
            copy.Absolute("art"),
            None
        );

        await Assert
            .That(incoming.Changes)
            .IsEquivalentTo([new IncomingChange("art/hero.txt", PathChange.Modified, false)]);
    }

    [Test]
    public async Task A_new_file_on_the_server_marks_its_folder_as_well()
    {
        using var copy = OneCommit();
        using var teammate = copy.AnotherCheckout();
        teammate.Write("art/villain.txt", "new\n");
        teammate.Svn("add", "--quiet", "art/villain.txt");
        teammate.Svn("commit", "--quiet", "-m", "new art");

        var incoming = await new SvnIncomingCommand(Svn).ReadAsync(copy.Root, copy.Root, None);

        await Assert
            .That(incoming.Changes)
            .IsEquivalentTo([
                new IncomingChange("art/villain.txt", PathChange.Added, false),
                new IncomingChange("art", PathChange.Modified, false),
            ]);
    }

    [Test]
    public async Task A_path_with_an_at_sign_is_asked_about_rather_than_read_as_a_revision()
    {
        using var copy = OneCommit();
        copy.CreateDirectory("icons@2x");
        copy.Write("icons@2x/a.txt", "ours\n");
        copy.Svn("add", "--quiet", "icons@2x@");
        copy.Svn("commit", "--quiet", "-m", "icons");
        copy.Svn("update", "--quiet");
        using var teammate = copy.AnotherCheckout();
        teammate.Write("icons@2x/a.txt", "theirs\n");
        teammate.Svn("commit", "--quiet", "-m", "their icons");

        var incoming = await new SvnIncomingCommand(Svn).ReadAsync(
            copy.Root,
            copy.Absolute("icons@2x"),
            None
        );

        await Assert
            .That(incoming.Changes)
            .IsEquivalentTo([new IncomingChange("icons@2x/a.txt", PathChange.Modified, false)]);
    }

    [Test]
    public async Task A_server_that_cannot_be_reached_fails_rather_than_reading_as_nothing_incoming()
    {
        using var copy = OneCommit();
        Directory.Move(copy.RepositoryPath, copy.RepositoryPath + "-gone");
        try
        {
            await Assert
                .That(async () =>
                    await new SvnIncomingCommand(Svn).ReadAsync(copy.Root, copy.Root, None)
                )
                .Throws<SvnCommandException>();
        }
        finally
        {
            Directory.Move(copy.RepositoryPath + "-gone", copy.RepositoryPath);
        }
    }

    private static SvnWorkingCopy OneCommit()
    {
        var copy = SvnWorkingCopy.Create();
        try
        {
            copy.Write("src/a.txt", "ours\n");
            copy.Write("art/hero.txt", "ours\n");
            copy.Svn("add", "--quiet", "src", "art");
            copy.Svn("commit", "--quiet", "-m", "first");
            copy.Svn("update", "--quiet");

            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }
}
