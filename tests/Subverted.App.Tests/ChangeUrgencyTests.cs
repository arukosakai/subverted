using Subverted.App.Presentation;

namespace Subverted.App.Tests;

public sealed class ChangeUrgencyTests
{
    [Test]
    public async Task Nothing_at_all_is_quiet()
    {
        await Assert.That(ChangeUrgency.MostUrgentOf([])).IsEqualTo(ChangeTone.Quiet);
    }

    [Test]
    public async Task Only_quiet_tones_stay_quiet()
    {
        await Assert
            .That(ChangeUrgency.MostUrgentOf([ChangeTone.Quiet, ChangeTone.Quiet]))
            .IsEqualTo(ChangeTone.Quiet);
    }

    [Test]
    public async Task Any_tone_that_needs_someone_outranks_quiet()
    {
        await Assert
            .That(ChangeUrgency.MostUrgentOf([ChangeTone.Quiet, ChangeTone.Unversioned]))
            .IsEqualTo(ChangeTone.Unversioned);
    }

    /// <summary>Each neighbouring pair in both input orders, so every step of the ranking is pinned.</summary>
    [Test]
    [Arguments(ChangeTone.Conflict, ChangeTone.Missing)]
    [Arguments(ChangeTone.Missing, ChangeTone.Modified)]
    [Arguments(ChangeTone.Modified, ChangeTone.Added)]
    [Arguments(ChangeTone.Added, ChangeTone.Deleted)]
    [Arguments(ChangeTone.Deleted, ChangeTone.Replaced)]
    [Arguments(ChangeTone.Replaced, ChangeTone.Renamed)]
    [Arguments(ChangeTone.Renamed, ChangeTone.Unversioned)]
    public async Task The_more_urgent_of_two_wins_whichever_comes_first(
        ChangeTone urgent,
        ChangeTone lesser
    )
    {
        await Assert.That(ChangeUrgency.MostUrgentOf([urgent, lesser])).IsEqualTo(urgent);
        await Assert.That(ChangeUrgency.MostUrgentOf([lesser, urgent])).IsEqualTo(urgent);
    }
}
