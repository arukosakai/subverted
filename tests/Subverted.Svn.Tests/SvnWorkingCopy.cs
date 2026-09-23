using System.Diagnostics;
using TUnit.Core.Exceptions;

namespace Subverted.Svn.Tests;

/// <summary>
/// A throwaway repository and checkout, built by driving the real <c>svn</c> client.
/// </summary>
/// <remarks>
/// Deliberately not a hand-built wc.db. The schema is Subversion's private business, and a fixture
/// we assemble ourselves can only confirm that our SQL matches our own idea of it —
/// <c>Present_directory_is_unmodified</c> was green for exactly that reason while every directory
/// in a real checkout reported Missing.
/// </remarks>
internal sealed class SvnWorkingCopy : IDisposable
{
    private static readonly Lazy<bool> Available = new(() =>
        TryRun(Path.GetTempPath(), "svn", "--version", "--quiet")
    );

    private readonly string _basePath;

    public string Root { get; }

    /// <summary>
    /// The repository behind this checkout. Exposed so a test can take it away — an update that
    /// cannot reach the server is the only way <c>svn update</c> fails outright.
    /// </summary>
    public string RepositoryPath { get; }

    private SvnWorkingCopy(string basePath, string root, string repositoryPath)
    {
        _basePath = basePath;
        Root = root;
        RepositoryPath = repositoryPath;
    }

    /// <exception cref="SkipTestException">
    /// The <c>svn</c> client is not on PATH. These tests assert against Subversion's real
    /// behaviour, so without it there is nothing to assert against and pretending otherwise would
    /// be worse than skipping.
    /// </exception>
    public static SvnWorkingCopy Create()
    {
        if (!Available.Value)
        {
            throw new SkipTestException("The svn command-line client is not on PATH.");
        }

        var basePath = Path.Combine(Path.GetTempPath(), $"subverted-it-{Guid.NewGuid():N}");
        var repository = Path.Combine(basePath, "repo");
        var root = Path.Combine(basePath, "wc");
        Directory.CreateDirectory(basePath);

        var copy = new SvnWorkingCopy(basePath, root, repository);
        try
        {
            Run(basePath, "svnadmin", "create", repository);
            Run(basePath, "svn", "checkout", "--quiet", ToFileUrl(repository), root);
            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A second checkout of the same repository — a teammate's working copy. Anything about
    /// <c>svn update</c> needs two: one to commit from, and one for the update to bring work into.
    /// </summary>
    /// <remarks>
    /// It lives inside this fixture's directory and owns only itself, so disposing either one
    /// cleans up and disposing both is not an error.
    /// </remarks>
    public SvnWorkingCopy AnotherCheckout()
    {
        var root = Path.Combine(_basePath, $"wc-{Guid.NewGuid():N}"[..12]);
        Run(_basePath, "svn", "checkout", "--quiet", ToFileUrl(RepositoryPath), root);
        return new SvnWorkingCopy(root, root, RepositoryPath);
    }

    /// <summary>Runs <c>svn</c> in the working copy, throwing if it reports failure.</summary>
    public void Svn(params string[] arguments) => Run(Root, "svn", arguments);

    public void Write(string relPath, string content)
    {
        var path = Absolute(relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // svn:needs-lock leaves the working file read-only, and rewriting it is the whole point of
        // several of these fixtures.
        if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        File.WriteAllText(path, content);
    }

    public void Delete(string relPath) => File.Delete(Absolute(relPath));

    public void CreateDirectory(string relPath) => Directory.CreateDirectory(Absolute(relPath));

    public string Absolute(string relPath) =>
        Path.Combine(Root, relPath.Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
        {
            DeleteTree(_basePath);
        }
    }

    private static void Run(string workingDirectory, string executable, params string[] arguments)
    {
        if (!TryRun(workingDirectory, executable, arguments, out var output))
        {
            throw new InvalidOperationException(
                $"{executable} {string.Join(' ', arguments)} failed: {output}"
            );
        }
    }

    private static bool TryRun(
        string workingDirectory,
        string executable,
        params string[] arguments
    ) => TryRun(workingDirectory, executable, arguments, out _);

    private static bool TryRun(
        string workingDirectory,
        string executable,
        string[] arguments,
        out string output
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

        try
        {
            using var process =
                Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Could not start {executable}.");

            output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            output = exception.Message;
            return false;
        }
    }

    private static string ToFileUrl(string path) =>
        "file:///" + Path.GetFullPath(path).Replace('\\', '/').TrimStart('/');

    /// <summary>
    /// Everything under <c>.svn/pristine</c> is read-only, so a plain recursive delete fails and
    /// would leave a repository behind on every run.
    /// </summary>
    private static void DeleteTree(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }
}
