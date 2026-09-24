namespace Subverted.Svn;

/// <summary>One line of a text, as a slice of its bytes that includes the line ending.</summary>
/// <param name="Start">Offset of the line's first byte.</param>
/// <param name="Length">Bytes in the line, its ending included.</param>
/// <param name="HasEnding">
/// False only for a last line with no ending — what <c>\ No newline at end of file</c> is about.
/// </param>
internal readonly record struct TextLine(int Start, int Length, bool HasEnding);
