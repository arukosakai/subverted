namespace Subverted.Frontend.Diff;

/// <summary>SVN declined to diff the content, because the file is marked binary.</summary>
/// <param name="MimeType">The <c>svn:mime-type</c> SVN names, or <c>null</c> when it names none.</param>
public sealed record BinaryChange(string? MimeType) : FileContentChange;
