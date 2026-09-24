// Times the in-process diff (D36) on a 50,000-line file against `svn diff` on the same file, warm
// and with the file cache evicted, and times the search giving up on the worst shape it can meet.
//
//   dotnet run -c Release tools/measure-context-diff.cs -- [runs]
//
// Builds its own throwaway repository under %TEMP%\subverted-ctx-perf; touches nothing else.
#:project ../src/Subverted.Svn/Subverted.Svn.csproj

using System.Diagnostics;
using System.Text;
using Subverted.Core;
using Subverted.Svn;

const int FileFlagNoBuffering = 0x2000_0000;
const int Lines = 50_000;

var runs = args.Length > 0 ? int.Parse(args[0]) : 5;
var basePath = Path.Combine(Path.GetTempPath(), "subverted-ctx-perf");
var repository = Path.Combine(basePath, "repo");
var root = Path.Combine(basePath, "wc");
if (Directory.Exists(basePath))
{
    foreach (var file in Directory.EnumerateFiles(basePath, "*", SearchOption.AllDirectories))
    {
        File.SetAttributes(file, FileAttributes.Normal);
    }

    Directory.Delete(basePath, recursive: true);
}

Directory.CreateDirectory(basePath);
Run(basePath, "svnadmin", "create", repository);
Run(basePath, "svn", "checkout", "-q", "file:///" + repository.Replace('\\', '/'), root);

var random = new Random(50_000);
var code = Enumerable.Range(0, Lines).Select(i => $"    statement_{i} = compute({i % 97}, {i});\n").ToArray();
string[] alphabet = ["{\n", "}\n", "\n", "return;\n"];
var scrambled = Enumerable.Range(0, Lines).Select(_ => alphabet[random.Next(4)]).ToArray();
File.WriteAllText(Path.Combine(root, "big.cs"), string.Concat(code));
File.WriteAllText(Path.Combine(root, "scrambled.txt"), string.Concat(scrambled));
Run(root, "svn", "add", "-q", "big.cs", "scrambled.txt");
Run(root, "svn", "commit", "-q", "-m", "base");

foreach (var at in new[] { 10, 12_000, 25_000, 25_003, 49_990 })
{
    code[at] = $"    edited_{at}();\n";
}

File.WriteAllText(Path.Combine(root, "big.cs"), string.Concat(code));
File.WriteAllText(
    Path.Combine(root, "scrambled.txt"),
    string.Concat(Enumerable.Range(0, Lines).Select(_ => alphabet[random.Next(4)]))
);

var svn = new SvnCommand("svn");
var inProcess = new WorkingCopyContextDiff(svn.Spelling, Environment.NewLine);
var big = Path.Combine(root, "big.cs");
var pristines = Directory.EnumerateFiles(Path.Combine(root, ".svn", "pristine"), "*.svn-base", SearchOption.AllDirectories).ToArray();
string[] evicted = [big, .. pristines];

Console.WriteLine($"big.cs: {Lines:N0} lines, {new FileInfo(big).Length:N0} bytes, 5 lines edited\n");

var svnText = await new SvnDiffCommand(svn).ReadAsync(root, big, CancellationToken.None);
var ours = await inProcess.ReadAsync(root, big, DiffContext.Default, CancellationToken.None);
Console.WriteLine($"at 3 lines, in-process equals svn diff: {ours == svnText}\n");

await Report("svn diff, 3 lines, warm", async () => await new SvnDiffCommand(svn).ReadAsync(root, big, CancellationToken.None));
await Report("in-process, 10 lines, warm", () => inProcess.ReadAsync(root, big, new DiffContext(10), CancellationToken.None));
await Report("in-process, whole file, warm", () => inProcess.ReadAsync(root, big, DiffContext.WholeFile, CancellationToken.None));
Console.WriteLine();
await Report("read only, warm", () => Task.FromResult<string?>(ReadAll(evicted)));
await Report("read only, cold", () => { Evict(evicted); return Task.FromResult<string?>(ReadAll(evicted)); });
await Report("in-process, 10 lines, cold", () => { Evict(evicted); return inProcess.ReadAsync(root, big, new DiffContext(10), CancellationToken.None); });
await Report("in-process, whole file, cold", () => { Evict(evicted); return inProcess.ReadAsync(root, big, DiffContext.WholeFile, CancellationToken.None); });
Console.WriteLine();
var scrambledPath = Path.Combine(root, "scrambled.txt");
await Report(
    "scrambled 50k vs 50k, until the budget declines",
    () => inProcess.ReadAsync(root, scrambledPath, new DiffContext(10), CancellationToken.None)
);
var declined = await inProcess.ReadAsync(root, scrambledPath, new DiffContext(10), CancellationToken.None);
Console.WriteLine($"  declined (svn diff answers instead): {declined is null}");
return 0;

async Task Report(string label, Func<Task<string?>> pass)
{
    var times = new List<double>();
    for (var i = 0; i < runs; i++)
    {
        var clock = Stopwatch.StartNew();
        await pass();
        times.Add(clock.Elapsed.TotalMilliseconds);
    }

    times.Sort();
    Console.WriteLine($"{label,-50} {times[0],8:F1} – {times[^1],8:F1} ms  (median {times[times.Count / 2]:F1})");
}

static string ReadAll(string[] paths)
{
    var total = 0L;
    foreach (var path in paths)
    {
        total += File.ReadAllBytes(path).Length;
    }

    return total.ToString();
}

// Opening with FILE_FLAG_NO_BUFFERING drops a file's pages from the cache; the read-only row is what
// shows it worked, as in measure-pristine-compare.cs.
static void Evict(string[] paths)
{
    foreach (var path in paths)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, (FileOptions)FileFlagNoBuffering);
    }
}

static void Run(string workingDirectory, string executable, params string[] arguments)
{
    var start = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardError = true };
    foreach (var argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    using var process = Process.Start(start)!;
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"{executable} {string.Join(' ', arguments)}: {error}");
    }
}
