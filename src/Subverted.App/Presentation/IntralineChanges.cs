namespace Subverted.App.Presentation;

/// <summary>
/// Which characters changed between a removed line and the added line it is paired with, so the
/// viewer can mark them inside the line's own tint.
/// </summary>
public sealed class IntralineChanges
{
    /// <summary>The longest line, on either side, that is compared at all; a longer pair marks nothing.</summary>
    public const int MaxComparedLineLength = 2_000;

    /// <summary>
    /// The most token pairs the token-by-token comparison will weigh. Past it, everything between
    /// the unchanged start and end of the line is marked whole, so no line makes a row slow to draw.
    /// </summary>
    public const int MaxComparedTokenPairs = 20_000;

    /// <summary>Nothing to mark on either side.</summary>
    public static readonly IntralineChanges None = new([], []);

    private IntralineChanges(IReadOnlyList<ChangedSpan> old, IReadOnlyList<ChangedSpan> @new)
    {
        Old = old;
        New = @new;
    }

    /// <summary>The changed spans of the removed line, in order and not overlapping.</summary>
    public IReadOnlyList<ChangedSpan> Old { get; }

    /// <summary>The changed spans of the added line, in order and not overlapping.</summary>
    public IReadOnlyList<ChangedSpan> New { get; }

    /// <returns>
    /// The tokens outside the longest sequence the two lines share, as spans; changes separated
    /// only by whitespace read as one. <see cref="None"/> when the lines share no text but
    /// whitespace — the whole line changed, and its tint already says so — or when either line is
    /// longer than <see cref="MaxComparedLineLength"/>.
    /// </returns>
    public static IntralineChanges Between(string oldText, string newText)
    {
        if (oldText.Length > MaxComparedLineLength || newText.Length > MaxComparedLineLength)
        {
            return None;
        }

        var vocabulary = new Dictionary<string, int>().GetAlternateLookup<ReadOnlySpan<char>>();
        var old = new Side(oldText, vocabulary);
        var @new = new Side(newText, vocabulary);
        MarkChangedTokens(old, @new);
        if (!old.KeepsVisibleText())
        {
            return None;
        }

        return new IntralineChanges(old.ChangedSpans(), @new.ChangedSpans());
    }

    private static void MarkChangedTokens(Side old, Side @new)
    {
        var shorter = Math.Min(old.Tokens.Count, @new.Tokens.Count);
        var prefix = 0;
        while (prefix < shorter && Side.Same(old, prefix, @new, prefix))
        {
            prefix++;
        }

        var suffix = 0;
        while (
            suffix < shorter - prefix
            && Side.Same(old, old.Tokens.Count - 1 - suffix, @new, @new.Tokens.Count - 1 - suffix)
        )
        {
            suffix++;
        }

        var oldEnd = old.Tokens.Count - suffix;
        var newEnd = @new.Tokens.Count - suffix;
        var comparedPairs = (long)(oldEnd - prefix) * (newEnd - prefix);
        if (comparedPairs > MaxComparedTokenPairs)
        {
            old.MarkChanged(prefix, oldEnd);
            @new.MarkChanged(prefix, newEnd);
            return;
        }

        MarkOutsideLongestCommonSequence(old, @new, prefix, oldEnd, newEnd);
    }

    /// <summary>
    /// The classic table of common-subsequence lengths, filled from the end so a forward walk can
    /// read which way to step; on a tie the old token is the one dropped.
    /// </summary>
    private static void MarkOutsideLongestCommonSequence(
        Side old,
        Side @new,
        int from,
        int oldEnd,
        int newEnd
    )
    {
        var rows = oldEnd - from;
        var columns = newEnd - from;
        var width = columns + 1;
        var common = new int[(rows + 1) * width];
        for (var row = rows - 1; row >= 0; row--)
        {
            for (var column = columns - 1; column >= 0; column--)
            {
                common[row * width + column] = Side.Same(old, from + row, @new, from + column)
                    ? common[(row + 1) * width + column + 1] + 1
                    : Math.Max(
                        common[(row + 1) * width + column],
                        common[row * width + column + 1]
                    );
            }
        }

        var oldIndex = 0;
        var newIndex = 0;
        while (oldIndex < rows && newIndex < columns)
        {
            if (Side.Same(old, from + oldIndex, @new, from + newIndex))
            {
                oldIndex++;
                newIndex++;
            }
            else if (
                common[(oldIndex + 1) * width + newIndex] >= common[oldIndex * width + newIndex + 1]
            )
            {
                old.Changed[from + oldIndex] = true;
                oldIndex++;
            }
            else
            {
                @new.Changed[from + newIndex] = true;
                newIndex++;
            }
        }

        old.MarkChanged(from + oldIndex, oldEnd);
        @new.MarkChanged(from + newIndex, newEnd);
    }

    /// <summary>One line's tokens, each numbered by its text so comparing two is comparing ints.</summary>
    private sealed class Side
    {
        private readonly string _text;
        private readonly int[] _textIds;

        public Side(
            string text,
            Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> vocabulary
        )
        {
            _text = text;
            Tokens = LineTokens.Of(text);
            Changed = new bool[Tokens.Count];
            _textIds =
            [
                .. Tokens.Select(token => IdOf(text.AsSpan(token.Start, token.Length), vocabulary)),
            ];
        }

        public IReadOnlyList<LineToken> Tokens { get; }

        public bool[] Changed { get; }

        public static bool Same(Side old, int oldIndex, Side @new, int newIndex) =>
            old._textIds[oldIndex] == @new._textIds[newIndex];

        public void MarkChanged(int from, int to) => Changed.AsSpan(from, to - from).Fill(true);

        public bool KeepsVisibleText() =>
            Tokens.Where((token, index) => !Changed[index] && !token.IsWhitespace).Any();

        public List<ChangedSpan> ChangedSpans()
        {
            var spans = new List<ChangedSpan>();
            for (var index = 0; index < Tokens.Count; index++)
            {
                if (!Changed[index])
                {
                    continue;
                }

                var token = Tokens[index];
                if (spans.Count > 0 && IsWhitespaceOnly(spans[^1].End, token.Start))
                {
                    spans[^1] = spans[^1] with { Length = token.End - spans[^1].Start };
                }
                else
                {
                    spans.Add(new ChangedSpan(token.Start, token.Length));
                }
            }

            return spans;
        }

        private static int IdOf(
            ReadOnlySpan<char> tokenText,
            Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> vocabulary
        )
        {
            if (!vocabulary.TryGetValue(tokenText, out var id))
            {
                id = vocabulary.Dictionary.Count;
                vocabulary[tokenText] = id;
            }

            return id;
        }

        private bool IsWhitespaceOnly(int from, int to) =>
            _text.AsSpan(from, to - from).IsWhiteSpace();
    }
}
