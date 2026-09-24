using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using Subverted.Cli;
using Subverted.Core;
using Subverted.Frontend;
using Subverted.Protocol;

var startedAt = Stopwatch.GetTimestamp();
var parsed = CommandLine.Parse(args, Directory.GetCurrentDirectory());
var channel = new DaemonChannel(
    DaemonSocketPath.FromEnvironment(),
    DaemonChannel.ExecutableNextTo(AppContext.BaseDirectory)
);

try
{
    return parsed.Command switch
    {
        StatusCommand status => await ShowStatusAsync(status, parsed.Output),
        LogCommand log => await ShowLogAsync(log, parsed.Output),
        DiffCommand diff => await ShowDiffAsync(diff, parsed.Output),
        UpdateCommand update => await UpdateAsync(update, parsed.Output),
        AddCommand add => await AddAsync(add, parsed.Output),
        RevertCommand revert => await RevertAsync(revert, parsed.Output),
        RemoveCommand remove => await RemoveAsync(remove, parsed.Output),
        MoveCommand move => await MoveAsync(move, parsed.Output),
        CommitCommand commit => await CommitAsync(commit, parsed.Output),
        MarkingCommitCommand marking => await MarkingCommitAsync(marking, parsed.Output),
        LockCommand take => await LockAsync(take, parsed.Output),
        UnlockCommand release => await UnlockAsync(release, parsed.Output),
        ResolveCommand resolve => await ResolveAsync(resolve, parsed.Output),
        CleanupCommand cleanup => await CleanupAsync(cleanup, parsed.Output),
        PickCommand pick => await PickAsync(pick, parsed.Output),
        DaemonStatusCommand => await ShowDaemonStatusAsync(),
        DaemonStopCommand => await StopDaemonAsync(),
        VersionCommand => ShowVersion(),
        HelpCommand help => ShowHelp(help),
        var unhandled => ShowHelp(
            new HelpCommand($"Unhandled command {unhandled.GetType().Name}.")
        ),
    };
}
catch (Exception ex)
    when (ex
            is SocketException
                or ProtocolException
                or IOException
                or TimeoutException
                or UnauthorizedAccessException
    )
{
    Console.Error.WriteLine($"sv: {ex.Message}");
    return ExitCode.DaemonFailure;
}

async Task<int> ShowStatusAsync(StatusCommand command, OutputOptions output)
{
    var response = await channel.SendAsync(
        new StatusRequest(
            command.Paths[0],
            command.IncludeUnmodified,
            command.IncludeIgnored,
            Scope: command.Paths
        ),
        CancellationToken.None
    );

    switch (response)
    {
        case StatusResponse status:
            var paint = PaintFor(output);
            var lines = command.IncludeUnmodified
                ? StatusReport.Verbose(status, paint)
                : StatusReport.Compact(status, paint);

            if (output.Timing)
            {
                lines =
                [
                    .. lines,
                    TimingLine.For(status, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds),
                ];
            }

            Emit(lines, output);
            return ExitCode.Success;

        case ErrorResponse error:
            return Complain(error);

        default:
            return Unexpected(response);
    }
}

async Task<int> ShowLogAsync(LogCommand command, OutputOptions output)
{
    var response = await channel.SendAsync(
        new LogRequest(command.Path, command.Limit),
        CancellationToken.None
    );

    switch (response)
    {
        case LogResponse log:
            Emit(
                LogReport.Lines(
                    log,
                    command.Limit,
                    TimeZoneInfo.Local,
                    Colouring(output) ? LogPalette.Ansi : LogPalette.Plain
                ),
                output
            );
            return ExitCode.Success;

        case ErrorResponse error:
            return Complain(error);

        default:
            return Unexpected(response);
    }
}

async Task<int> ShowDiffAsync(DiffCommand command, OutputOptions output)
{
    var response = await channel.SendAsync(new DiffRequest(command.Path), CancellationToken.None);

    switch (response)
    {
        case DiffResponse diff:
            // Redirected, this is a patch somebody may apply, so SVN's bytes go through untouched
            // — line endings included. Splitting into lines and printing them would re-terminate
            // every content line with the console's newline, and the patch would no longer match
            // the file it came from. On a terminal it is something to read, so it is coloured.
            if (Console.IsOutputRedirected)
            {
                Console.Out.Write(diff.UnifiedDiff);
                return ExitCode.Success;
            }

            Emit(
                DiffReport.Lines(diff, Colouring(output) ? DiffPalette.Ansi : DiffPalette.Plain),
                output
            );
            return ExitCode.Success;

        case ErrorResponse error:
            return Complain(error);

        default:
            return Unexpected(response);
    }
}

