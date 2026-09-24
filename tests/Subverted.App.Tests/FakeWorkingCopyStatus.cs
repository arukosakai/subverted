using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>Answers from a queue, then repeats the last answer; counts every question.</summary>
internal sealed class FakeWorkingCopyStatus : IWorkingCopyStatus
{
    private readonly Queue<Func<DaemonResponse>> _answers = new();
    private Func<DaemonResponse> _last = () => Entries.Listing();

    public int Reads { get; private set; }

    public List<string> Paths { get; } = [];

    /// <summary>Which nodes each question asked for, in order.</summary>
    public List<ListedNodes> Listings { get; } = [];

    /// <summary>The scan each question said it already held, in order.</summary>
    public List<Guid?> HeldScans { get; } = [];

    public FakeWorkingCopyStatus Answers(DaemonResponse response)
    {
        _answers.Enqueue(() => response);
        return this;
    }

    public FakeWorkingCopyStatus IsUnreachable(string message = "connection refused")
    {
        _answers.Enqueue(() =>
            throw new DaemonUnreachableException(message, new IOException(message))
        );
        return this;
    }

    public Task<DaemonResponse> ReadAsync(
        string workingCopyPath,
        ListedNodes listed,
        Guid? heldScan,
        CancellationToken cancellationToken
    )
    {
        Reads++;
        Paths.Add(workingCopyPath);
        Listings.Add(listed);
        HeldScans.Add(heldScan);
        if (_answers.TryDequeue(out var next))
        {
            _last = next;
        }

        return Task.FromResult(_last());
    }
}
