using Subverted.Core;

namespace Subverted.Svn.Tests;

public sealed class MoveRouteResolverTests
{
    [Test]
    public async Task A_source_on_disk_and_a_free_destination_is_an_ordinary_move()
    {
        await Assert
            .That(MoveRouteResolver.Resolve(sourceExists: true, destinationKind: null))
            .IsEqualTo(MoveRoute.Ordinary);
    }

    [Test]
    [Arguments(NodeKind.File)]
    [Arguments(NodeKind.Directory)]
    [Arguments(NodeKind.Symlink)]
    public async Task Anything_at_the_destination_refuses_a_move_onto_it(NodeKind occupant)
    {
        await Assert
            .That(MoveRouteResolver.Resolve(sourceExists: true, occupant))
            .IsEqualTo(MoveRoute.DestinationOccupied);
    }

    /// <summary>
    /// The case <c>svn move</c> cannot do at all: the rename already happened in a file manager, so
    /// there is nothing at the source for SVN to move.
    /// </summary>
    [Test]
    public async Task A_missing_source_and_a_file_at_the_destination_is_a_rename_already_made()
    {
        await Assert
            .That(MoveRouteResolver.Resolve(sourceExists: false, NodeKind.File))
            .IsEqualTo(MoveRoute.AlreadyRenamed);
    }

    /// <summary>
    /// A symlink is a file as far as a rename goes, and must take the file route rather than falling
    /// into the directory one — which refuses.
    /// </summary>
    [Test]
    public async Task A_symlink_at_the_destination_takes_the_file_route()
    {
        await Assert
            .That(MoveRouteResolver.Resolve(sourceExists: false, NodeKind.Symlink))
            .IsEqualTo(MoveRoute.AlreadyRenamed);
    }

    [Test]
    public async Task A_missing_source_and_a_directory_at_the_destination_is_refused()
    {
        await Assert
            .That(MoveRouteResolver.Resolve(sourceExists: false, NodeKind.Directory))
            .IsEqualTo(MoveRoute.AlreadyRenamedDirectory);
    }

    [Test]
    public async Task Neither_path_holding_anything_is_no_rename_at_all()
    {
        await Assert
            .That(MoveRouteResolver.Resolve(sourceExists: false, destinationKind: null))
            .IsEqualTo(MoveRoute.NothingAtSource);
    }
}
