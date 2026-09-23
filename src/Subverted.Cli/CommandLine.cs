using System.Globalization;
using Subverted.Core;

namespace Subverted.Cli;

/// <summary>
/// Turns argv into a command. Pure, and takes the working directory rather than reading it, so
/// every rule below is a test rather than something you find out by running <c>sv</c> in anger.
/// </summary>
public static class CommandLine
{
    /// <summary>
    /// History is a server round trip, so it is capped unless the user says otherwise. An
    /// uncapped <c>svn log</c> on a studio repository is thousands of revisions and a long wait
    /// for the twenty anybody reads.
    /// </summary>
    public const int DefaultRevisionLimit = 20;

    /// <param name="workingDirectory">What a bare or relative path is resolved against.</param>
    public static ParsedCommandLine Parse(IReadOnlyList<string> arguments, string workingDirectory)
    {
        var output = new OutputOptions(
            Colour: !arguments.Contains("--no-color"),
            Pager: !arguments.Contains("--no-pager"),
            Timing: arguments.Contains("--timing")
        );
        var words = arguments
            .Where(argument => argument is not ("--no-color" or "--no-pager" or "--timing"))
            .ToList();

        return new ParsedCommandLine(Command(words, workingDirectory), output);
    }

    private static CliCommand Command(List<string> words, string workingDirectory) =>
        words.Count == 0
            ? new HelpCommand(null)
            : words[0] switch
            {
                "st" or "status" => Status(words[1..], workingDirectory),
                "log" => Log(words[1..], workingDirectory),
                "d" or "diff" => Diff(words[1..], workingDirectory),
                "up" or "update" => Update(words[1..], workingDirectory),
                "add" => Add(words[1..], workingDirectory),
                "revert" => Revert(words[1..], workingDirectory),
                "rm" or "remove" or "delete" => Remove(words[1..], workingDirectory),
                "mv" or "move" or "rename" => Move(words[1..], workingDirectory),
                "ci" or "commit" => Commit(words[1..], workingDirectory),
                "lock" => Lock(words[1..], workingDirectory),
                "unlock" => Unlock(words[1..], workingDirectory),
                "resolve" => Resolve(words[1..], workingDirectory),
                "cleanup" => Cleanup(words[1..], workingDirectory),
                "pick" => Pick(words[1..], workingDirectory),
                "daemon" => Daemon(words[1..]),
                "--version" or "-V" => new VersionCommand(),
                "help" or "--help" or "-h" => new HelpCommand(null),
                var unknown => new HelpCommand($"Unknown command '{unknown}'."),
            };

    private static CliCommand Status(List<string> words, string workingDirectory)
    {
        var paths = new List<string>();
        var includeUnmodified = false;
        var includeIgnored = false;

        foreach (var word in words)
        {
            switch (word)
            {
                case "-v" or "--verbose":
                    includeUnmodified = true;
                    break;
                case "--no-ignore":
                    includeIgnored = true;
                    break;
                default:
                    if (word.StartsWith('-'))
                    {
                        return new HelpCommand($"Unknown option '{word}' for 'sv st'.");
                    }

                    paths.Add(word);
                    break;
            }
        }

        return new StatusCommand(
            ResolveAll(paths, workingDirectory),
            includeUnmodified,
            includeIgnored
        );
    }

    private static CliCommand Log(List<string> words, string workingDirectory)
    {
        var paths = new List<string>();
        int? limit = DefaultRevisionLimit;

        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            switch (word)
            {
                case "--all":
                    limit = null;
                    break;

                case "-l"
                or "--limit":
                    if (index + 1 == words.Count)
                    {
                        return new HelpCommand($"'{word}' needs a number of revisions after it.");
                    }

                    index++;
                    if (!Revisions(words[index], out var wanted))
                    {
                        return new HelpCommand(
                            $"'{words[index]}' is not a number of revisions to show."
                        );
                    }

                    limit = wanted;
                    break;

                default:
                    if (word.StartsWith('-'))
                    {
                        return new HelpCommand($"Unknown option '{word}' for 'sv log'.");
                    }

                    paths.Add(word);
                    break;
            }
        }

