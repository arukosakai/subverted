namespace Subverted.Daemon.Tests;

internal sealed class FakeChangeNotifier : IChangeNotifier
{
    public event Action<WorkingCopyChange>? Changed;

    public bool IsWatching { get; set; } = true;

    public bool IsDisposed { get; private set; }

    /// <summary>Whether the session is still listening — a disposed session must not be.</summary>
    public bool HasSubscriber => Changed is not null;

    /// <summary>A change the notifier could not attribute — an overflow, as far as a session cares.</summary>
    public void RaiseChanged() => Changed?.Invoke(WorkingCopyChange.Unknown);

    public void RaiseChangedAt(string relPath) => Changed?.Invoke(new WorkingCopyChange(relPath));

    public void Dispose() => IsDisposed = true;
}
