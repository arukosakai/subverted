namespace Subverted.Frontend.Diff;

/// <summary>A forward-only position in a diff's lines, shared by the readers of its sections.</summary>
internal sealed class LineCursor(IReadOnlyList<string> lines)
{
    private int index;

    /// <summary>The line at the position, or <c>null</c> once every line has been read.</summary>
    public string? Current => index < lines.Count ? lines[index] : null;

    public void Advance() => index++;
}
