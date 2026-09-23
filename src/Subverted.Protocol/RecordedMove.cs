namespace Subverted.Protocol;

/// <summary>A rename this request recorded with history, spelled as status entries spell paths.</summary>
/// <param name="FromRelPath">The path the node had, now scheduled for deletion.</param>
/// <param name="ToRelPath">The path it has, now scheduled as a copy of the old one.</param>
public sealed record RecordedMove(string FromRelPath, string ToRelPath);
