namespace Subverted.Svn;

/// <summary>
/// Whether a file's properties leave its text something the daemon can compare the way
/// <c>svn diff</c> does, and if so how its line endings are normalised first.
/// </summary>
/// <remarks>
/// Deliberately short of what SVN supports. <c>svn:keywords</c> is refused because SVN contracts
/// expanded keywords before comparing, by rules not reproduced here; <c>svn:special</c> because the
/// pristine of a link is not its target; and any <c>svn:mime-type</c> outside <c>text/</c> because
/// that is SVN's own test for binary, and a binary file has no lines to give context to.
/// </remarks>
internal static class ComparableText
{
    /// <returns>
    /// How the working text is brought to normal form, or <see langword="null"/> when this file's
    /// diff has to come from <c>svn diff</c>.
    /// </returns>
    public static LineEndingStyle? LineEndingsOf(IReadOnlyList<SvnProperty> properties)
    {
        var style = LineEndingStyle.AsCommitted;
        foreach (var property in properties)
        {
            switch (property.Name)
            {
                case "svn:keywords":
                case "svn:special":
                    return null;

                case "svn:mime-type" when !property.Value.StartsWith("text/", StringComparison.Ordinal):
                    return null;

                case "svn:eol-style":
                    if (StyleNamed(property.Value) is not { } named)
                    {
                        return null;
                    }

                    style = named;
                    break;
            }
        }

        return style;
    }

    /// <remarks>The four values <c>svn propset</c> accepts, spelled as it requires them.</remarks>
    private static LineEndingStyle? StyleNamed(string value) =>
        value switch
        {
            "native" => LineEndingStyle.Native,
            "LF" => LineEndingStyle.Lf,
            "CRLF" => LineEndingStyle.CrLf,
            "CR" => LineEndingStyle.Cr,
            _ => null,
        };
}