        return paths.Count > 1
            ? new HelpCommand("'sv log' takes at most one path.")
            : new LogCommand(ResolveOne(paths, workingDirectory), limit);
    }

    private static CliCommand Diff(List<string> words, string workingDirectory)
    {
        if (words.FirstOrDefault(word => word.StartsWith('-')) is { } unknown)
        {
            return new HelpCommand($"Unknown option '{unknown}' for 'sv d'.");
        }

        return words.Count > 1
            ? new HelpCommand("'sv d' takes at most one path.")
            : new DiffCommand(ResolveOne(words, workingDirectory));
    }

    private static CliCommand Update(List<string> words, string workingDirectory)
    {
        if (words.FirstOrDefault(word => word.StartsWith('-')) is { } unknown)
        {
            return new HelpCommand($"Unknown option '{unknown}' for 'sv up'.");
        }

        // One target, because SVN updates several of them independently and reports a revision for
        // each. Two paths would leave "what revision is this now" with two answers.
        return words.Count > 1
            ? new HelpCommand("'sv up' takes at most one path.")
            : new UpdateCommand(ResolveOne(words, workingDirectory));
    }

    private static CliCommand Add(List<string> words, string workingDirectory)
    {
        if (words.FirstOrDefault(word => word.StartsWith('-')) is { } unknown)
        {
            return new HelpCommand($"Unknown option '{unknown}' for 'sv add'.");
        }

        // No default target on purpose. `svn add` with none is an error, and the obvious guess —
        // the current directory — would schedule a build output tree the first time someone typed
        // it in the wrong folder.
        return words.Count == 0
            ? new HelpCommand("'sv add' needs at least one path.")
            : new AddCommand(ResolveAll(words, workingDirectory));
    }

    private static CliCommand Revert(List<string> words, string workingDirectory)
    {
        var paths = new List<string>();
        var alreadyConfirmed = false;

        foreach (var word in words)
        {
            switch (word)
            {
                case "-y" or "--yes":
                    alreadyConfirmed = true;
                    break;
                default:
                    if (word.StartsWith('-'))
                    {
                        return new HelpCommand($"Unknown option '{word}' for 'sv revert'.");
                    }

                    paths.Add(word);
                    break;
            }
        }

        // The one command with no default target at all. Reverting the current directory is
        // exactly the accident this is guarding against.
        return paths.Count == 0
            ? new HelpCommand("'sv revert' needs at least one path.")
            : new RevertCommand(ResolveAll(paths, workingDirectory), alreadyConfirmed);
    }

    private static CliCommand Remove(List<string> words, string workingDirectory)
    {
        var paths = new List<string>();
        var alreadyConfirmed = false;

        foreach (var word in words)
        {
            switch (word)
            {
                case "-y" or "--yes":
                    alreadyConfirmed = true;
                    break;
                default:
                    if (word.StartsWith('-'))
                    {
                        return new HelpCommand($"Unknown option '{word}' for 'sv rm'.");
                    }

                    paths.Add(word);
                    break;
            }
        }

        // No default target, for revert's reason one degree worse: the current directory is the
        // project, and this one takes the files off disk as well as out of SVN.
        return paths.Count == 0
            ? new HelpCommand("'sv rm' needs at least one path.")
            : new RemoveCommand(ResolveAll(paths, workingDirectory), alreadyConfirmed);
    }

    private static CliCommand Move(List<string> words, string workingDirectory)
    {
        if (words.FirstOrDefault(word => word.StartsWith('-')) is { } unknown)
        {
            return new HelpCommand($"Unknown option '{unknown}' for 'sv mv'.");
        }

        // Exactly two, and no default for either. `svn move` also takes several sources into one
        // directory; that is a different operation with a different failure mode, and it is not
        // what anyone types when they mean "rename this".
        return words.Count != 2
            ? new HelpCommand("'sv mv' takes exactly two paths: the old name and the new one.")
            : new MoveCommand(
                Path.GetFullPath(words[0], workingDirectory),
                Path.GetFullPath(words[1], workingDirectory)
            );
    }

    private static CliCommand Commit(List<string> words, string workingDirectory)
    {
        var paths = new List<string>();
        string? message = null;
        var marking = false;

        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            switch (word)
            {
                case "-m" or "--message":
                    if (index + 1 == words.Count)
                    {
                        return new HelpCommand($"'{word}' needs a log message after it.");
                    }

                    message = words[++index];
                    break;

                case "--mark":
                    marking = true;
                    break;

                default:
                    if (word.StartsWith('-'))
                    {
                        return new HelpCommand($"Unknown option '{word}' for 'sv commit'.");
                    }

                    paths.Add(word);
                    break;
            }
        }

        // An editor is what SVN would open, and the daemon that runs the commit has no terminal to
        // open one in. Asking for -m is honest; pretending to have an editor would not be.
        if (message?.Trim() is not { Length: > 0 })
        {
            return new HelpCommand("'sv commit' needs a log message: -m \"what changed\".");
        }

        var resolved = ResolveAll(paths, workingDirectory);
        return marking
            ? new MarkingCommitCommand(resolved, message)
            : new CommitCommand(resolved, message);
    }

    private static CliCommand Lock(List<string> words, string workingDirectory)
    {
        var paths = new List<string>();
        string? comment = null;
        var foreign = ForeignLock.Respected;

        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            switch (word)
            {
                case "--steal":
                    foreign = ForeignLock.Overridden;
                    break;

                case "-m"
                or "--message":
                    if (index + 1 == words.Count)
                    {
                        return new HelpCommand($"'{word}' needs a lock comment after it.");
                    }

                    comment = words[++index];
                    break;

                default:
                    if (word.StartsWith('-'))
                    {
                        return new HelpCommand($"Unknown option '{word}' for 'sv lock'.");
                    }

                    paths.Add(word);
                    break;
            }
        }

        // No default target. The only one available is the current directory, and a directory is
        // precisely what SVN refuses to lock.
        return paths.Count == 0
            ? new HelpCommand("'sv lock' needs at least one file to lock.")
            : new LockCommand(ResolveAll(paths, workingDirectory), comment, foreign);
    }

    private static CliCommand Unlock(List<string> words, string workingDirectory)
    {
        var paths = new List<string>();
        var foreign = ForeignLock.Respected;

        foreach (var word in words)
        {
            switch (word)
            {
                case "--break":
                    foreign = ForeignLock.Overridden;
                    break;
                default:
                    if (word.StartsWith('-'))
                    {
                        return new HelpCommand($"Unknown option '{word}' for 'sv unlock'.");
                    }

                    paths.Add(word);
                    break;
            }
        }

        return paths.Count == 0
            ? new HelpCommand("'sv unlock' needs at least one file to unlock.")
            : new UnlockCommand(ResolveAll(paths, workingDirectory), foreign);
    }

    private static CliCommand Resolve(List<string> words, string workingDirectory)
    {
        var paths = new List<string>();
        var alreadyConfirmed = false;
        ConflictResolution? resolution = null;

        foreach (var word in words)
        {
            switch (word)
            {
                case "-y" or "--yes":
                    alreadyConfirmed = true;
                    break;

                default:
                    if (Chosen(word) is { } chosen)
                    {
                        if (resolution is { } already && already != chosen)
                        {
                            return new HelpCommand(
                                "'sv resolve' keeps one version: "
                                    + "--working, --mine, --theirs or --base."
                            );
                        }

                        resolution = chosen;
                    }
                    else if (word.StartsWith('-'))
                    {
                        return new HelpCommand($"Unknown option '{word}' for 'sv resolve'.");
                    }
                    else
                    {
                        paths.Add(word);
                    }

                    break;
            }
        }

        // No default version. SVN's own default is to ask, and there is no terminal in the daemon
        // that runs this — so the choice is made here, in words, or not at all.
        if (resolution is not { } keep)
        {
            return new HelpCommand(
                "'sv resolve' needs to know which version to keep: "
                    + "--working, --mine, --theirs or --base."
            );
        }

        return paths.Count == 0
            ? new HelpCommand("'sv resolve' needs at least one path.")
            : new ResolveCommand(ResolveAll(paths, workingDirectory), keep, alreadyConfirmed);
    }

    /// <returns>
    /// The version that word asks to keep, or null when it is not one of them. The four names live
    /// here and nowhere else, so a fifth cannot be recognised in one place and dropped in another.
    /// </returns>
    private static ConflictResolution? Chosen(string word) =>
        word switch
        {
            "--working" => ConflictResolution.Working,
            "--mine" => ConflictResolution.Mine,
            "--theirs" => ConflictResolution.Theirs,
            "--base" => ConflictResolution.Base,
            _ => null,
        };

    private static CliCommand Cleanup(List<string> words, string workingDirectory)
    {
        var paths = new List<string>();
        var alreadyConfirmed = false;

        foreach (var word in words)
        {
            switch (word)
            {
                case "-y" or "--yes":
                    alreadyConfirmed = true;
                    break;
                default:
                    if (word.StartsWith('-'))
                    {
                        return new HelpCommand($"Unknown option '{word}' for 'sv cleanup'.");
                    }

                    paths.Add(word);
                    break;
            }
        }

        // The current directory is the right default here and nowhere else: the path picks a working
        // copy rather than a subtree, so the worst a wrong one can do is name a different checkout.
        return paths.Count > 1
            ? new HelpCommand("'sv cleanup' takes at most one path.")
            : new CleanupCommand(ResolveOne(paths, workingDirectory), alreadyConfirmed);
    }

    private static CliCommand Pick(List<string> words, string workingDirectory)
    {
        var paths = new List<string>();
        string? message = null;

        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            switch (word)
            {
                case "-m" or "--message":
                    if (index + 1 == words.Count)
                    {
                        return new HelpCommand($"'{word}' needs a log message after it.");
                    }

                    message = words[++index];
                    break;

                default:
                    if (word.StartsWith('-'))
                    {
                        return new HelpCommand($"Unknown option '{word}' for 'sv pick'.");
                    }

                    paths.Add(word);
                    break;
            }
        }

        // One target, because the walk is a single ordered pass and ancestors have to come before
        // their children in it. Two targets would need the two listings merged first.
        if (paths.Count > 1)
        {
            return new HelpCommand("'sv pick' takes at most one path.");
        }

        return message?.Trim() is not { Length: > 0 }
            ? new HelpCommand("'sv pick' needs a log message: -m \"what changed\".")
            : new PickCommand(ResolveOne(paths, workingDirectory), message);
    }

    /// <summary>Zero revisions is not a shorter listing, it is a listing nobody wants.</summary>
    private static bool Revisions(string word, out int count) =>
        int.TryParse(word, CultureInfo.InvariantCulture, out count) && count > 0;

    private static string ResolveOne(List<string> paths, string workingDirectory) =>
        Path.GetFullPath(paths.Count == 0 ? workingDirectory : paths[0], workingDirectory);

    /// <summary>
    /// Every path the user gave, made absolute; the working directory when they gave none. The
    /// commands that write take a list, because committing three of nine changed files is the
    /// thing people do most days.
    /// </summary>
    private static IReadOnlyList<string> ResolveAll(List<string> paths, string workingDirectory) =>
        paths.Count == 0
            ? [Path.GetFullPath(workingDirectory)]
            : [.. paths.Select(path => Path.GetFullPath(path, workingDirectory))];

    private static CliCommand Daemon(List<string> words) =>
        words.Count == 0
            ? new HelpCommand("'sv daemon' needs 'status' or 'stop'.")
            : words[0] switch
            {
                "status" => new DaemonStatusCommand(),
                "stop" => new DaemonStopCommand(),
                var unknown => new HelpCommand($"Unknown 'sv daemon' subcommand '{unknown}'."),
            };
}
