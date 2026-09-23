using System.Diagnostics;
using Subverted.App.ViewModels;

namespace Subverted.App.Infrastructure;

/// <summary>Reveals by starting a file manager's command line; see <see cref="RevealCommand"/>.</summary>
/// <param name="commandFor">The command for a path that exists; told whether it is a folder.</param>
public sealed class ProcessFileRevealer(Func<string, bool, RevealCommand> commandFor)
    : IFileRevealer
{
    public Task RevealAsync(string path)
    {
        if (NearestPresentPath.Of(path) is not { } shown)
        {
            return Task.CompletedTask;
        }

        var command = commandFor(shown, Directory.Exists(shown));
        var start = new ProcessStartInfo(command.FileName) { UseShellExecute = false };
        foreach (var argument in command.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // Nothing waits on the file manager; it is the person's window from here on.
        Process.Start(start)?.Dispose();
        return Task.CompletedTask;
    }
}