/// <summary>
/// Brings the rest of the studio's work in. The one command here that can finish successfully and
/// still leave the working copy needing a person, which is why its exit code says so.
/// </summary>
async Task<int> UpdateAsync(UpdateCommand command, OutputOptions output)
{
    var response = await channel.SendAsync(new UpdateRequest(command.Path), CancellationToken.None);

    switch (response)
    {
        case UpdateResponse updated:
            Emit(UpdateReport.Lines(updated), output);
            return updated.Conflicts + updated.SkippedPaths > 0
                ? ExitCode.NeedsAttention
                : ExitCode.Success;

        case ErrorResponse error:
            return Complain(error);

        default:
            return Unexpected(response);
    }
}

/// <summary>
/// Takes the lock that stops two people editing one binary asset. The second command whose exit
/// code deliberately differs from <c>svn</c>'s: <c>svn lock</c> warns and exits zero when it will
/// not give you one, and <c>sv lock &amp;&amp; open-the-file</c> is how that becomes a lost evening.
/// </summary>
async Task<int> LockAsync(LockCommand command, OutputOptions output)
{
    var response = await channel.SendAsync(
        new LockRequest(command.Paths, command.Comment, command.Foreign),
        CancellationToken.None
    );

    switch (response)
    {
        case LockResponse locked:
            Emit(LockReport.Locked(locked), output);
            return LockAttention.IsNeeded(locked) ? ExitCode.NeedsAttention : ExitCode.Success;

        case ErrorResponse error:
            return Complain(error);

        default:
            return Unexpected(response);
    }
}

/// <summary>
/// Gives the lock back. A refusal here is not work left undone — SVN drops the local token either
/// way — but it means the lock had already gone, which is how somebody learns theirs was stolen.
/// </summary>
async Task<int> UnlockAsync(UnlockCommand command, OutputOptions output)
{
    var response = await channel.SendAsync(
        new UnlockRequest(command.Paths, command.Foreign),
        CancellationToken.None
    );

    switch (response)
    {
        case UnlockResponse unlocked:
            Emit(LockReport.Unlocked(unlocked), output);
            return LockAttention.IsNeeded(unlocked) ? ExitCode.NeedsAttention : ExitCode.Success;

        case ErrorResponse error:
            return Complain(error);

        default:
            return Unexpected(response);
    }
}

/// <summary>
/// Finishes what <c>sv up</c> starts. Exits <see cref="ExitCode.NeedsAttention"/> for a node SVN
/// would not resolve — a tree conflict asked to become anything but the working version is the one
/// a person actually hits, and it leaves the working copy exactly as conflicted as it was.
/// </summary>
async Task<int> ResolveAsync(ResolveCommand command, OutputOptions output)
{
    if (
        command.OverwritesLocalWork
        && !command.AlreadyConfirmed
        && await ConfirmResolveAsync(command, output) is { } instead
    )
    {
        return instead;
    }

    var response = await channel.SendAsync(
        new ResolveRequest(command.Paths, command.Resolution),
        CancellationToken.None
    );

    switch (response)
    {
        case ResolveResponse resolved:
            Emit(ResolveReport.Lines(resolved, command.Resolution), output);
            return resolved.Refusals.Count > 0 ? ExitCode.NeedsAttention : ExitCode.Success;

        case ErrorResponse error:
            return Complain(error);

        default:
            return Unexpected(response);
    }
}

/// <returns>
/// <c>null</c> to go ahead, or the exit code to return instead. Declining is
/// <see cref="ExitCode.Success"/>: nothing was overwritten, which is what the person asked for.
/// </returns>
async Task<int?> ConfirmResolveAsync(ResolveCommand command, OutputOptions output)
{
    var response = await channel.SendAsync(
        new StatusRequest(command.Paths[0], IncludeUnmodified: false, IncludeIgnored: false),
        CancellationToken.None
    );

    if (response is ErrorResponse error)
    {
        return Complain(error);
    }

    if (response is not StatusResponse status)
    {
        return Unexpected(response);
    }

    var losing = ConflictPreview.Lines(status, command.Paths, PathComparison());
    if (losing.Count == 0)
    {
        Console.WriteLine("nothing was conflicted");
        return ExitCode.Success;
    }

    Emit(losing, output);

    // Same rule as revert, and for the same reason: a pipe cannot answer, and defaulting to yes is
    // how a script overwrites an afternoon nobody has committed yet.
    if (Console.IsInputRedirected)
    {
        Console.Error.WriteLine(
            $"sv: {losing.Count} node(s) above would be overwritten. "
                + "Re-run with --yes to confirm; there is no terminal here to ask in."
        );
        return ExitCode.UserError;
    }

    Console.Write($"overwrite {losing.Count} node(s) and lose those changes? [y/N] ");
    if (Confirmation.IsYes(Console.ReadLine()))
    {
        return null;
    }

    Console.WriteLine("nothing resolved");
    return ExitCode.Success;
}

