namespace Subverted.Svn;

/// <summary>A file's <c>svn:eol-style</c>, which says what its line endings are in the pristine.</summary>
internal enum LineEndingStyle
{
    /// <summary>No <c>svn:eol-style</c>: the working file's bytes are the pristine's bytes.</summary>
    AsCommitted,

    /// <summary><c>native</c>: <c>\n</c> in the pristine, whatever the working file uses.</summary>
    Native,

    /// <summary><c>LF</c>: <c>\n</c> in the pristine.</summary>
    Lf,

    /// <summary><c>CRLF</c>: <c>\r\n</c> in the pristine — not <c>\n</c>, measured on 1.8.15.</summary>
    CrLf,

    /// <summary><c>CR</c>: a lone <c>\r</c> in the pristine.</summary>
    Cr,
}
