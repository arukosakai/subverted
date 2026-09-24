using System.Globalization;
using System.Text;

namespace Subverted.App.Presentation;

/// <summary>Cuts a line into the tokens intraline highlighting compares.</summary>
public static class LineTokens
{
    /// <returns>
    /// The line's tokens in order, covering every character once. Letters, digits and underscores
    /// run together into a word; whitespace runs together; anything else is a token of its own.
    /// Tokens are whole grapheme clusters, so a surrogate pair or a combining mark is never split.
    /// </returns>
    public static IReadOnlyList<LineToken> Of(string text)
    {
        var tokens = new List<LineToken>();
        CharacterClass? previous = null;
        for (var index = 0; index < text.Length; )
        {
            var length = StringInfo.GetNextTextElementLength(text, index);
            var current = ClassOf(text, index);
            if (current == previous && current != CharacterClass.Symbol)
            {
                var last = tokens[^1];
                tokens[^1] = last with { Length = last.Length + length };
            }
            else
            {
                tokens.Add(new LineToken(index, length, current == CharacterClass.Whitespace));
            }

            previous = current;
            index += length;
        }

        return tokens;
    }

    /// <summary>A cluster is classed by its first code point; a lone surrogate reads as a symbol.</summary>
    private static CharacterClass ClassOf(string text, int index)
    {
        Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out _);
        if (Rune.IsWhiteSpace(rune))
        {
            return CharacterClass.Whitespace;
        }

        var isWordCharacter =
            Rune.IsLetterOrDigit(rune)
            || Rune.GetUnicodeCategory(rune) == UnicodeCategory.ConnectorPunctuation;
        return isWordCharacter ? CharacterClass.Word : CharacterClass.Symbol;
    }

    private enum CharacterClass
    {
        Whitespace,
        Word,
        Symbol,
    }
}
