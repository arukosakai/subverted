namespace Subverted.Frontend.Diff;

/// <param name="Text">The line without its prefix character or its line ending.</param>
/// <param name="OldNumber">Its line number before the change; <c>null</c> for an added line.</param>
/// <param name="NewNumber">Its line number after the change; <c>null</c> for a removed line.</param>
/// <param name="EndsWithoutNewline">SVN printed <c>\ No newline at end of file</c> after it.</param>
public sealed record DiffLine(
    DiffLineKind Kind,
    string Text,
    int? OldNumber,
    int? NewNumber,
    bool EndsWithoutNewline
);
