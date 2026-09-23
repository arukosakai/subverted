// Times the two ways of settling a working copy by content — hashing the working file, and byte-
// comparing it against its pristine — with the OS file cache evicted first. Every figure in
// ARCHITECTURE.md D15 was taken on a tree resident in RAM, which is the one state where reading
// twice is free. This is the measurement D7 actually turns on.
//
//   dotnet run tools/measure-pristine-compare.cs -- <working-copy> [runs]
//
// Reads nothing but the filesystem: no wc.db, so this stays outside the rule that only
// Subverted.Svn knows SVN's private schema. The pristine of an unmodified file is located by
// hashing it, which is also what proves the tree is in the state the measurement needs.

using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;

const int FileFlagNoBuffering = 0x2000_0000;
const int ChunkBytes = 1 << 20;

if (args is not [var rootArgument, ..])
{
    Console.Error.WriteLine("usage: measure-pristine-compare.cs <working-copy> [runs]");
    return 2;
}

var root = Path.GetFullPath(rootArgument);
var runs = args.Length > 1 ? int.Parse(args[1]) : 3;
var pristineRoot = Path.Combine(root, ".svn", "pristine");

if (!Directory.Exists(pristineRoot))
{
    Console.Error.WriteLine($"no pristine store at {pristineRoot}");
    return 2;
}

var nodes = BuildManifest(root, pristineRoot, out var unpaired);
if (nodes.Count == 0)
{
    Console.Error.WriteLine("no file paired with a pristine — is anything here unmodified?");
    return 1;
}

var totalBytes = nodes.Sum(node => node.Length);
Console.WriteLine($"{nodes.Count} files paired with a pristine, {Bytes(totalBytes)}");
if (unpaired > 0)
{
    Console.WriteLine($"{unpaired} file(s) had no pristine — modified, translated, or added.");
}
Console.WriteLine($"eviction covers {Bytes(totalBytes * 2)} (working + pristine)\n");

var everything = nodes
    .Select(node => node.WorkingPath)
    .Concat(nodes.Select(node => node.PristinePath))
    .ToArray();

Report("read only, warm", runs, () => Measure(nodes, ReadOnlyPass), totalBytes);
Report("hash (what we do), warm", runs, () => Measure(nodes, HashPass), totalBytes);
Report("pristine compare, warm", runs, () => Measure(nodes, ComparePass), totalBytes);

Console.WriteLine();

Report(
    "read only, cold",
    runs,
    () =>
    {
        Evict(everything);
        return Measure(nodes, ReadOnlyPass);
    },
    totalBytes
);
Report(
    "hash (what we do), cold",
    runs,
    () =>
    {
        Evict(everything);
        return Measure(nodes, HashPass);
    },
    totalBytes
);
Report(
    "pristine compare, cold",
    runs,
    () =>
    {
        Evict(everything);
        return Measure(nodes, ComparePass);
    },
    totalBytes
);

return 0;

static List<Node> BuildManifest(string root, string pristineRoot, out int unpaired)
{
    var svnDirectory = Path.Combine(root, ".svn");
    var paired = new List<Node>();
    var missing = 0;

    foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
    {
        if (path.StartsWith(svnDirectory, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        using var stream = File.OpenRead(path);
        var sha1 = Convert.ToHexStringLower(SHA1.HashData(stream));
        var pristinePath = Path.Combine(pristineRoot, sha1[..2], $"{sha1}.svn-base");

        if (!File.Exists(pristinePath))
        {
            missing++;
            continue;
        }

        paired.Add(new Node(path, pristinePath, sha1, stream.Length));
    }

    unpaired = missing;
    return paired;
}

// Opening a file with FILE_FLAG_NO_BUFFERING drops its pages from the system cache, which is the
// only way to get a disk-cold read on a tree that fits in RAM. Whether it works is checked by the
// read-only rows above and below, not assumed.
static void Evict(IReadOnlyList<string> paths)
{
    foreach (var path in paths)
    {
        try
        {
            using var handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                (FileOptions)FileFlagNoBuffering
            );
        }
        catch (IOException)
        {
            // A file another process holds open cannot be evicted; it stays warm and is reported
            // as part of the run rather than skipped.
        }
    }
}

static TimeSpan Measure(List<Node> nodes, Action<Node> pass)
{
    var clock = Stopwatch.StartNew();
    Parallel.For(0, nodes.Count, i => pass(nodes[i]));
    clock.Stop();
    return clock.Elapsed;
}

static void ReadOnlyPass(Node node)
{
    var buffer = ArrayPool<byte>.Shared.Rent(ChunkBytes);
    try
    {
        using var stream = File.OpenRead(node.WorkingPath);
        while (stream.Read(buffer, 0, ChunkBytes) > 0) { }
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}

// Mirrors PristineComparer exactly — File.OpenRead plus SHA1.HashData(stream) — so this measures
// the shipped path rather than a faster stand-in.
static void HashPass(Node node)
{
    using var stream = File.OpenRead(node.WorkingPath);
    var actual = Convert.ToHexStringLower(SHA1.HashData(stream));
    if (!string.Equals(actual, node.Sha1, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{node.WorkingPath} changed mid-run");
    }
}

static void ComparePass(Node node)
{
    var working = ArrayPool<byte>.Shared.Rent(ChunkBytes);
    var pristine = ArrayPool<byte>.Shared.Rent(ChunkBytes);
    try
    {
        using var workingStream = File.OpenRead(node.WorkingPath);
        using var pristineStream = File.OpenRead(node.PristinePath);

        while (true)
        {
            var read = workingStream.ReadAtLeast(working, ChunkBytes, throwOnEndOfStream: false);
            var other = pristineStream.ReadAtLeast(pristine, ChunkBytes, throwOnEndOfStream: false);

            if (read != other)
            {
                throw new InvalidOperationException($"{node.WorkingPath} differs in length");
            }

            if (read == 0)
            {
                return;
            }

            if (!working.AsSpan(0, read).SequenceEqual(pristine.AsSpan(0, read)))
            {
                throw new InvalidOperationException(
                    $"{node.WorkingPath} differs from its pristine"
                );
            }
        }
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(working);
        ArrayPool<byte>.Shared.Return(pristine);
    }
}

static void Report(string label, int runs, Func<TimeSpan> run, long totalBytes)
{
    var timings = new List<TimeSpan>();
    for (var i = 0; i < runs; i++)
    {
        timings.Add(run());
    }

    var fastest = timings.Min();
    var slowest = timings.Max();
    var throughput = totalBytes / slowest.TotalSeconds / (1024.0 * 1024.0 * 1024.0);
    var ceiling = totalBytes / fastest.TotalSeconds / (1024.0 * 1024.0 * 1024.0);

    Console.WriteLine(
        $"{label, -28} {fastest.TotalMilliseconds, 8:N0} – {slowest.TotalMilliseconds, -8:N0} ms"
            + $"   {throughput, 5:N2} – {ceiling, -5:N2} GiB/s"
    );
}

static string Bytes(long count) => $"{count / (1024.0 * 1024.0 * 1024.0):N2} GiB";

internal readonly record struct Node(
    string WorkingPath,
    string PristinePath,
    string Sha1,
    long Length
);
