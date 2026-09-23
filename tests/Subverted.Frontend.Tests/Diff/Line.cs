using Subverted.Frontend.Diff;

namespace Subverted.Frontend.Tests.Diff;

/// <summary>Expected <see cref="DiffLine"/>s, spelled the way a hunk reads.</summary>
internal static class Line
{
    public static DiffLine Context(string text, int oldNumber, int newNumber) =>
        new(DiffLineKind.Context, text, oldNumber, newNumber, EndsWithoutNewline: false);

    public static DiffLine Removed(string text, int oldNumber, bool endsWithoutNewline = false) =>
        new(DiffLineKind.Removed, text, oldNumber, null, endsWithoutNewline);

    public static DiffLine Added(string text, int newNumber, bool endsWithoutNewline = false) =>
        new(DiffLineKind.Added, text, null, newNumber, endsWithoutNewline);
}
