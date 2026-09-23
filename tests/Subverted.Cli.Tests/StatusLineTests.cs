using Subverted.Core;

namespace Subverted.Cli.Tests;

/// <summary>
/// The letters and the column positions are what makes <c>sv st</c> readable by anyone who already
/// reads <c>svn status</c>, so they are pinned character by character.
/// </summary>
public sealed class StatusLineTests
{
    [Test]
    [Arguments(NodeStatus.Unmodified, ' ')]
    [Arguments(NodeStatus.Modified, 'M')]
    [Arguments(NodeStatus.Added, 'A')]
    [Arguments(NodeStatus.Deleted, 'D')]
    [Arguments(NodeStatus.Replaced, 'R')]
    [Arguments(NodeStatus.Missing, '!')]
    [Arguments(NodeStatus.Incomplete, '!')]
    [Arguments(NodeStatus.Unversioned, '?')]
    [Arguments(NodeStatus.Ignored, 'I')]
    [Arguments(NodeStatus.Conflicted, 'C')]
    [Arguments(NodeStatus.Obstructed, '~')]
    [Arguments(NodeStatus.External, 'X')]
    public async Task Each_status_gets_the_letter_svn_would_print(NodeStatus status, char expected)
    {
        await Assert.That(StatusLine.Letter(status)).IsEqualTo(expected);
    }

    /// <summary>
    /// The one letter SVN has no equivalent for. Reporting it as clean would hide work; reporting
    /// it as modified would claim something we did not establish.
    /// </summary>
    /// <remarks>
    /// The character is asserted literally, not against <see cref="StatusLine.Undecided"/> — that
    /// comparison holds whatever the constant says and so could not catch it moving off <c>~</c>
    /// when obstructed nodes took that letter back.
    /// </remarks>
    [Test]
    public async Task The_undecided_state_gets_a_letter_of_its_own()
    {
        var undecided = StatusLine.Letter(NodeStatus.NeedsPristineCompare);

        await Assert.That(undecided).IsEqualTo('*');
        await Assert.That(undecided).IsEqualTo(StatusLine.Undecided);
        await Assert.That(undecided).IsNotEqualTo(StatusLine.Letter(NodeStatus.Modified));
        await Assert.That(undecided).IsNotEqualTo(StatusLine.Letter(NodeStatus.Unmodified));
    }

    /// <summary>
    /// The collision this has to keep avoiding: SVN spends <c>~</c> on obstructed, so the invented
    /// state cannot also have it. Two statuses sharing a letter is a listing nobody can read.
    /// </summary>
    [Test]
    public async Task The_undecided_letter_is_not_the_one_svn_spends_on_obstructed()
    {
        await Assert
            .That(StatusLine.Letter(NodeStatus.NeedsPristineCompare))
            .IsNotEqualTo(StatusLine.Letter(NodeStatus.Obstructed));
    }

    /// <summary>
    /// A daemon one version ahead can send a status this build has no letter for. Printing
    /// "I cannot decide this" is right for that too, and is better than throwing on a listing the
    /// user asked for.
    /// </summary>
    [Test]
    public async Task A_status_this_build_has_never_heard_of_prints_as_undecided()
    {
        await Assert.That(StatusLine.Letter((NodeStatus)9999)).IsEqualTo(StatusLine.Undecided);
    }

    [Test]
    public async Task A_modified_file_is_a_letter_then_seven_spaces_then_its_path()
    {
        await Assert
            .That(StatusLine.Compact(Entry() with { Status = NodeStatus.Modified }))
            .IsEqualTo("M       art/hero.png");
    }

    [Test]
    public async Task Property_changes_go_in_the_second_column_the_way_svn_prints_them()
    {
        await Assert
            .That(
                StatusLine.Compact(
                    Entry() with
                    {
                        Status = NodeStatus.Modified,
                        PropertyStatus = PropertyStatus.Modified,
                    }
                )
            )
            .IsEqualTo("MM      art/hero.png");
    }

