// Runs `svn diff` exactly as Subverted.Svn's SvnCommand + SvnDiffCommand do, and writes the
// decoded standard output back out as UTF-8 bytes, so the file holds the string the daemon returns.
// usage: dotnet run capture.cs -- <wc-root> <path> <output-file> [<path> <output-file> ...]
using System.Diagnostics;
using System.Text;

var root = Path.GetFullPath(args[0]);
var utf8 = new UTF8Encoding(false);

for (var i = 1; i + 1 < args.Length; i += 2)
{
    var path = Path.GetFullPath(Path.Combine(root, args[i]));
    var output = args[i + 1];

    var startInfo = new ProcessStartInfo("svn")
    {
        WorkingDirectory = root,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = utf8,
        StandardErrorEncoding = utf8,
        UseShellExecute = false,
    };
    startInfo.ArgumentList.Add("diff");
    startInfo.ArgumentList.Add("--non-interactive");
    startInfo.ArgumentList.Add(Path.GetRelativePath(root, path));
    startInfo.Environment["LC_ALL"] = "C";

    using var process = Process.Start(startInfo)!;
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    File.WriteAllText(output, await stdout, utf8);
    Console.WriteLine(
        $"{Path.GetRelativePath(root, path)} -> {output}: exit {process.ExitCode}; {(await stdout).Length} chars; stderr: {(await stderr).Trim()}"
    );
}
