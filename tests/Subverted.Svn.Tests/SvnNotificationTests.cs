namespace Subverted.Svn.Tests;

/// <summary>
/// The one rewriting Subverted does to SVN's own text, and the reason it is narrow: everything else
/// SVN says is passed through, because its wording is better than anything written over it.
/// </summary>
public sealed class SvnNotificationTests
{
    /// <summary>
    /// <c>sv st</c> reports slash-separated paths and SVN prints native ones. Two spellings of one
    /// file in one tool is the thing this exists to prevent.
    /// </summary>
    [Test]
    public async Task Where_a_backslash_is_only_a_separator_it_becomes_a_forward_slash()
    {
        await Assert
            .That(SvnNotification.Spelled(@"A         src\b.txt", backslashIsOnlyASeparator: true))
            .IsEqualTo("A         src/b.txt");
    }

    /// <summary>
    /// On POSIX a backslash is a legal character in a filename. Rewriting it there would name a
    /// different file — and the one it named might exist.
    /// </summary>
    [Test]
    public async Task Where_a_backslash_can_be_part_of_a_name_it_is_left_alone()
    {
        await Assert
            .That(
                SvnNotification.Spelled(
                    @"A         art/od\d name.png",
                    backslashIsOnlyASeparator: false
                )
            )
            .IsEqualTo(@"A         art/od\d name.png");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Text_with_no_backslash_in_it_is_the_same_either_way(bool reserved)
    {
        await Assert
            .That(SvnNotification.Spelled("Committed revision 12.\n", reserved))
            .IsEqualTo("Committed revision 12.\n");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Saying_nothing_stays_nothing(bool reserved)
    {
        await Assert.That(SvnNotification.Spelled(string.Empty, reserved)).IsEmpty();
    }

    /// <summary>Every line is rewritten, not only the first — a commit names many paths.</summary>
    [Test]
    public async Task Every_line_is_respelled_and_not_only_the_first()
    {
        await Assert
            .That(
                SvnNotification.Spelled(
                    "Sending        src\\a.txt\nAdding         art\\hero.png\n",
                    backslashIsOnlyASeparator: true
                )
            )
            .IsEqualTo("Sending        src/a.txt\nAdding         art/hero.png\n");
    }
}
