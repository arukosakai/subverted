namespace Subverted.App.Presentation;

/// <summary>
/// One unit a line is compared in: a word, a run of whitespace, or a single symbol. Its bounds are
/// UTF-16 offsets that never fall inside a user-perceived character.
/// </summary>
public readonly record struct LineToken(int Start, int Length, bool IsWhitespace)
{
    public int End => Start + Length;
}
