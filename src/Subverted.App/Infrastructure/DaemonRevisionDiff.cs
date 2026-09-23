using Subverted.App.ViewModels;
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
        CancellationToken cancellationToken
    ) =>
        DaemonQuestion.AskAsync(
            channel,
            new RevisionDiffRequest(workingCopyPath, repositoryPath, revision),
            cancellationToken
        );
}
