namespace Subverted.Svn;

/// <summary>
/// Raised when wc.db cannot be read directly. A <see cref="WcDbFailure.Unreadable"/> failure is
/// what callers are expected to answer by shelling out to <c>svn status --xml</c> rather than
/// treating as fatal.
/// </summary>
public sealed class WcDbException(WcDbFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public WcDbFailure Failure { get; } = failure;
}
