namespace Subverted.Daemon;

/// <summary>
/// Maps an absolute path to the one spelling the daemon compares by. Declared here so the request
/// handler can be tested with any respelling, the identity included.
/// </summary>
public delegate string RespellPath(string absolutePath);
