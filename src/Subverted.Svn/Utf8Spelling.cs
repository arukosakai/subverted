using System.Text;

namespace Subverted.Svn;

/// <summary>svn writing UTF-8, as it does under the locale <see cref="SvnLocale"/> gives it off Windows.</summary>
public sealed class Utf8Spelling : ISvnTextSpelling
{
    public Encoding LinesThatAreNotUtf8 => Encoding.UTF8;

    public bool CanLoseNames => false;

    public string AsSvnWouldPrint(string name) => name;
}
