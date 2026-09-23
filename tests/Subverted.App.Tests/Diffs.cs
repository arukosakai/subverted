using Subverted.Frontend.Diff;

namespace Subverted.App.Tests;

/// <summary>
/// Diff documents shaped like what <c>svn diff</c> prints for each case GUI.md names: the counts in
/// each hunk header agree with its lines, and numbers run on from the header as SVN's do.
/// </summary>
internal static class Diffs
{
    public static DiffLine Context(int old, int @new, string text) =>
        new(DiffLineKind.Context, text, old, @new, EndsWithoutNewline: false);

    public static DiffLine Added(int @new, string text, bool endsWithoutNewline = false) =>
        new(DiffLineKind.Added, text, null, @new, endsWithoutNewline);

    public static DiffLine Removed(int old, string text, bool endsWithoutNewline = false) =>
        new(DiffLineKind.Removed, text, old, null, endsWithoutNewline);

    public static FileDiff Text(string path, params Hunk[] hunks) =>
        new(path, new TextChange(hunks), []);

    public static DiffDocument Document(params FileDiff[] files) => new(files);

    /// <summary><c>@@ -10,7 +10,8 @@</c>: one line replaced by two, three lines of context either side.</summary>
    public static readonly Hunk ModifiedHunk = new(
        10,
        7,
        10,
        8,
        [
            Context(10, 10, "    public void Update(float delta)"),
            Context(11, 11, "    {"),
            Context(12, 12, "        velocity += gravity * delta;"),
            Removed(13, "        position += velocity;"),
            Added(13, "        position += velocity * delta;"),
            Added(14, "        ClampToLevel();"),
            Context(14, 15, "    }"),
            Context(15, 16, ""),
            Context(16, 17, "    private void Jump()"),
        ]
    );

    /// <summary><c>@@ -40 +41 @@</c>: SVN leaves a count of one out of the header.</summary>
    public static readonly Hunk OneLineHunk = new(
        40,
        1,
        41,
        1,
        [Removed(40, "    const int MaxJumps = 1;"), Added(41, "    const int MaxJumps = 2;")]
    );

    public static readonly DiffDocument Modified = Document(
        Text("src/Player.cs", ModifiedHunk, OneLineHunk)
    );

    /// <summary>An added file is one hunk from <c>-0,0</c>: every line new, no old numbers.</summary>
    public static readonly DiffDocument AddedFile = Document(
        Text(
            "src/Enemy.cs",
            new Hunk(
                0,
                0,
                1,
                4,
                [
                    Added(1, "namespace Game;"),
                    Added(2, ""),
                    Added(3, "public sealed class Enemy"),
                    Added(4, "{ }"),
                ]
            )
        )
    );

    /// <summary>A deleted file is one hunk to <c>+0,0</c>: every line gone, no new numbers.</summary>
    public static readonly DiffDocument DeletedFile = Document(
        Text(
            "levels/old-cave.map",
            new Hunk(
                1,
                3,
                0,
                0,
                [Removed(1, "size 64 64"), Removed(2, "spawn 3 4"), Removed(3, "exit 60 60")]
            )
        )
    );

    /// <summary>The last line changed, and neither side ends with a newline.</summary>
    public static readonly DiffDocument NoNewlineAtEnd = Document(
        Text(
            "config/version.txt",
            new Hunk(
                1,
                2,
                1,
                2,
                [
                    Context(1, 1, "product=Subverted"),
                    Removed(2, "version=1.4", endsWithoutNewline: true),
                    Added(2, "version=1.5", endsWithoutNewline: true),
                ]
            )
        )
    );

    public static readonly PropertyChange EolStyleAdded = new(
        "svn:eol-style",
        PropertyChangeKind.Added,
        [new Hunk(0, 0, 1, 1, [Added(1, "native")])]
    );

    public static readonly PropertyChange KeywordsModified = new(
        "svn:keywords",
        PropertyChangeKind.Modified,
        [new Hunk(1, 1, 1, 1, [Removed(1, "Id"), Added(1, "Id Rev")])]
    );

    public static readonly PropertyChange IgnoreDeleted = new(
        "svn:ignore",
        PropertyChangeKind.Deleted,
        [new Hunk(1, 2, 0, 0, [Removed(1, "bin"), Removed(2, "obj")])]
    );

    /// <summary>A property-only change: SVN prints no content at all, only the property section.</summary>
    public static readonly DiffDocument PropertiesOnly = Document(
        new FileDiff("src/Player.cs", null, [EolStyleAdded, KeywordsModified])
    );

    public static readonly DiffDocument ContentAndProperties = Document(
        new FileDiff("src/Player.cs", new TextChange([OneLineHunk]), [KeywordsModified])
    );

    /// <summary>A newly added image: SVN declines the content and prints the mime-type it set.</summary>
    public static readonly DiffDocument Binary = Document(
        new FileDiff(
            "art/hero.png",
            new BinaryChange("image/png"),
            [
                new PropertyChange(
                    "svn:mime-type",
                    PropertyChangeKind.Added,
                    [new Hunk(0, 0, 1, 1, [Added(1, "image/png")])]
                ),
            ]
        )
    );

    /// <summary>A directory row's diff: every changed file beneath it, in SVN's order.</summary>
    public static readonly DiffDocument WholeDirectory = Document(
        Text(
            "levels/forest.map",
            new Hunk(3, 1, 3, 1, [Removed(3, "trees 40"), Added(3, "trees 55")])
        ),
        new FileDiff("levels/music.ogg", new BinaryChange(null), []),
        Text(
            "levels/cave.map",
            new Hunk(0, 0, 1, 2, [Added(1, "size 32 32"), Added(2, "spawn 1 1")])
        ),
        new FileDiff("levels", null, [IgnoreDeleted])
    );

    public static readonly DiffDocument LongLine = Document(
        Text("data/items.json", new Hunk(1, 1, 1, 1, [Removed(1, Json(40)), Added(1, Json(60))]))
    );

    /// <summary>
    /// A heavily edited file: hunks of a hundred lines each, every third line replaced, numbers
    /// contiguous within a hunk as SVN's are. 500 hunks is 50,000 lines.
    /// </summary>
    public static DiffDocument Huge(int hunkCount)
    {
        var hunks = new List<Hunk>();
        for (var index = 0; index < hunkCount; index++)
        {
            var start = 1 + index * 100;
            var hunkLines = new List<DiffLine>();
            for (var number = start; number < start + 75; number += 3)
            {
                hunkLines.Add(Context(number, number, $"line {number} stays"));
                hunkLines.Add(Removed(number + 1, $"line {number + 1} was here"));
                hunkLines.Add(Added(number + 1, $"line {number + 1} is here now"));
                hunkLines.Add(Context(number + 2, number + 2, $"line {number + 2} stays"));
            }

            hunks.Add(new Hunk(start, 75, start, 75, hunkLines));
        }

        return Document(Text("data/generated.txt", [.. hunks]));
    }

    private static string Json(int items) =>
        "["
        + string.Join(
            ",",
            Enumerable.Range(1, items).Select(i => $"{{\"id\":{i},\"name\":\"item-{i}\"}}")
        )
        + "]";
}