/// <summary>
/// Unwedges a working copy a crashed client left stuck. The one command whose whole report is
/// computed rather than printed by <c>svn</c>: cleanup says nothing at all, so without this a person
/// cannot tell it worked from it having had nothing to do.
/// </summary>
async Task<int> CleanupAsync(CleanupCommand command, OutputOptions output)
{
    if (!command.AlreadyConfirmed && ConfirmCleanup() is { } instead)
    {
        return instead;
    }

    var response = await channel.SendAsync(
        new CleanupRequest(command.Path),
        CancellationToken.None
    );

    switch (response)
    {
        case CleanupResponse cleaned:
            Emit(CleanupReport.Lines(cleaned), output);
            return cleaned.RemainingWriteLocks.Count > 0
                ? ExitCode.NeedsAttention
                : ExitCode.Success;

        case ErrorResponse error:
            return Complain(error);

        default:
            return Unexpected(response);
    }
}

/// <returns>
/// <c>null</c> to go ahead, or the exit code to return instead. Declining is
/// <see cref="ExitCode.Success"/>: nothing was touched, which is what the person asked for.
/// </returns>
/// <remarks>
/// There is nothing to preview here, unlike revert and resolve — a working copy this is aimed at may
/// be one <c>svn status</c> itself refuses to read. What is being confirmed is SVN's own warning:
/// cleanup cannot tell a lock left by a dead client from one a live client is still using.
/// </remarks>
int? ConfirmCleanup()
{
    Console.WriteLine(
        "Cleanup breaks any working-copy lock it finds. If another SVN client — "
            + "TortoiseSVN, an IDE, another `svn` — is using this working copy right now, "
            + "that can corrupt it beyond repair."
    );

    if (Console.IsInputRedirected)
    {
        Console.Error.WriteLine(
            "sv: re-run with --yes to confirm; there is no terminal here to ask in."
        );
        return ExitCode.UserError;
    }

    Console.Write("no other client is using this working copy? [y/N] ");
    if (Confirmation.IsYes(Console.ReadLine()))
    {
        return null;
    }

    Console.WriteLine("nothing cleaned");
    return ExitCode.Success;
}

async Task<int> AddAsync(AddCommand command, OutputOptions output)
{
    var response = await channel.SendAsync(new AddRequest(command.Paths), CancellationToken.None);

    switch (response)
    {
        case AddResponse added:
            Emit(NotificationLines.OrElse(added.Notifications, "nothing to add"), output);
            return ExitCode.Success;

        case ErrorResponse error:
            return Complain(error);

        default:
            return Unexpected(response);
    }
}

/// <summary>
/// The only command in <c>sv</c> that destroys work nobody else has a copy of, so it shows what it
/// is about to take and asks — unless <c>--yes</c> already answered.
/// </summary>
async Task<int> RevertAsync(RevertCommand command, OutputOptions output)
{
    if (!command.AlreadyConfirmed && await ConfirmRevertAsync(command, output) is { } instead)
    {
        return instead;
    }

    var response = await channel.SendAsync(
        new RevertRequest(command.Paths),
        CancellationToken.None
    );

    switch (response)
    {
        case RevertResponse reverted:
            Emit(NotificationLines.OrElse(reverted.Notifications, "nothing reverted"), output);
            return ExitCode.Success;

        case ErrorResponse error:
            return Complain(error);

        default:
            return Unexpected(response);
    }
}

