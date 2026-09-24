using Subverted.Core;

namespace Subverted.Frontend.Tests;

public sealed class ResolutionRiskTests
{
    [Test]
    [Arguments(ConflictResolution.Theirs, true)]
    [Arguments(ConflictResolution.Base, true)]
    [Arguments(ConflictResolution.Mine, false)]
    [Arguments(ConflictResolution.Working, false)]
    public async Task Only_the_versions_that_discard_local_work_overwrite_it(
        ConflictResolution resolution,
        bool expected
    )
    {
        await Assert.That(ResolutionRisk.OverwritesLocalWork(resolution)).IsEqualTo(expected);
    }
}
