using System.Runtime.InteropServices;
using System.Text;

namespace Subverted.Svn;

/// <summary>
/// svn on Windows writing the ANSI code page. The spelling goes through the same Win32 call svn's
/// conversion does, so a name comes out exactly as svn prints it — best-fit letters included, which
/// is how <c>ż</c> becomes <c>z</c> on a code page without it.
/// </summary>
/// <remarks>Nothing touches Win32 until a member is used, so constructing one is safe anywhere.</remarks>
public sealed class AnsiCodePageSpelling : ISvnTextSpelling
{
    private const uint AnsiCodePage = 0;
    private const uint Utf8CodePage = 65001;

    private readonly Lazy<uint> _codePage = new(GetACP);
    private readonly Lazy<Encoding> _encoding;

    public AnsiCodePageSpelling()
    {
        _encoding = new(() =>
            _codePage.Value == Utf8CodePage
                ? Encoding.UTF8
                : CodePagesEncodingProvider.Instance.GetEncoding((int)_codePage.Value)
                    ?? Encoding.GetEncoding((int)_codePage.Value)
        );
    }

    public Encoding LinesThatAreNotUtf8 => _encoding.Value;

    /// <summary>A machine set to UTF-8 system-wide ("Beta: Use Unicode UTF-8") loses nothing.</summary>
    public bool CanLoseNames => _codePage.Value != Utf8CodePage;

    public string AsSvnWouldPrint(string name)
    {
        if (name.Length == 0)
        {
            return name;
        }

        var size = WideCharToMultiByte(AnsiCodePage, 0, name, name.Length, null, 0, 0, 0);
        var bytes = new byte[size];
        WideCharToMultiByte(AnsiCodePage, 0, name, name.Length, bytes, size, 0, 0);
        return _encoding.Value.GetString(bytes);
    }

    // DllImport rather than LibraryImport, for the reason StandardHandleInheritance gives.
    [DllImport("kernel32.dll")]
    private static extern uint GetACP();

    [DllImport("kernel32.dll")]
    private static extern int WideCharToMultiByte(
        uint codePage,
        uint flags,
        [MarshalAs(UnmanagedType.LPWStr)] string wide,
        int wideLength,
        byte[]? multiByte,
        int multiByteLength,
        nint defaultCharacter,
        nint usedDefaultCharacter
    );
}