/// <returns>
/// <c>null</c> to go ahead, or the exit code to return instead. Declining is
/// <see cref="ExitCode.Success"/>: nothing was destroyed, which is what the person asked for.
/// </returns>
async Task<int?> ConfirmRevertAsync(RevertCommand command, OutputOptions output)
{
    var response = await channel.SendAsync(
        new StatusRequest(command.Paths[0], IncludeUnmodified: false, IncludeIgnored: false),
        CancellationToken.None
    );

    if (response is ErrorResponse error)
    {
        return Complain(error);
    }

    if (response is not StatusResponse status)
    {
        return Unexpected(response);
    }

    var losing = RevertPreview.Lines(status, command.Paths, PathComparison());
    if (losing.Count == 0)
    {
        Console.WriteLine("nothing to revert");
        return ExitCode.Success;
    }

    Emit(losing, output);

    // A pipe cannot answer, and defaulting to yes for one is how a script reverts a studio's
    // afternoon. The flag is the way to mean it from a script.
    if (Console.IsInputRedirected)
    {
        Console.Error.WriteLine(
            $"sv: {losing.Count} node(s) above would be reverted. "
                + "Re-run with --yes to confirm; there is no terminal here to ask in."
        );
        return ExitCode.UserError;
    }

    Console.Write($"revert {losing.Count} node(s) and lose those changes? [y/N] ");
    if (Confirmation.IsYes(Console.ReadLine()))
    {
        return null;
    }

    Console.WriteLine("nothing reverted");
    return ExitCode.Success;
}

/// <summary>
/// Takes files out of SVN and off disk. Shows what would go and asks — and counts the ones with no
/// pristine behind them separately, because those are the only removals nothing can undo.
/// </summary>
async Task<int> RemoveAsync(RemoveCommand command, OutputOptions output)
{
    var (instead, preview) = await ConfirmRemovalAsync(command, output);
    if (instead is { } exitCode)
    {
        return exitCode;
    }

    var response = await channel.SendAsync(
        new DeleteRequest(command.Paths),
        CancellationToken.None
    );

    switch (response)
    {
        case DeleteResponse removed:
            Emit(RemovalReport.Lines(removed, preview!.Unrecoverable.Count), output);
            return ExitCode.Success;

        case ErrorResponse error:
            return Complain(error);

        default:
            return Unexpected(response);
    }
}

/// <returns>
/// Either the exit code to return instead of removing anything, or the preview that was confirmed —
/// never both. Declining is <see cref="ExitCode.Success"/>: nothing was removed, which is what the
/// person asked for.
/// </returns>
async Task<(int? Instead, RemovalPreview? Confirmed)> ConfirmRemovalAsync(
    RemoveCommand command,
    OutputOptions output
)
{
    var response = await channel.SendAsync(
        RemovalPreview.ListingFor(command.Paths[0]),
        CancellationToken.None
    );

    if (response is ErrorResponse error)
    {
        return (Complain(error), null);
    }

    if (response is not StatusResponse status)
    {
        return (Unexpected(response), null);
    }

    var preview = RemovalPreview.Of(status, command.Paths, PathComparison());
    if (preview.Count == 0)
    {
        Console.WriteLine("nothing to remove");
        return (ExitCode.Success, null);
    }

    if (command.AlreadyConfirmed)
    {
        return (null, preview);
    }

    Emit(preview.Lines, output);

    // Revert's rule, for the same reason: a pipe cannot answer, and defaulting to yes is how a
    // script takes a studio's assets off disk.
    if (Console.IsInputRedirected)
    {
        Console.Error.WriteLine(
            $"sv: {preview.Count} node(s) above would be removed. "
                + "Re-run with --yes to confirm; there is no terminal here to ask in."
        );
        return (ExitCode.UserError, null);
    }

    Console.Write($"remove {preview.Count} node(s)? [y/N] ");
    if (Confirmation.IsYes(Console.ReadLine()))
    {
        return (null, preview);
    }

    Console.WriteLine("nothing removed");
    return (ExitCode.Success, null);
}

/// <summary>
/// Renames a node and keeps its history — including when the rename already happened in a file
/// manager, which is the case <c>svn move</c> cannot do anything with at all.
/// </summary>
async Task<int> MoveAsync(MoveCommand command, OutputOptions output)
{
    // Read before the move: afterwards the source is a path scheduled for deletion, and whether it
    // was holding a lock is no longer something worth asking it.
    var sourceHeldLock = await HeldLockAsync(command.Source);

    var response = await channel.SendAsync(
        new MoveRequest(command.Source, command.Destination),
        CancellationToken.None
    );

    switch (response)
    {
        case MoveResponse moved:
            Emit(MoveReport.Lines(moved, sourceHeldLock), output);
            return ExitCode.Success;

        case ErrorResponse error:
            return Complain(error);

        default:
            return Unexpected(response);
    }
}

