namespace Subverted.Daemon.Tests;

/// <summary>
/// The rule with the filesystem handed in: <c>SHORT~1</c> stands for a component whose long name is
/// <c>ShortName</c>, which is the only respelling the fake knows.
/// </summary>
public sealed class PathRespellingTests
{
    private static readonly string Root = Path.GetFullPath("/");

    [Test]
    public async Task An_existing_path_takes_the_filesystems_spelling()
    {
        var respelled = Respell(Under("SHORT~1", "art"), existing: [Under("SHORT~1", "art")]);

        await Assert.That(respelled).IsEqualTo(Under("ShortName", "art"));
    }

    /// <summary>A move's destination is not there yet; what it sits in is.</summary>
    [Test]
    public async Task A_path_not_there_yet_is_respelled_through_what_contains_it()
    {
        var respelled = Respell(
            Under("SHORT~1", "art", "new", "hero.png"),
            existing: [Under("SHORT~1", "art")]
        );

        await Assert.That(respelled).IsEqualTo(Under("ShortName", "art", "new", "hero.png"));
    }

    /// <summary>The missing part is kept exactly as given: it has no other spelling to take.</summary>
    [Test]
    public async Task What_lies_beyond_the_existing_part_is_not_respelled()
    {
        var respelled = Respell(Under("SHORT~1", "GONE~1.PNG"), existing: [Under("SHORT~1")]);

        await Assert.That(respelled).IsEqualTo(Under("ShortName", "GONE~1.PNG"));
    }

    /// <summary>Nothing of it exists, not even the root it names: there is nothing to ask.</summary>
    [Test]
    public async Task A_path_with_nothing_existing_under_it_comes_back_as_given()
    {
        var path = Under("SHORT~1", "art");

        await Assert.That(Respell(path, existing: [])).IsEqualTo(path);
    }

    private static string Respell(string path, string[] existing)
    {
        var there = existing.ToHashSet(StringComparer.Ordinal);
        return PathRespelling.Respell(
            path,
            candidate => there.Contains(candidate),
            candidate => candidate.Replace("SHORT~1", "ShortName", StringComparison.Ordinal)
        );
    }

    private static string Under(params string[] segments) => Path.Combine([Root, .. segments]);
}
