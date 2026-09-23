namespace Subverted.Protocol;

/// <param name="Message">Already phrased for a human; front-ends print it as-is.</param>
public sealed record ErrorResponse(DaemonErrorKind Kind, string Message) : DaemonResponse;
