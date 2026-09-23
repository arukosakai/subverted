namespace Subverted.Svn;

/// <summary>The code pages of the console a process is attached to, as Windows reports them.</summary>
internal readonly record struct ConsoleCodePages(uint Output, uint Input)
{
    public const uint Utf8 = 65001;

    public bool AreBothUtf8 => Output == Utf8 && Input == Utf8;
}
