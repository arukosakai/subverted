namespace Subverted.Protocol;

/// <summary>
/// The peer said something the wire format does not allow — an oversized frame, a truncated one,
/// or a payload that is not a message. Always a bug or a mismatched build, never user error.
/// </summary>
public sealed class ProtocolException(string message, Exception? innerException = null)
    : Exception(message, innerException);
