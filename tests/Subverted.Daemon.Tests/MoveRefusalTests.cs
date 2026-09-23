using Subverted.Core;

namespace Subverted.Daemon.Tests;

public sealed class MoveRefusalTests
{
    private const string Source = "/wc/art/hero.png";
    private const string Destination = "/wc/art/protagonist.png";

    [Test]
    [Arguments(MoveRoute.Ordinary)]
    [Arguments(MoveRoute.AlreadyRenamed)]
    public async Task A_route_that_did_the_move_has_nothing_to_explain(MoveRoute route)
    {
        await Assert.That(MoveRefusal.Explain(route, Source, Destination)).IsNull();
    }

    [Test]
    public async Task Nothing_at_the_source_names_both_paths()
    {
        var refusal = MoveRefusal.Explain(MoveRoute.NothingAtSource, Source, Destination);

        await Assert.That(refusal).IsNotNull();
        await Assert.That(refusal!).Contains(Source);
        await Assert.That(refusal).Contains(Destination);
    }

    /// <summary>
    /// The refusal a person is most likely to hit, and the one SVN describes as a directory problem.
    /// It has to name the destination, because that is the path they need to deal with.
    /// </summary>
    [Test]
    public async Task An_occupied_destination_says_which_path_is_in_the_way()
    {
        var refusal = MoveRefusal.Explain(MoveRoute.DestinationOccupied, Source, Destination);

        await Assert.That(refusal!).Contains(Destination);
        await Assert.That(refusal).Contains("already exists");
    }

    /// <summary>
    /// The refusal has to name the path that exists as well as the one that does not: telling
    /// someone to rename a directory back says nothing if it never says which one moved.
    /// </summary>
    [Test]
    public async Task A_directory_renamed_outside_svn_says_to_rename_it_back()
    {
        var refusal = MoveRefusal.Explain(MoveRoute.AlreadyRenamedDirectory, Source, Destination);

        await Assert.That(refusal!).Contains(Source);
        await Assert.That(refusal).Contains(Destination);
        await Assert.That(refusal).Contains("directory");
    }

    /// <summary>
    /// The arm the type system does not rule out: <see cref="MoveRoute"/> is an enum, so a value
    /// outside it can be cast into existence. Throwing beats returning null, which would report a
    /// refused move as a successful one.
    /// </summary>
    [Test]
    public async Task A_route_this_build_does_not_know_throws_rather_than_reporting_success()
    {
        await Assert
            .That(() => MoveRefusal.Explain((MoveRoute)999, Source, Destination))
            .Throws<ArgumentOutOfRangeException>();
    }
}
