namespace Subverted.Svn.Tests;

public sealed class SvnCommandResultTests
{
    /// <summary>SVN's own wording names the error code, which is what a user can search for.</summary>
    [Test]
    public async Task A_failure_svn_explained_is_reported_in_svns_words()
    {
        var result = new SvnCommandResult(1, string.Empty, "svn: E170013: unable to connect\n");

        await Assert.That(result.Complaint).IsEqualTo("svn: E170013: unable to connect");
    }

    [Test]
    [Arguments("")]
    [Arguments("   \n\t ")]
    public async Task A_failure_svn_said_nothing_about_is_reported_by_its_exit_code(string stderr)
    {
        var result = new SvnCommandResult(2, "some output", stderr);

        await Assert
            .That(result.Complaint)
            .IsEqualTo("svn exited with code 2 and said nothing about why.");
    }
}
