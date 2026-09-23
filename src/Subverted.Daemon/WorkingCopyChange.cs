namespace Subverted.Daemon;

/// <summary>
/// One filesystem event, reduced to the only thing a session can act on: which path moved.
/// </summary>
/// <param name="RelPath">
/// Slash-separated and relative to the working-copy root, or <see langword="null"/> when the
/// notifier cannot say. Prefer <see cref="Unknown"/> to writing the null.
/// </param>
public readonly record struct WorkingCopyChange(string? RelPath)
{
    /// <summary>
    /// Something moved and the notifier lost track of what — the buffer overflowed and events were
    /// dropped, so no set of paths describes the change. D5's rule still holds: this costs a full
    /// rescan, never a wrong answer.
    /// </summary>
    public static WorkingCopyChange Unknown { get; } = new(RelPath: null);
}
