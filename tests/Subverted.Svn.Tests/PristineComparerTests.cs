using Subverted.Core;
using static Subverted.Svn.Tests.WcDbRowFactory;

namespace Subverted.Svn.Tests;

/// <summary>
/// Hits the real filesystem: the whole point of this type is hashing bytes on disk.
/// </summary>
public sealed class PristineComparerTests
{
    // SHA-1 of "hello", which is what each test writes unless it is testing a mismatch.
    private const string HelloSha1 = "aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d";

    [Test]
    public async Task A_file_matching_its_recorded_checksum_is_clean()
    {
        await WithFile(
            "hello",
            async path =>
            {
                var row = Row(checksum: $"$sha1${HelloSha1}");

                await Assert
                    .That(new PristineComparer().Compare(row, path))
                    .IsEqualTo(NodeStatus.Unmodified);
            }
        );
    }

    [Test]
    public async Task A_file_diverging_from_its_recorded_checksum_is_modified()
    {
        await WithFile(
            "hello, world",
            async path =>
            {
                var row = Row(checksum: $"$sha1${HelloSha1}");

                await Assert
                    .That(new PristineComparer().Compare(row, path))
                    .IsEqualTo(NodeStatus.Modified);
            }
        );
    }

    /// <summary>
    /// With svn:eol-style or svn:keywords in play the working file is a translation of the
    /// pristine, so hashing it proves nothing. Reporting Modified here would flag clean files.
    /// </summary>
    [Test]
    public async Task A_translated_node_stays_undecided_even_when_the_hash_differs()
    {
        await WithFile(
            "hello, world",
            async path =>
            {
                var row = Row(checksum: $"$sha1${HelloSha1}", isTranslated: true);

                await Assert
                    .That(new PristineComparer().Compare(row, path))
                    .IsEqualTo(NodeStatus.NeedsPristineCompare);
            }
        );
    }

    /// <summary>
    /// The inverse of the rule above, and the reason the property skel is parsed at all: a node
    /// that is untranslated must be settled by hash rather than parked as undecided.
    /// </summary>
    [Test]
    public async Task An_untranslated_node_is_settled_by_its_hash()
    {
        await WithFile(
            "hello, world",
            async path =>
            {
                var row = Row(checksum: $"$sha1${HelloSha1}", isTranslated: false);

                await Assert
                    .That(new PristineComparer().Compare(row, path))
                    .IsEqualTo(NodeStatus.Modified);
            }
        );
    }

    [Test]
    [Arguments(null)]
    [Arguments("$md5 $a6632de9c737a64c905a6e193f61ff6a")]
    public async Task An_unusable_checksum_stays_undecided(string? checksum)
    {
        await WithFile(
            "hello",
            async path =>
            {
                var row = Row(checksum: checksum);

                await Assert
                    .That(new PristineComparer().Compare(row, path))
                    .IsEqualTo(NodeStatus.NeedsPristineCompare);
            }
        );
    }

    [Test]
    public async Task An_unreadable_file_stays_undecided_rather_than_throwing()
    {
        await WithFile(
            "hello",
            async path =>
            {
                var row = Row(checksum: $"$sha1${HelloSha1}");
                var absent = path + ".does-not-exist";

                await Assert
                    .That(new PristineComparer().Compare(row, absent))
                    .IsEqualTo(NodeStatus.NeedsPristineCompare);
            }
        );
    }

    /// <summary>
    /// Opening a directory as a file throws <see cref="UnauthorizedAccessException"/> rather than
    /// an IOException, so it is a distinct escape route out of the hash.
    /// </summary>
    [Test]
    public async Task A_path_that_is_a_directory_stays_undecided_rather_than_throwing()
    {
        await WithFile(
            "hello",
            async path =>
            {
                var row = Row(checksum: $"$sha1${HelloSha1}");
                var directory = Path.GetDirectoryName(path)!;

                await Assert
                    .That(new PristineComparer().Compare(row, directory))
                    .IsEqualTo(NodeStatus.NeedsPristineCompare);
            }
        );
    }

    private static async Task WithFile(string content, Func<string, Task> body)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"subverted-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            var path = Path.Combine(temp, "node.dat");
            await File.WriteAllTextAsync(path, content);
            await body(path);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
