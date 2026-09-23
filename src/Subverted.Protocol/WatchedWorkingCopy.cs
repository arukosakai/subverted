namespace Subverted.Protocol;

/// <param name="EntryCount">Nodes currently held in memory, versioned and not.</param>
public sealed record WatchedWorkingCopy(string RootPath, int EntryCount, WatcherState WatcherState);
