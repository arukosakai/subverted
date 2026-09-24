using System.Text;

namespace Subverted.Svn;

/// <summary>
/// How the svn on this machine spells a path in its text output. On Windows every current build
/// writes the ANSI code page, which cannot hold every name (D34); elsewhere it is UTF-8.
/// </summary>
public interface ISvnTextSpelling
{
    /// <summary>What a line of svn's output that is not valid UTF-8 is written in.</summary>
    Encoding LinesThatAreNotUtf8 { get; }

    /// <summary>Whether a name can reach svn's text changed or lost — when it can, diffs are respelled.</summary>
    bool CanLoseNames { get; }

    /// <summary>The name as svn would print it: unchanged where nothing is lost.</summary>
    string AsSvnWouldPrint(string name);
}
