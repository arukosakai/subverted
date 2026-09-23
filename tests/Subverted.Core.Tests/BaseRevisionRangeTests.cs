namespace Subverted.Core.Tests;

public sealed class BaseRevisionRangeTests
{
    [Test]
    public async Task A_copy_whose_nodes_are_all_at_one_revision_is_not_mixed()
    {
        await Assert.That(new BaseRevisionRange(9, 9).IsMixed).IsFalse();
    }

    [Test]
    public async Task A_copy_one_revision_apart_is_already_mixed()
    {
        await Assert.That(new BaseRevisionRange(8, 9).IsMixed).IsTrue();
    }
}
