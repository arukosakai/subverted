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
        CancellationToken cancellationToken
    )
    {
        Reads++;
        Paths.Add(workingCopyPath);
        if (_answers.TryDequeue(out var next))
        {
            _last = next;
        }

        return Task.FromResult(_last());
    }
}
