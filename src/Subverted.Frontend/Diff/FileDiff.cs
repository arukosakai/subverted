namespace Subverted.Frontend.Diff;

/// <param name="Path">Slash-separated and relative to the working-copy root, as a status row's <c>RelPath</c> is.</param>
/// <param name="Content">What happened to the bytes, or <c>null</c> when only properties changed.</param>
/// <param name="PropertyChanges">Empty when no property changed.</param>
public sealed record FileDiff(
    string Path,
    FileContentChange? Content,
    IReadOnlyList<PropertyChange> PropertyChanges
);
