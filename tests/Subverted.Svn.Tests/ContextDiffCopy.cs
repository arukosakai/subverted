using System.Text;

namespace Subverted.Svn.Tests;

/// <summary>
/// One throwaway repository holding a file per case the in-process diff has to get right or
/// decline: r1 adds them, r2 edits each (History's case), and a local edit on top is Changes'.
/// Built once for the classes that share it, since every case is a read.
/// </summary>
internal static class ContextDiffCopy
{
    private static readonly Lazy<SvnWorkingCopy> Built = new(Build);

    public static SvnWorkingCopy Copy => Built.Value;

    public static string RepositoryRoot
    {
        get
        {
            using var reader = WcDbReader.Open(Copy.Root);
            return reader.Info.RepositoryRoot;
        }
    }

    public static void Dispose()
    {
        if (Built.IsValueCreated)
        {
            Built.Value.Dispose();
        }
    }

    private static byte[] Lines(int count, string ending = "\n") =>
        Encoding.ASCII.GetBytes(
            string.Concat(Enumerable.Range(1, count).Select(i => $"line {i}{ending}"))
        );

    private static byte[] Replaced(byte[] text, string old, string replacement)
    {
        var ascii = Encoding.ASCII.GetString(text);
        var at = ascii.IndexOf(old, StringComparison.Ordinal);
        if (at < 0)
        {
            throw new InvalidOperationException($"'{old}' is not in the fixture text.");
        }

        return Encoding.ASCII.GetBytes(ascii[..at] + replacement + ascii[(at + old.Length)..]);
    }

    /// <summary>Base, the r2 edit and the local edit on top of r2, for every case.</summary>
    private static readonly (
        string Name,
        byte[] Base,
        Func<byte[], byte[]> Commit,
        Func<byte[], byte[]> Local
    )[] Cases =
    [
        (
            "plain.txt",
            Lines(40),
            t => Replaced(Replaced(t, "line 5\n", "line five\n"), "line 30\n", "line thirty\n"),
            t =>
                Replaced(
                    Replaced(t, "line five\n", "line 5 again\n"),
                    "line 38\n",
                    "line thirty-eight\n"
                )
        ),
        (
            "gap5.txt",
            Lines(40),
            t => Replaced(Replaced(t, "line 10\n", "line ten\n"), "line 16\n", "line sixteen\n"),
            t =>
                Replaced(Replaced(t, "line 11\n", "line eleven\n"), "line 17\n", "line seventeen\n")
        ),
        (
            "gap6.txt",
            Lines(40),
            t => Replaced(Replaced(t, "line 10\n", "line ten\n"), "line 17\n", "line seventeen\n"),
            t => Replaced(Replaced(t, "line 11\n", "line eleven\n"), "line 18\n", "line eighteen\n")
        ),
        (
            "crlf-noprop.txt",
            Lines(12, "\r\n"),
            t => Replaced(t, "line 6\r\n", "line six\r\n"),
            t => Replaced(t, "line 7\r\n", "line seven\r\n")
        ),
        (
            "mixed-noprop.txt",
            Lines(12),
            t => Replaced(t, "line 6\n", "line six\r\n"),
            t => Replaced(t, "line 3\n", "line three\r\n")
        ),
        (
            "eol-only.txt",
            Lines(6),
            t => Replaced(t, "line 2\n", "line 2\r\n"),
            t => Replaced(t, "line 4\n", "line 4\r\n")
        ),
        (
            "lone-cr.txt",
            "a\rb\nc\nd\n"u8.ToArray(),
            t => Replaced(t, "b\n", "B\n"),
            t => Replaced(t, "c\n", "C\n")
        ),
        (
            "native.txt",
            Lines(12),
            t => Replaced(t, "line 6", "line six"),
            t => Replaced(t, "line 7", "line seven")
        ),
        (
            "lf-style.txt",
            Lines(12),
            t => Replaced(t, "line 6\n", "line six\n"),
            t => Replaced(t, "line 7\n", "line seven\n")
        ),
        (
            "crlf-style.txt",
            Lines(12),
            t => Replaced(t, "line 6", "line six"),
            t => Replaced(t, "line 7", "line seven")
        ),
        (
            "cr-style.txt",
            Lines(12),
            t => Replaced(t, "line 6", "line six"),
            t => Replaced(t, "line 7", "line seven")
        ),
        (
            "native-lf-on-disk.txt",
            Lines(12),
            t => Replaced(t, "line 6", "line six"),
            t =>
                Replaced(
                    Encoding.ASCII.GetBytes(
                        Encoding.ASCII.GetString(t).Replace("\r\n", "\n", StringComparison.Ordinal)
                    ),
                    "line 7",
                    "line seven"
                )
        ),
        (
            "text-mime.txt",
            Lines(12),
            t => Replaced(t, "line 6\n", "line six\n"),
            t => Replaced(t, "line 7\n", "line seven\n")
        ),
        (
            "html-mime.txt",
            "<p>1</p>\n<p>2</p>\n"u8.ToArray(),
            t => Replaced(t, "<p>2</p>", "<p>two</p>"),
            t => Replaced(t, "<p>1</p>", "<p>one</p>")
        ),
        ("empty.txt", [], _ => Lines(2), _ => Lines(1)),
        ("becomes-empty.txt", Lines(3), _ => [], _ => Lines(2)),
        (
            "no-eol.txt",
            "one\ntwo\nthree"u8.ToArray(),
            _ => "one\ntwo\nTHREE"u8.ToArray(),
            _ => "one\ntwo\nTHREE\nfour"u8.ToArray()
        ),
        (
            "gains-no-eol.txt",
            "one\ntwo\nthree\n"u8.ToArray(),
            _ => "one\ntwo\nthree"u8.ToArray(),
            _ => "one\ntwo\nthree\n"u8.ToArray()
        ),
        (
            "no-eol-context.txt",
            "a\nb\nc\nd"u8.ToArray(),
            _ => "a\nB\nc\nd"u8.ToArray(),
            _ => "A\nB\nc\nd"u8.ToArray()
        ),
        (
            "ambiguous.txt",
            "a\nb\nc\na\nb\nc\n"u8.ToArray(),
            t => [.. t, .. "x\n"u8],
            _ => "a\nb\nc\nx\na\nb\nc\na\nb\nc\nx\n"u8.ToArray()
        ),
        (
            "swap.txt",
            "1\n2\n3\n4\n5\n6\n7\n8\n"u8.ToArray(),
            _ => "5\n6\n7\n8\n1\n2\n3\n4\n"u8.ToArray(),
            _ => "1\n2\n3\n4\n5\n6\n7\n8\n"u8.ToArray()
        ),
        (
            "brace.txt",
            "f() {\n  a;\n}\n\ng() {\n  b;\n}\n"u8.ToArray(),
            _ => "f() {\n  a;\n}\n\nh() {\n  c;\n}\n\ng() {\n  b;\n}\n"u8.ToArray(),
            _ => "f() {\n  a;\n}\n\ng() {\n  b;\n}\n"u8.ToArray()
        ),
        (
            "keywords.txt",
            "top\n$Id$\nmid\nend\n"u8.ToArray(),
            t => Replaced(t, "end\n", "END\n"),
            t => Replaced(t, "mid\n", "MID\n")
        ),
        (
            "binary.dat",
            "a\0b\0c\n"u8.ToArray(),
            _ => "a\0B\0c\n"u8.ToArray(),
            _ => "a\0B\0C\n"u8.ToArray()
        ),
        (
            "binary-no-mime.dat",
            "x\0y\n"u8.ToArray(),
            _ => "x\0Y\n"u8.ToArray(),
            _ => "x\0Z\n"u8.ToArray()
        ),
        ("props.txt", Lines(5), t => t, t => t),
        (
            "props-and-text.txt",
            Lines(5),
            t => Replaced(t, "line 2\n", "line two\n"),
            t => Replaced(t, "line 3\n", "line three\n")
        ),
        ("missing.txt", Lines(5), t => t, t => t),
        ("copy-source.txt", Lines(5), t => t, t => t),
        ("unchanged.txt", Lines(5), t => t, t => t),
    ];

