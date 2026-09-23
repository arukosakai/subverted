namespace Subverted.Frontend.Diff;

/// <summary>One <c>@@ -a,b +c,d @@</c> block. The counts are SVN's, not recounted from the lines.</summary>
public sealed record Hunk(
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount,
    IReadOnlyList<DiffLine> Lines
);
