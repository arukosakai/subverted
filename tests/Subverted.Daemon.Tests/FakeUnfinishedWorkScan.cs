namespace Subverted.Daemon.Tests;

/// <summary>
/// A work queue a test can change between scans, counting how often it was read. The counter is
/// the point: a session that re-reads on every request and one that reads once at open both
/// return a plausible number, and only the count tells them apart.
/// </summary>
internal sealed class FakeUnfinishedWorkScan : IUnfinishedWorkScan
{
    public int Reads { get; private set; }

    public int Operations { get; set; }

    public int CountUnfinishedOperations()
    {
        Reads++;
        return Operations;
    }
}
