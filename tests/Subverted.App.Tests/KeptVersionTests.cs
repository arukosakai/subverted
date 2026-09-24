using Subverted.App.Presentation;
using Subverted.Core;

namespace Subverted.App.Tests;

public sealed class KeptVersionTests
{
    [Test]
    [Arguments(ConflictResolution.Working, "the files as they are on disk")]
    [Arguments(ConflictResolution.Mine, "your version")]
    [Arguments(ConflictResolution.Theirs, "the incoming version")]
    [Arguments(ConflictResolution.Base, "the revision both sides started from")]
    public async Task Each_resolution_names_the_version_it_keeps(
        ConflictResolution kept,
        string said
    )
    {
        await Assert.That(KeptVersion.Of(kept)).IsEqualTo(said);
    }

    [Test]
    public async Task A_value_outside_the_enum_is_refused_rather_than_named()
    {
        await Assert
            .That(() => KeptVersion.Of((ConflictResolution)99))
            .Throws<ArgumentOutOfRangeException>();
    }
}
