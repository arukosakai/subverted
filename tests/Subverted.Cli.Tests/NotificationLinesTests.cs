namespace Subverted.Cli.Tests;

public sealed class NotificationLinesTests
{
    [Test]
    public async Task Every_path_svn_reported_becomes_a_line()
    {
        await Assert
            .That(NotificationLines.Of("A         art/hero.png\nA         art/hero.psd\n"))
            .IsEquivalentTo(["A         art/hero.png", "A         art/hero.psd"]);
    }

    [Test]
    public async Task Svn_saying_nothing_is_no_lines_rather_than_one_empty_one()
    {
        await Assert.That(NotificationLines.Of(string.Empty)).IsEmpty();
        await Assert.That(NotificationLines.Of("\n")).IsEmpty();
    }

    /// <summary>
    /// The client is a child process on Windows too, so its lines arrive with carriage returns.
    /// Leaving them attached puts a stray character at the end of every path the user reads.
    /// </summary>
    [Test]
    public async Task Carriage_returns_do_not_survive_into_the_output()
    {
        await Assert
            .That(NotificationLines.Of("A         a.txt\r\nA         b.txt\r\n"))
            .IsEquivalentTo(["A         a.txt", "A         b.txt"]);
    }

    /// <summary>A blank line between notifications is SVN's, and dropping it would reflow its output.</summary>
    [Test]
    public async Task A_blank_line_in_the_middle_is_kept()
    {
        await Assert
            .That(NotificationLines.Of("A         a.txt\n\nA         b.txt\n").Count)
            .IsEqualTo(3);
    }

    [Test]
    public async Task The_stand_in_is_used_only_when_svn_said_nothing()
    {
        await Assert
            .That(NotificationLines.OrElse(string.Empty, "nothing to add"))
            .IsEquivalentTo(["nothing to add"]);
        await Assert
            .That(NotificationLines.OrElse("A         a.txt\n", "nothing to add"))
            .IsEquivalentTo(["A         a.txt"]);
    }
}
