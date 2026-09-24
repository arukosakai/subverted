using System.Text;
using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// Where several smallest diffs exist — repetitive text, the usual shape of code — the search
/// has to pick the one <c>svn diff</c> picks, or the same edit reads differently in the two. Checked
/// on generated files of few distinct lines, the shape most likely to part them, against live svn.
/// </summary>
public sealed class ContextDiffAlignmentIntegrationTests
{
    private const int Files = 100;
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly SvnCommand Svn = new("svn");

    [Test]
    public async Task Repetitive_files_edited_at_random_align_as_svn_aligns_them()
    {
        using var copy = SvnWorkingCopy.Create();
        var random = new Random(20260924);
        var edited = new List<(string Name, byte[] Text)>();
        for (var i = 0; i < Files; i++)
        {
            var name = $"f{i:D3}.txt";
            var original = RepetitiveLines(random, random.Next(0, 30));
            copy.WriteBytes(name, Encoding.ASCII.GetBytes(string.Concat(original)));
            edited.Add((name, Encoding.ASCII.GetBytes(string.Concat(Edited(random, original)))));
        }

        copy.Svn(["add", "--quiet", .. edited.Select(file => file.Name)]);
        copy.Svn("commit", "--quiet", "-m", "base");
        foreach (var (name, text) in edited)
        {
            copy.WriteBytes(name, text);
        }

        var differing = new List<string>();
        foreach (var (name, _) in edited)
        {
            var path = copy.Absolute(name);
            var svn = await new SvnDiffCommand(Svn).ReadAsync(copy.Root, path, None);
            var written = await new WorkingCopyContextDiff(
                Svn.Spelling,
                Environment.NewLine
            ).ReadAsync(copy.Root, path, DiffContext.Default, None);
            if (written != svn)
            {
                differing.Add($"{name}\n--- svn\n{svn}\n--- ours\n{written}");
            }
        }

        await Assert.That(string.Join("\n", differing)).IsEmpty();
    }

    private static List<string> RepetitiveLines(Random random, int count) =>
        [.. Enumerable.Range(0, count).Select(_ => Line(random))];

    private static string Line(Random random) =>
        random.Next(6) switch
        {
            0 => "\n",
            1 => "}\n",
            2 => "{\n",
            3 => "a\n",
            4 => "b\n",
            _ => $"x{random.Next(4)}\n",
        };

    private static List<string> Edited(Random random, List<string> original)
    {
        var lines = new List<string>(original);
        var edits = random.Next(1, 5);
        for (var e = 0; e < edits; e++)
        {
            var at = random.Next(lines.Count + 1);
            switch (random.Next(3))
            {
                case 0:
                    lines.Insert(at, Line(random));
                    break;
                case 1 when at < lines.Count:
                    lines.RemoveAt(at);
                    break;
                default:
                    if (at < lines.Count)
                    {
                        lines[at] = Line(random);
                    }

                    break;
            }
        }

        return lines;
    }
}
