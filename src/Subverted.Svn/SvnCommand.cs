using System.ComponentModel;
using System.Diagnostics;

namespace Subverted.Svn;

/// <summary>
/// Runs the <c>svn</c> binary and reports what it said. The one place in Subverted that starts a
/// process, which is what keeps D6 — network operations shell out — down to a single file anyone
/// reviewing it can read.
/// </summary>
/// <param name="executable">The client to run. A bare <c>svn</c> resolves against PATH.</param>
/// <param name="spelling">How that client spells paths in its text; see D34.</param>
public sealed class SvnCommand(string executable, ISvnTextSpelling spelling)
{
    public SvnCommand(string executable)
        : this(executable, SvnTextSpelling.ForThisMachine()) { }

    public ISvnTextSpelling Spelling => spelling;

    /// <param name="workingDirectory">An existing directory; SVN resolves relative paths against it.</param>
    /// <exception cref="SvnCommandException">The client could not be started at all.</exception>
    public async Task<SvnCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        var (exitCode, standardOutput, standardError) = await RunForBytesAsync(
            workingDirectory,
            arguments,
            cancellationToken
        );

        return new SvnCommandResult(
            exitCode,
            SvnOutputText.Decode(standardOutput, spelling.LinesThatAreNotUtf8),
            SvnOutputText.Decode(standardError, spelling.LinesThatAreNotUtf8)
        );
    }

    /// <summary>
    /// As <see cref="RunAsync"/>, with standard output left as the bytes svn wrote — for
    /// <c>svn cat</c>, whose output is a file rather than text.
    /// </summary>
    /// <exception cref="SvnCommandException">The client could not be started at all.</exception>
    public async Task<(int ExitCode, byte[] StandardOutput, byte[] StandardError)> RunForBytesAsync(
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
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["LC_ALL"] = SvnLocale.LcAllFor(
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS()
        );

        using var process = Start(startInfo);

        // Read both pipes before waiting: a command that fills one of them while nobody drains it
        // blocks forever, and `svn diff` on a large asset fills it easily.
        var standardOutput = ReadAllAsync(process.StandardOutput.BaseStream, cancellationToken);
        var standardError = ReadAllAsync(process.StandardError.BaseStream, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, await standardOutput, await standardError);
    }

    private static async Task<byte[]> ReadAllAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes, cancellationToken);
        return bytes.ToArray();
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