/// <summary>
/// Whether this working copy holds a repository lock on a path. False for anything that cannot be
/// read — a status this could not fetch is not a reason to refuse a rename, only a reason not to
/// claim anything about locks afterwards.
/// </summary>
async Task<bool> HeldLockAsync(string path)
{
    var response = await channel.SendAsync(
        new StatusRequest(path, IncludeUnmodified: false, IncludeIgnored: false),
        CancellationToken.None
    );

    return response is StatusResponse status
        && AffectedNodes.Under(status, [path], PathComparison(), entry => entry.HasLockToken).Count
            > 0;
}

async Task<int> CommitAsync(CommitCommand command, OutputOptions output)
{
    var response = await channel.SendAsync(command.Request, CancellationToken.None);

    return ReportCommit(response, output);
}

/// <summary>
/// <c>sv commit --mark</c>: everything changed under the paths, marked on the way. The listing is
/// read here only to name the nodes — what each one needs is the daemon's decision, from its own status.
/// </summary>
async Task<int> MarkingCommitAsync(MarkingCommitCommand command, OutputOptions output)
{
    var response = await channel.SendAsync(
        new StatusRequest(
            command.Paths[0],
            IncludeUnmodified: false,
            IncludeIgnored: false,
            Scope: command.Paths
        ),
        CancellationToken.None
    );

    if (response is ErrorResponse statusError)
    {
        return Complain(statusError);
    }

    if (response is not StatusResponse status)
    {
        return Unexpected(response);
    }

    var targets = MarkingCommitTargets.Under(status, command.Paths, PathComparison());
    if (targets.Count == 0)
    {
        Console.WriteLine("nothing to commit");
        return ExitCode.Success;
    }

    return ReportSelectionCommit(
        await channel.SendAsync(
            new CommitSelectionRequest(
                [.. targets.Select(relPath => Absolute(status.Info.RootPath, relPath))],
                command.Message
            ),
            CancellationToken.None
        ),
        output
    );
}

/// <summary>
/// Walks the changed nodes one at a time and commits the ones that were said yes to — `git add -p`'s
/// interaction with a whole file per prompt, which is the unit an artist thinks in.
/// </summary>
async Task<int> PickAsync(PickCommand command, OutputOptions output)
{
    // Nine questions cannot be answered by a pipe, and a default answer to any of them either sends
    // work nobody approved or silently sends nothing. Naming the paths is the scriptable way.
    if (Console.IsInputRedirected)
    {
        Console.Error.WriteLine(
            "sv: 'sv pick' asks about every change and there is no terminal here to ask in. "
                + "Use `sv commit PATH... -m \"what changed\"` from a script."
        );
        return ExitCode.UserError;
    }

    var response = await channel.SendAsync(
        new StatusRequest(command.Path, IncludeUnmodified: false, IncludeIgnored: false),
        CancellationToken.None
    );

    if (response is ErrorResponse statusError)
    {
        return Complain(statusError);
    }

    if (response is not StatusResponse status)
    {
        return Unexpected(response);
    }

    var candidates = PickCandidates.Under(status, command.Path, PathComparison());
    if (candidates.Count == 0)
    {
        Console.WriteLine("nothing to pick");
        return ExitCode.Success;
    }

    var paint = PaintFor(output);
    var picker = new ChangePicker(candidates, PathComparison());
    PickConversation.Walk(picker, paint, new ConsolePrompt(), candidates.Count);

    if (picker.Abandoned)
    {
        Console.WriteLine("nothing committed");
        return ExitCode.Success;
    }

    if (picker.Picked.Count == 0)
    {
        Console.WriteLine("nothing picked, nothing committed");
        return ExitCode.Success;
    }

    Emit(PickReport.Sending(picker, paint), output);

    return ReportSelectionCommit(
        await channel.SendAsync(
            new CommitSelectionRequest(
                [
                    .. picker
                        .Picked.SelectMany(candidate => candidate.RelPaths)
                        .Select(relPath => Absolute(status.Info.RootPath, relPath)),
                ],
                command.Message
            ),
            CancellationToken.None
        ),
        output
    );
}

/// <summary>An entry's slash-separated relative path, back as a path this platform's `svn` accepts.</summary>
string Absolute(string rootPath, string relPath) =>
    Path.Combine(rootPath, relPath.Replace('/', Path.DirectorySeparatorChar));

