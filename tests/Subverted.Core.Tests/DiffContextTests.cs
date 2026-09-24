namespace Subverted.Core.Tests;

public sealed class DiffContextTests
{
    [Test]
    public async Task The_default_is_the_three_lines_svn_diff_prints()
    {
        await Assert.That(DiffContext.Default.LinesAround).IsEqualTo(3);
    }

    [Test]
    public async Task The_whole_file_has_no_limit_on_its_lines()
    {
        await Assert.That(DiffContext.WholeFile.LinesAround).IsNull();
    }

    [Test]
    [Arguments(4)]
    [Arguments(25)]
    public async Task More_than_three_lines_is_wider_than_the_default(int lines)
    {
        await Assert.That(new DiffContext(lines).IsWiderThanDefault).IsTrue();
    }

    [Test]
    [Arguments(3)]
    [Arguments(2)]
    [Arguments(0)]
    public async Task Three_lines_or_fewer_is_not_wider_than_the_default(int lines)
    {
        await Assert.That(new DiffContext(lines).IsWiderThanDefault).IsFalse();
    }

    [Test]
    public async Task The_whole_file_is_wider_than_the_default()
    {
        await Assert.That(DiffContext.WholeFile.IsWiderThanDefault).IsTrue();
    }
}
