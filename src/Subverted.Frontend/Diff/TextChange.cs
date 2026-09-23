namespace Subverted.Frontend.Diff;

public sealed record TextChange(IReadOnlyList<Hunk> Hunks) : FileContentChange;
