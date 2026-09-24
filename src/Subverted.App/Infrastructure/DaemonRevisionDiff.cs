using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.App.Infrastructure;

/// <summary>Asks the daemon what one revision did to one repository path.</summary>
public sealed class DaemonRevisionDiff(DaemonChannel channel) : IRevisionDiff
{
    public Task<DaemonResponse> ReadAsync(
        string workingCopyPath,
        string repositoryPath,
        long revision,
        DiffContext? context,
        CancellationToken cancellationToken
    ) =>
        DaemonQuestion.AskAsync(
            channel,
            new RevisionDiffRequest(workingCopyPath, repositoryPath, revision, context),
            cancellationToken
        );
}