    private static SvnWorkingCopy Build()
    {
        var copy = SvnWorkingCopy.Create();
        foreach (var (name, content, _, _) in Cases)
        {
            copy.WriteBytes(name, content);
        }

        copy.WriteBytes("deleted-in-r2.txt", Lines(5));
        copy.Svn(["add", "--quiet", "deleted-in-r2.txt", .. Cases.Select(c => c.Name)]);
        copy.Svn(
            "propset",
            "--quiet",
            "svn:eol-style",
            "native",
            "native.txt",
            "native-lf-on-disk.txt"
        );
        copy.Svn("propset", "--quiet", "svn:eol-style", "LF", "lf-style.txt");
        copy.Svn("propset", "--quiet", "svn:eol-style", "CRLF", "crlf-style.txt");
        copy.Svn("propset", "--quiet", "svn:eol-style", "CR", "cr-style.txt");
        copy.Svn("propset", "--quiet", "svn:keywords", "Id", "keywords.txt");
        copy.Svn("propset", "--quiet", "svn:mime-type", "text/plain", "text-mime.txt");
        copy.Svn("propset", "--quiet", "svn:mime-type", "text/html", "html-mime.txt");
        copy.Svn("propset", "--quiet", "svn:mime-type", "application/octet-stream", "binary.dat");
        copy.Svn("propdel", "--quiet", "svn:mime-type", "binary-no-mime.dat");
        copy.Svn("commit", "--quiet", "-m", "r1");
        copy.Svn("update", "--quiet");

        foreach (var (name, _, commit, _) in Cases)
        {
            copy.WriteBytes(name, commit(File.ReadAllBytes(copy.Absolute(name))));
        }

        copy.Svn("propset", "--quiet", "custom:flag", "yes", "props.txt", "props-and-text.txt");
        copy.WriteBytes("added-in-r2.txt", Lines(3));
        copy.Svn("add", "--quiet", "added-in-r2.txt");
        copy.Svn("delete", "--quiet", "deleted-in-r2.txt");
        copy.Svn("commit", "--quiet", "-m", "r2");
        copy.Svn("update", "--quiet");

        foreach (var (name, _, _, local) in Cases)
        {
            copy.WriteBytes(name, local(File.ReadAllBytes(copy.Absolute(name))));
        }

        copy.Svn("propset", "--quiet", "custom:flag", "no", "props.txt", "props-and-text.txt");
        copy.Svn("delete", "--quiet", "copy-source.txt");
        copy.Delete("missing.txt");
        copy.Svn("copy", "--quiet", "unchanged.txt", "copied.txt");
        copy.WriteBytes("copied.txt", [.. Lines(5), .. "extra\n"u8]);
        copy.WriteBytes("added.txt", Lines(3));
        copy.Svn("add", "--quiet", "added.txt");
        return copy;
    }
}
