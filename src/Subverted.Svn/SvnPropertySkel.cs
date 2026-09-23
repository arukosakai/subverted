using System.Text;

namespace Subverted.Svn;

/// <summary>One entry of a node's property list.</summary>
internal readonly record struct SvnProperty(string Name, string Value);

/// <summary>
/// Parses the skel Subversion serialises property lists into, as stored in
/// <c>NODES.properties</c> and <c>ACTUAL_NODE.properties</c>.
/// </summary>
internal static class SvnPropertySkel
{
    /// <summary>
    /// A property list is a flat skel list of alternating name and value atoms. An atom is written
    /// bare when it starts with a letter and contains no whitespace or parenthesis, and as
    /// <c>&lt;length&gt;&lt;one space&gt;&lt;bytes&gt;</c> otherwise.
    /// </summary>
    /// <returns>
    /// Entries in stored order, empty when the node records none. <see langword="null"/> when the
    /// blob is not a well-formed property list, so callers fall back rather than guess.
    /// </returns>
    public static IReadOnlyList<SvnProperty>? Parse(byte[]? blob)
    {
        if (blob is null or { Length: 0 })
        {
            return [];
        }

        var span = blob.AsSpan();
        var position = SkipWhitespace(span, 0);
        if (position == span.Length || span[position] != (byte)'(')
        {
            return null;
        }

        position++;
        var atoms = new List<string>();
        while (true)
        {
            position = SkipWhitespace(span, position);
            if (position == span.Length)
            {
                return null;
            }

            if (span[position] == (byte)')')
            {
                position++;
                break;
            }

            if (!TryReadAtom(span, ref position, out var atom))
            {
                return null;
            }

            atoms.Add(atom);
        }

        var trailing = SkipWhitespace(span, position);
        return trailing == span.Length && atoms.Count % 2 == 0 ? PairUp(atoms) : null;
    }

    private static SvnProperty[] PairUp(List<string> atoms)
    {
        var properties = new SvnProperty[atoms.Count / 2];
        for (var i = 0; i < properties.Length; i++)
        {
            properties[i] = new SvnProperty(atoms[2 * i], atoms[2 * i + 1]);
        }

        return properties;
    }

    /// <summary>
    /// The length of an explicit atom is followed by exactly one whitespace byte, which is what
    /// lets a value begin with whitespace, a digit, or a parenthesis without ambiguity.
    /// </summary>
    private static bool TryReadAtom(ReadOnlySpan<byte> span, ref int position, out string atom)
    {
        atom = string.Empty;

        if (IsLetter(span[position]))
        {
            var start = position;
            while (
                position < span.Length
                && !IsWhitespace(span[position])
                && !IsParenthesis(span[position])
            )
            {
                position++;
            }

            atom = Decode(span[start..position]);
            return true;
        }

        if (!IsDigit(span[position]))
        {
            return false;
        }

        var length = 0;
        while (position < span.Length && IsDigit(span[position]))
        {
            // A length larger than the blob can never be satisfied, and checking as we go keeps a
            // long run of digits from overflowing before we get to reject it.
            length = length * 10 + (span[position] - (byte)'0');
            if (length > span.Length)
            {
                return false;
            }

            position++;
        }

        if (position == span.Length || !IsWhitespace(span[position]))
        {
            return false;
        }

        position++;
        if (span.Length - position < length)
        {
            return false;
        }

        atom = Decode(span.Slice(position, length));
        position += length;
        return true;
    }

    private static int SkipWhitespace(ReadOnlySpan<byte> span, int position)
    {
        while (position < span.Length && IsWhitespace(span[position]))
        {
            position++;
        }

        return position;
    }

    private static bool IsLetter(byte c) =>
        c is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z';

    private static bool IsDigit(byte c) => c is >= (byte)'0' and <= (byte)'9';

    private static bool IsWhitespace(byte c) =>
        c is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or (byte)'\v';

    private static bool IsParenthesis(byte c) => c is (byte)'(' or (byte)')';

    private static string Decode(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);
}
