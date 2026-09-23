using System.Diagnostics;
using Subverted.Core;
using Subverted.Svn;

// Throwaway harness: point it at a working copy and see whether reading wc.db directly is both
// correct (diff it against `svn status`) and fast enough to justify skipping the CLI.
var path = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Directory.GetCurrentDirectory();

// --all drops the 25-entry cap so the output can be diffed line-for-line against `svn status`.
var listAll = args.Contains("--all");

try
{
    var stopwatch = Stopwatch.StartNew();

    using var reader = WcDbReader.Open(path);
    var openMs = stopwatch.Elapsed.TotalMilliseconds;

    var entries = new WorkingCopyScanner(reader, GlobalIgnoreConfiguration.Load()).Scan();
    stopwatch.Stop();

    Console.WriteLine($"root       {reader.Info.RootPath}");
    Console.WriteLine($"repository {reader.Info.RepositoryRoot}");
    Console.WriteLine($"uuid       {reader.Info.RepositoryUuid}");
    Console.WriteLine($"format     {reader.Info.Format}");
    Console.WriteLine();
    Console.WriteLine(
        $"{entries.Count} entries in {stopwatch.Elapsed.TotalMilliseconds:F1} ms "
            + $"(open {openMs:F1} ms)"
    );
    Console.WriteLine();

    foreach (var group in entries.GroupBy(e => e.Status).OrderByDescending(g => g.Count()))
    {
        Console.WriteLine($"  {group.Key, -22} {group.Count()}");
    }

    var locked = entries.Where(e => e.HasLockToken).ToList();
    if (locked.Count > 0)
    {
        Console.WriteLine();
        foreach (var entry in locked)
        {
            Console.WriteLine($"  {"locked", -22} {entry.RelPath}");
        }
    }

    var interesting = entries
        .Where(e =>
            e.Status != NodeStatus.Unmodified || e.PropertyStatus != PropertyStatus.Unmodified
        )
        .OrderBy(e => e.RelPath, StringComparer.Ordinal)
        .Take(listAll ? int.MaxValue : 25)
        .ToList();
    if (interesting.Count > 0)
    {
        Console.WriteLine();
        foreach (var entry in interesting)
        {
            var changelist = entry.Changelist is null ? string.Empty : $"  [{entry.Changelist}]";
            var properties =
                entry.PropertyStatus == PropertyStatus.Modified ? "props" : string.Empty;
            var copied = entry.IsCopied ? "copied" : string.Empty;
            var relPath = entry.RelPath.Length == 0 ? "." : entry.RelPath;
            Console.WriteLine(
                $"  {entry.Status, -22} {properties, -6} {copied, -6} {relPath}{changelist}"
            );
        }
    }

    return 0;
}
catch (WcDbException ex)
{
    Console.Error.WriteLine($"wc.db unreadable: {ex.Message}");
    Console.Error.WriteLine(
        "(this is the case where the real client falls back to `svn status --xml`)"
    );
    return 1;
}
