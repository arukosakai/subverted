using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Subverted.Svn;

/// <summary>
/// Runs the <c>svn</c> binary and reports what it said. The one place in Subverted that starts a
/// process, which is what keeps D6 — network operations shell out — down to a single file anyone
/// reviewing it can read.
/// </summary>
/// <param name="executable">The client to run. A bare <c>svn</c> resolves against PATH.</param>
public sealed class SvnCommand(string executable)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <param name="workingDirectory">An existing directory; SVN resolves relative paths against it.</param>
    /// <exception cref="SvnCommandException">The client could not be started at all.</exception>
    public async Task<SvnCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // svn translates its own output, so without this both the diff headers and every error
        // message would depend on the language the machine is installed in.
        startInfo.Environment["LC_ALL"] = "C";

        using var process = Start(startInfo);

        // Read both pipes before waiting: a command that fills one of them while nobody drains it
        // blocks forever, and `svn diff` on a large asset fills it easily.
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new SvnCommandResult(process.ExitCode, await standardOutput, await standardError);
    }

    private Process Start(ProcessStartInfo startInfo)
    {
        var process = new Process { StartInfo = startInfo };
        try
        {
            // The instance Start is used rather than the static one so there is no null to check:
            // the static overload returns null only when it reuses an already-running shell, which
            // UseShellExecute = false rules out, and a branch nothing can reach is one no test can
            // cover. What can really go wrong throws.
            process.Start();
            return process;
        }
        catch (Win32Exception exception)
        {
            process.Dispose();
            throw new SvnCommandException(
                $"Could not run '{executable}': {exception.Message}. "
                    + "Subverted shells out to the Subversion client for anything that talks to the server.",
                exception
            );
        }
    }
}
