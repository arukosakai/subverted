namespace Subverted.Svn;

/// <summary>
/// The <c>svn</c> client could not answer: it is not on PATH, it exited non-zero, or it wrote
/// something this build cannot read. Callers turn it into a message for the user, never into a
/// crash — the escape hatch is that the same command still works typed by hand.
/// </summary>
public sealed class SvnCommandException(string message, Exception? innerException = null)
    : Exception(message, innerException);