    /// <summary>The D9 case that a one-column status could not say: clean text, dirty properties.</summary>
    [Test]
    public async Task A_property_only_change_leaves_the_first_column_blank()
    {
        await Assert
            .That(StatusLine.Compact(Entry() with { PropertyStatus = PropertyStatus.Modified }))
            .IsEqualTo(" M      art/hero.png");
    }

    [Test]
    public async Task A_held_lock_token_goes_in_the_sixth_column()
    {
        await Assert
            .That(StatusLine.Compact(Entry() with { HasLockToken = true }))
            .IsEqualTo("     K  art/hero.png");
    }

    /// <summary>
    /// The working-copy write lock — a client part-way through changing this directory, or one that
    /// died while it was. A different thing from the sixth column's <c>K</c>, and the state in which
    /// every write to this working copy is about to fail.
    /// </summary>
    [Test]
    public async Task A_working_copy_write_lock_goes_in_the_third_column()
    {
        await Assert
            .That(StatusLine.Compact(Entry() with { IsWriteLocked = true }))
            .IsEqualTo("  L     art/hero.png");
    }

    /// <summary>
    /// The two locks are unrelated and a node can carry both, so neither column may be written from
    /// the other's flag.
    /// </summary>
    [Test]
    public async Task A_write_lock_and_a_held_token_occupy_their_own_columns()
    {
        await Assert
            .That(StatusLine.Compact(Entry() with { IsWriteLocked = true, HasLockToken = true }))
            .IsEqualTo("  L  K  art/hero.png");
    }

    /// <summary>
    /// What <c>sv mv</c> leaves behind every time: <c>svn status</c> prints <c>A  +</c>, and this
    /// printed <c>A</c> alone for as long as the column was not modelled.
    /// </summary>
    [Test]
    public async Task A_copy_goes_in_the_fourth_column()
    {
        await Assert
            .That(StatusLine.Compact(Entry() with { Status = NodeStatus.Added, IsCopied = true }))
            .IsEqualTo("A  +    art/hero.png");
    }

    /// <summary>
    /// An untouched file inside a copied directory, as <c>svn status -v</c> prints it: a blank
    /// first column and the <c>+</c>, and the BASE revision column empty because there is none.
    /// </summary>
    [Test]
    public async Task An_untouched_copied_node_shows_only_its_history_in_a_verbose_listing()
    {
        await Assert
            .That(StatusLine.Verbose(Entry() with { IsCopied = true, Revision = null }))
            .IsEqualTo("   +           -  art/hero.png");
    }

    [Test]
    public async Task A_clean_node_is_seven_blanks_and_its_path()
    {
        await Assert.That(StatusLine.Compact(Entry())).IsEqualTo("        art/hero.png");
    }

    /// <summary>The root has an empty relative path and <c>svn status</c> calls it <c>.</c></summary>
    [Test]
    public async Task The_working_copy_root_prints_as_a_dot()
    {
        await Assert
            .That(StatusLine.Compact(Entry() with { RelPath = string.Empty }))
            .IsEqualTo("        .");
    }

    [Test]
    public async Task Verbose_adds_the_revision()
    {
        await Assert
            .That(StatusLine.Verbose(Entry() with { Status = NodeStatus.Modified }))
            .IsEqualTo("M             42  art/hero.png");
    }

    /// <summary>
    /// A node that exists only locally has no BASE revision, and a zero there would read as
    /// "committed at r0" rather than as "never committed".
    /// </summary>
    [Test]
    public async Task Verbose_marks_a_node_with_no_revision_rather_than_printing_a_number()
    {
        await Assert
            .That(
                StatusLine.Verbose(
                    Entry() with
                    {
                        Status = NodeStatus.Unversioned,
                        Revision = null,
                    }
                )
            )
            .IsEqualTo("?              -  art/hero.png");
    }

    private static WorkingCopyEntry Entry() =>
        new(
            "art/hero.png",
            NodeKind.File,
            NodeStatus.Unmodified,
            PropertyStatus.Unmodified,
            Revision: 42,
            Changelist: null,
            IsConflicted: false,
            HasLockToken: false,
            IsWriteLocked: false,
            IsCopied: false
        );
}
