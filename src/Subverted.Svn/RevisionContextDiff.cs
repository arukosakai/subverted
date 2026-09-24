using System.Globalization;
using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// What one revision did to one file, with any amount of context: both sides read with
/// <c>svn cat</c> and compared in-process. Only a plain text edit is answered; everything else is
/// declined, and the caller asks <see cref="SvnRevisionDiffCommand"/> instead.
/// </summary>
/// <remarks>
/// Four runs of svn where <c>svn diff -c</c> is one: the summary says whether the revision only
/// edited the file's text, the property list whether its text is comparable, and two cats give the
/// sides. Both are pegged at the revision, so history is followed as <c>svn diff -c</c> follows it.
/// </remarks>
/// <param name="newline">What ends SVN's own lines here — the platform's newline.</param>
public sealed class RevisionContextDiff(SvnCommand command, string newline)
{
    /// <param name="repositoryRoot">The working copy's repository root URL, as wc.db records it.</param>
    /// <param name="repositoryPath">Repository-absolute, as <c>svn log</c> prints a changed path.</param>
    /// <returns>
    /// The diff; <see langword="null"/> when <c>svn diff -c</c> has to answer: an add, delete, copy or
    /// property change, a folder, text that is not comparable (<see cref="ComparableText"/>), a
    /// side whose line endings are mixed, a change too large to search, or any svn run failing.
    /// </returns>
    /// <exception cref="SvnCommandException">The client could not be started at all.</exception>
    public async Task<string?> ReadAsync(
        string workingCopyRoot,
        string repositoryRoot,
        string repositoryPath,
        long revision,
        DiffContext context,
        CancellationToken cancellationToken
    )
    {
        if (revision < 2)
        {
            return null;
        }

        var target = SvnTarget.InRepository(repositoryRoot, repositoryPath, revision);
        var number = revision.ToString(CultureInfo.InvariantCulture);
        var before = (revision - 1).ToString(CultureInfo.InvariantCulture);

        var summary = await command.RunAsync(
            workingCopyRoot,
            ["diff", "--non-interactive", "--summarize", "--xml", "--change", number, target],
            cancellationToken
        );
        if (
            summary.ExitCode != 0
            || SvnDiffSummary.Changes(summary.StandardOutput) is not [{ IsTextEditOfAFile: true }]
        )
        {
            return null;
        }

        var properties = command.RunAsync(
            workingCopyRoot,
            ["proplist", "--non-interactive", "--verbose", "--xml", "--revision", number, target],
            cancellationToken
        );
        var oldSide = command.RunForBytesAsync(
            workingCopyRoot,
            ["cat", "--non-interactive", "--revision", before, target],
            cancellationToken
        );
        var newSide = command.RunForBytesAsync(
            workingCopyRoot,
            ["cat", "--non-interactive", "--revision", number, target],
            cancellationToken
        );
        await Task.WhenAll(properties, oldSide, newSide);

        if (
            (await properties) is not { ExitCode: 0 } listed
            || SvnPropertyListXml.Parse(listed.StandardOutput) is not { } parsed
            || ComparableText.LineEndingsOf(parsed) is not { } lineEndings
            || (await oldSide) is not (0, var oldCat, _)
            || (await newSide) is not (0, var newCat, _)
            || LineEndingNormalForm.Of(oldCat, lineEndings) is not { } oldText
            || LineEndingNormalForm.Of(newCat, lineEndings) is not { } newText
        )
        {
            return null;
        }

        var name = repositoryPath[(repositoryPath.LastIndexOf('/') + 1)..];
        var header = new DiffSectionHeader(name, $"revision {before}", $"revision {number}");
        var diff = SvnStyleDiff.Write(header, oldText, newText, context.LinesAround, newline);
        return diff is null
            ? null
            : SvnOutputText.Decode(diff, command.Spelling.LinesThatAreNotUtf8);
    }
}
