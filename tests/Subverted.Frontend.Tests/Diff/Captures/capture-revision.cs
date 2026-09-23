// Runs `svn diff -c N URL@N` exactly as Subverted.Svn's SvnRevisionDiffCommand does — from the
// working-copy root, each path segment escaped, LC_ALL=C — and writes the decoded stdout as UTF-8.
// usage: dotnet run capture-revision.cs -- <wc-root> <repo-path> <revision> <output-file> [...]
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

var root = Path.GetFullPath(args[0]);
var utf8 = new UTF8Encoding(false);
var repositoryRoot = RepositoryRoot(root);

for (var i = 1; i + 2 < args.Length; i += 3)
{
    var (repositoryPath, revision, output) = (args[i], args[i + 1], args[i + 2]);
    var escaped = string.Join(
        '/',
        repositoryPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString)
    );
    var url = escaped.Length == 0 ? repositoryRoot : $"{repositoryRoot}/{escaped}";

    var (exit, stdout, stderr) = Svn(
        root,
        "diff",
        "--non-interactive",
        "--change",
        revision,
        $"{url}@{revision}"
    );
    File.WriteAllText(output, stdout, utf8);
    Console.WriteLine(
        $"{repositoryPath} -c {revision} -> {output}: exit {exit}; {stdout.Length} chars; stderr: {stderr.Trim()}"
    );
}

string RepositoryRoot(string wc)
{
    var (_, xml, _) = Svn(wc, "info", "--xml", ".");
    return XDocument.Parse(xml).Descendants("root").Single().Value.TrimEnd('/');
}

(int, string, string) Svn(string workingDirectory, params string[] arguments)
{
    var startInfo = new ProcessStartInfo("svn")
    {
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = utf8,
        StandardErrorEncoding = utf8,
        UseShellExecute = false,
    };
    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }
    startInfo.Environment["LC_ALL"] = "C";

    using var process = Process.Start(startInfo)!;
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    return (process.ExitCode, stdout.Result, stderr.Result);
}
