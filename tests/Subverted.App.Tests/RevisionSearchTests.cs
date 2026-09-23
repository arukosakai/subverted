using Subverted.App.Presentation;

namespace Subverted.App.Tests;

public sealed class RevisionSearchTests
{
    private static readonly RevisionRow Row = Revisions.Row(
        120,
        message: "Fix the Hero walk cycle",
        author: "keiichi",
        "/trunk/art/hero.png"
    );

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task Nothing_typed_matches_everything(string query)
    {
        await Assert.That(RevisionSearch.Matches(Row, query)).IsTrue();
    }

    [Test]
    [Arguments("walk")]
    [Arguments("WALK CYCLE")]
    [Arguments("  hero walk  ")]
    public async Task The_message_matches_in_any_case_and_ignoring_surrounding_space(string query)
    {
        await Assert.That(RevisionSearch.Matches(Row, query)).IsTrue();
    }

    [Test]
    public async Task The_author_matches()
    {
        await Assert.That(RevisionSearch.Matches(Row, "KEIICHI")).IsTrue();
    }

    [Test]
    public async Task A_changed_path_matches()
    {
        await Assert.That(RevisionSearch.Matches(Row, "art/hero.PNG")).IsTrue();
    }

    [Test]
    public async Task Text_found_nowhere_does_not_match()
    {
        await Assert.That(RevisionSearch.Matches(Row, "villain")).IsFalse();
    }

    [Test]
    [Arguments("120")]
    [Arguments("r120")]
    [Arguments("R120")]
    public async Task A_revision_number_matches_that_revision_with_or_without_its_r(string query)
    {
        await Assert.That(RevisionSearch.Matches(Row, query)).IsTrue();
    }

    [Test]
    [Arguments("12")]
    [Arguments("r12")]
    [Arguments("1200")]
    public async Task A_revision_number_does_not_match_one_that_merely_shares_its_digits(
        string query
    )
    {
        await Assert.That(RevisionSearch.Matches(Row, query)).IsFalse();
    }

    /// <summary>Typed as a number, it is a revision — a message mentioning it does not count.</summary>
    [Test]
    public async Task A_number_is_a_revision_even_when_a_message_contains_it()
    {
        var row = Revisions.Row(5, message: "fix 12 bugs");

        await Assert.That(RevisionSearch.Matches(row, "12")).IsFalse();
    }

    [Test]
    [Arguments("r")]
    [Arguments("-120")]
    public async Task Something_that_is_not_quite_a_number_is_searched_as_text(string query)
    {
        var row = Revisions.Row(120, message: "refactor -120 lines");

        await Assert.That(RevisionSearch.Matches(row, query)).IsTrue();
    }
}