/// <summary>
/// A selection that stopped part-way exits <see cref="ExitCode.SvnFailure"/> like any failed
/// commit, and still prints what it marked — those marks are in the working copy now.
/// </summary>
int ReportSelectionCommit(DaemonResponse response, OutputOptions output)
{
    switch (response)
    {
        case CommitSelectionResponse committed:
            Emit(SelectionReport.Committed(committed), output);
            return ExitCode.Success;

        case SelectionNotCommittedResponse stopped:
            Console.Error.WriteLine($"sv: {SelectionReport.Failure(stopped)}");
            Emit(SelectionReport.LeftInPlace(stopped), output);
            return ExitCode.SvnFailure;

        case ErrorResponse error:
            return Complain(error);

        default:
            return Unexpected(response);
    }
}

int ReportCommit(DaemonResponse response, OutputOptions output)
{
    switch (response)
    {
        case CommitResponse committed:
            Emit(CommitReport.Lines(committed), output);
            return ExitCode.Success;

        case ErrorResponse error:
            return Complain(error);

        default:
            return Unexpected(response);
    }
}

async Task<int> ShowDaemonStatusAsync()
{
    var response = await channel.SendIfRunningAsync(
        new DaemonInfoRequest(),
        CancellationToken.None
    );

    switch (response)
    {
        case null:
            Console.WriteLine("no daemon running");
            return ExitCode.Success;

        case DaemonInfoResponse info:
            ConsolePager.Write(Console.Out, DaemonInfoReport.Lines(info));
            return ExitCode.Success;

        case ErrorResponse error:
            return Complain(error);

        default:
            return Unexpected(response);
    }
}

/// <summary>Stopping a daemon that is already gone is what the user asked for, not a failure.</summary>
async Task<int> StopDaemonAsync()
{
    var response = await channel.SendIfRunningAsync(new ShutdownRequest(), CancellationToken.None);

    switch (response)
    {
        case null:
            Console.WriteLine("no daemon running");
            return ExitCode.Success;

        case AcknowledgedResponse:
            Console.WriteLine("daemon stopping");
            return ExitCode.Success;

        case ErrorResponse error:
            return Complain(error);

        default:
            return Unexpected(response);
    }
}

int ShowVersion()
{
    Console.WriteLine(
        Assembly
            .GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? "0.0.0"
    );
    return ExitCode.Success;
}

int ShowHelp(HelpCommand help)
{
    if (help.Complaint is null)
    {
        ConsolePager.Write(Console.Out, HelpText.Lines);
        return ExitCode.Success;
    }

    Console.Error.WriteLine($"sv: {help.Complaint}");
    ConsolePager.Write(Console.Error, HelpText.Lines);
    return ExitCode.UserError;
}

int Complain(ErrorResponse error)
{
    Console.Error.WriteLine($"sv: {error.Message}");
    return error.Kind switch
    {
        DaemonErrorKind.NotAWorkingCopy or DaemonErrorKind.RequestRefused => ExitCode.UserError,
        DaemonErrorKind.SvnCommandFailed => ExitCode.SvnFailure,
        _ => ExitCode.DaemonFailure,
    };
}

int Unexpected(DaemonResponse response)
{
    Console.Error.WriteLine(
        $"sv: the daemon answered with {response.GetType().Name}, which this build does not expect. "
            + "Run `sv daemon stop` and try again."
    );
    return ExitCode.DaemonFailure;
}

/// <summary>
/// The user's switch, then the terminal's. NO_COLOR is honoured because a studio's build logs go
/// through tooling that has no idea what an escape sequence is.
/// </summary>
bool Colouring(OutputOptions output) =>
    output.Colour
    && !Console.IsOutputRedirected
    && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

/// <summary>
/// Windows compares paths without case and everything else compares them with it. Getting this
/// backwards makes a revert preview list nothing on a path the user spelled differently.
/// </summary>
StringComparison PathComparison() =>
    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

Paint PaintFor(OutputOptions output) =>
    Colouring(output) ? StatusPalette.Ansi : StatusPalette.Plain;

void Emit(IReadOnlyList<string> lines, OutputOptions output)
{
    if (output.Pager && !Console.IsOutputRedirected && !Console.IsInputRedirected)
    {
        var height = Console.WindowHeight;
        if (height > 1 && lines.Count > height)
        {
            ConsolePager.Page(Console.Out, lines, height, PromptForMore);
            return;
        }
    }

    ConsolePager.Write(Console.Out, lines);
}

static bool PromptForMore()
{
    const string Prompt = "-- more (space to continue, q to quit) --";
    Console.Write(Prompt);
    var key = Console.ReadKey(intercept: true);
    Console.Write($"\r{new string(' ', Prompt.Length)}\r");
    return key.Key is not (ConsoleKey.Q or ConsoleKey.Escape);
}
