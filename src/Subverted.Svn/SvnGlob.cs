namespace Subverted.Svn;

/// <summary>
/// Matches a filename against one of Subversion's ignore globs.
/// </summary>
/// <remarks>
/// Close to fnmatch but not identical, and the differences are the ones that bite: Subversion's
/// <c>*</c> matches a leading dot, so <c>*.rej</c> ignores <c>.foo.rej</c>, and matching stays
/// case-sensitive on Windows, where <c>*.tmp</c> leaves <c>CASE.TMP</c> unignored.
/// </remarks>
internal static class SvnGlob
{
    /// <param name="pattern">A glob supporting <c>*</c>, <c>?</c> and <c>[...]</c> classes.</param>
    /// <param name="name">A single path segment, never a path.</param>
    public static bool Matches(string pattern, string name)
    {
        var patternPosition = 0;
        var namePosition = 0;
        var starPattern = -1;
        var starName = 0;

        while (namePosition < name.Length)
        {
            if (patternPosition < pattern.Length && pattern[patternPosition] == '*')
            {
                starPattern = patternPosition++;
                starName = namePosition;
                continue;
            }

            if (
                patternPosition < pattern.Length
                && TryMatchOne(pattern, ref patternPosition, name[namePosition])
            )
            {
                namePosition++;
                continue;
            }

            if (starPattern < 0)
            {
                return false;
            }

            // Give the last '*' one more character and retry everything after it.
            patternPosition = starPattern + 1;
            namePosition = ++starName;
        }

        while (patternPosition < pattern.Length && pattern[patternPosition] == '*')
        {
            patternPosition++;
        }

        return patternPosition == pattern.Length;
    }

    /// <summary>Advances <paramref name="position"/> only when the character matches.</summary>
    private static bool TryMatchOne(string pattern, ref int position, char candidate)
    {
        if (pattern[position] == '?')
        {
            position++;
            return true;
        }

        if (pattern[position] == '[' && TryFindClassEnd(pattern, position, out var end))
        {
            if (!ClassMatches(pattern, position, end, candidate))
            {
                return false;
            }

            position = end + 1;
            return true;
        }

        if (pattern[position] != candidate)
        {
            return false;
        }

        position++;
        return true;
    }

    /// <summary>
    /// An unterminated <c>[</c> is a literal bracket rather than an error, which is what fnmatch
    /// does and therefore what a pattern written for <c>svn</c> will expect.
    /// </summary>
    private static bool TryFindClassEnd(string pattern, int start, out int end)
    {
        var position = start + 1;
        if (position < pattern.Length && pattern[position] is '!' or '^')
        {
            position++;
        }

        // A ']' in the first slot is a literal member, not the terminator.
        if (position < pattern.Length && pattern[position] == ']')
        {
            position++;
        }

        while (position < pattern.Length && pattern[position] != ']')
        {
            position++;
        }

        end = position;
        return position < pattern.Length;
    }

    private static bool ClassMatches(string pattern, int start, int end, char candidate)
    {
        var position = start + 1;
        var negated = pattern[position] is '!' or '^';
        if (negated)
        {
            position++;
        }

        var matched = false;
        while (position < end)
        {
            // A '-' is only a range when it sits between two members.
            if (position + 2 < end && pattern[position + 1] == '-')
            {
                if (candidate >= pattern[position] && candidate <= pattern[position + 2])
                {
                    matched = true;
                }

                position += 3;
                continue;
            }

            if (pattern[position] == candidate)
            {
                matched = true;
            }

            position++;
        }

        return matched != negated;
    }
}
