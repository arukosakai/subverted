using Subverted.Core;

namespace Subverted.Cli.Tests;

/// <summary>
/// Argument parsing is where a front-end quietly does the wrong thing — a flag read as a path, a
/// path read as a flag. All of it is pure, so all of it is pinned here rather than found later.
/// </summary>
public sealed class CommandLineTests
{
    private static readonly string Cwd = Path.GetFullPath("/wc/art");

    [Test]
    public async Task No_arguments_at_all_asks_for_help_rather_than_guessing()
    {
        await Assert.That(Command([])).IsEqualTo(new HelpCommand(null));
    }

    /// <summary>
    /// As <c>svn status</c> does with no target: the directory the person is standing in, not the
    /// whole working copy it happens to sit inside.
    /// </summary>
    [Test]
    [Arguments("st")]
    [Arguments("status")]
    public async Task Status_defaults_to_the_working_directory_and_to_neither_switch(string verb)
    {
        await AssertStatus(Command([verb]), [Cwd], includeUnmodified: false, includeIgnored: false);
    }

    [Test]
    public async Task A_relative_path_is_resolved_against_the_working_directory()
    {
        await AssertStatus(
            Command(["st", "characters"]),
            [Path.GetFullPath(Path.Combine(Cwd, "characters"))],
            includeUnmodified: false,
            includeIgnored: false
        );
    }

    [Test]
    public async Task An_absolute_path_is_taken_as_given()
    {
        var elsewhere = Path.GetFullPath("/other");

        await AssertStatus(
            Command(["st", elsewhere]),
            [elsewhere],
            includeUnmodified: false,
            includeIgnored: false
        );
    }

    /// <summary><c>svn status a b</c> lists the union, and so does this.</summary>
    [Test]
    public async Task Several_paths_are_all_kept_in_the_order_given()
    {
        await AssertStatus(
            Command(["st", "art", "-v", "readme.txt"]),
            [
                Path.GetFullPath(Path.Combine(Cwd, "art")),
                Path.GetFullPath(Path.Combine(Cwd, "readme.txt")),
            ],
            includeUnmodified: true,
            includeIgnored: false
        );
    }

    [Test]
    [Arguments("-v")]
    [Arguments("--verbose")]
    public async Task Verbose_asks_the_daemon_for_the_unmodified_nodes_too(string switchName)
    {
        await AssertStatus(
            Command(["st", switchName]),
            [Cwd],
            includeUnmodified: true,
            includeIgnored: false
        );
    }

    [Test]
    public async Task No_ignore_asks_for_the_ignored_nodes_and_nothing_else()
    {
        await AssertStatus(
            Command(["st", "--no-ignore"]),
            [Cwd],
            includeUnmodified: false,
            includeIgnored: true
        );
    }

    [Test]
    public async Task The_two_status_switches_are_independent()
    {
        await AssertStatus(
            Command(["st", "-v", "--no-ignore"]),
            [Cwd],
            includeUnmodified: true,
            includeIgnored: true
        );
    }

    [Test]
    public async Task An_unknown_status_option_is_not_treated_as_a_path()
    {
        var command = Command(["st", "--depth=infinity"]);

        await Assert.That(command).IsTypeOf<HelpCommand>();
        await Assert.That(((HelpCommand)command).Complaint).Contains("--depth=infinity");
    }

    /// <summary>
    /// The default cap is the one place <c>sv log</c> deliberately disagrees with <c>svn log</c>,
    /// so it is pinned rather than left to whatever the parser happens to do.
    /// </summary>
    [Test]
    public async Task Log_defaults_to_the_working_directory_and_to_the_newest_twenty()
    {
        await Assert.That(Command(["log"])).IsEqualTo(new LogCommand(Cwd, 20));
    }

    [Test]
    public async Task A_relative_log_path_is_resolved_against_the_working_directory()
    {
        await Assert
            .That(Command(["log", "characters"]))
            .IsEqualTo(new LogCommand(Path.GetFullPath(Path.Combine(Cwd, "characters")), 20));
    }

    [Test]
    [Arguments("-l")]
    [Arguments("--limit")]
    public async Task A_limit_replaces_the_default_under_either_spelling(string spelling)
    {
        await Assert.That(Command(["log", spelling, "5"])).IsEqualTo(new LogCommand(Cwd, 5));
    }

    [Test]
    public async Task All_asks_for_the_whole_history_by_asking_for_no_limit()
    {
        await Assert.That(Command(["log", "--all"])).IsEqualTo(new LogCommand(Cwd, null));
    }

    /// <summary>
    /// Both orders, because "the last one wins" and "the specific one wins" are different rules
    /// and only one of them is implemented.
    /// </summary>
    [Test]
    [Arguments(new[] { "log", "--all", "-l", "5" }, 5)]
    [Arguments(new[] { "log", "-l", "5", "--all" }, null)]
    public async Task The_last_of_the_two_limit_switches_is_the_one_that_counts(
        string[] arguments,
        int? expected
    )
    {
        await Assert.That(Command(arguments)).IsEqualTo(new LogCommand(Cwd, expected));
    }

    [Test]
    [Arguments("-l")]
    [Arguments("--limit")]
    public async Task A_limit_with_no_number_after_it_says_what_it_wanted(string spelling)
    {
        var command = Command(["log", spelling]);

        await Assert.That(command).IsTypeOf<HelpCommand>();
        await Assert.That(((HelpCommand)command).Complaint).Contains(spelling);
    }

    /// <summary>
    /// Zero and negative are refused rather than clamped: a limit of zero would come back as an
    /// empty history, which reads as "this file has none".
    /// </summary>
    [Test]
    [Arguments("0")]
    [Arguments("-3")]
    [Arguments("lots")]
    [Arguments("2.5")]
    public async Task A_limit_that_is_not_a_count_of_revisions_is_refused(string word)
    {
        var command = Command(["log", "-l", word]);

        await Assert.That(command).IsTypeOf<HelpCommand>();
        await Assert.That(((HelpCommand)command).Complaint).Contains(word);
    }

    [Test]
    public async Task A_number_that_is_a_count_of_revisions_is_taken()
    {
        await Assert.That(Command(["log", "-l", "1"])).IsEqualTo(new LogCommand(Cwd, 1));
    }

    [Test]
    public async Task An_unknown_log_option_is_not_treated_as_a_path()
    {
        var command = Command(["log", "--stop-on-copy"]);

        await Assert.That(command).IsTypeOf<HelpCommand>();
        await Assert.That(((HelpCommand)command).Complaint).Contains("--stop-on-copy");
    }

    [Test]
    public async Task Log_refuses_more_than_one_path()
    {
        await Assert.That(Command(["log", "one", "two"])).IsTypeOf<HelpCommand>();
    }

    [Test]
    [Arguments("d")]
    [Arguments("diff")]
    public async Task Diff_defaults_to_the_working_directory_under_either_spelling(string verb)
    {
        await Assert.That(Command([verb])).IsEqualTo(new DiffCommand(Cwd));
    }

    [Test]
    public async Task A_relative_diff_path_is_resolved_against_the_working_directory()
    {
        await Assert
            .That(Command(["d", "characters"]))
            .IsEqualTo(new DiffCommand(Path.GetFullPath(Path.Combine(Cwd, "characters"))));
    }

    [Test]
    public async Task An_unknown_diff_option_is_not_treated_as_a_path()
    {
        var command = Command(["d", "--summarize"]);

        await Assert.That(command).IsTypeOf<HelpCommand>();
        await Assert.That(((HelpCommand)command).Complaint).Contains("--summarize");
    }

    [Test]
    public async Task Diff_refuses_more_than_one_path()
    {
        await Assert.That(Command(["d", "one", "two"])).IsTypeOf<HelpCommand>();
    }

    [Test]
    [Arguments("status", typeof(DaemonStatusCommand))]
    [Arguments("stop", typeof(DaemonStopCommand))]
    public async Task The_daemon_subcommands_are_recognised(string word, Type expected)
    {
        await Assert.That(Command(["daemon", word]).GetType()).IsEqualTo(expected);
    }

    [Test]
    public async Task Daemon_on_its_own_says_what_it_wanted()
    {
        var command = Command(["daemon"]);

        await Assert.That(command).IsTypeOf<HelpCommand>();
        await Assert.That(((HelpCommand)command).Complaint).Contains("status");
    }

    [Test]
    public async Task An_unknown_daemon_subcommand_is_refused()
    {
        await Assert.That(Command(["daemon", "restart"])).IsTypeOf<HelpCommand>();
    }

    [Test]
    [Arguments("--version")]
    [Arguments("-V")]
    public async Task Version_has_both_of_its_spellings(string spelling)
    {
        await Assert.That(Command([spelling])).IsTypeOf<VersionCommand>();
    }

    /// <summary>Asked-for help has no complaint, so it goes to stdout and exits zero.</summary>
    [Test]
    [Arguments("help")]
    [Arguments("--help")]
    [Arguments("-h")]
    public async Task Help_asked_for_outright_carries_no_complaint(string spelling)
    {
        await Assert.That(Command([spelling])).IsEqualTo(new HelpCommand(null));
    }

    /// <summary>
    /// The lengths and near-misses are deliberate: a command table is a string switch, and a typo
    /// one character away from a real verb is the case that must not fall through to it.
    /// </summary>
    [Test]
    [Arguments("commit")]
    [Arguments("s")]
    [Arguments("sta")]
    [Arguments("statuses")]
    [Arguments("daemons")]
    [Arguments("--vers")]
    [Arguments("")]
    public async Task An_unknown_command_complains_by_name(string word)
    {
        var command = Command([word]);

        await Assert.That(command).IsTypeOf<HelpCommand>();
        await Assert.That(((HelpCommand)command).Complaint).Contains(word);
    }

    [Test]
    public async Task Colour_and_paging_are_on_and_timing_is_off_unless_asked_otherwise()
    {
        await Assert
            .That(CommandLine.Parse(["st"], Cwd).Output)
            .IsEqualTo(new OutputOptions(Colour: true, Pager: true, Timing: false));
    }

    [Test]
    [Arguments("--no-color", false, true, false)]
    [Arguments("--no-pager", true, false, false)]
    [Arguments("--timing", true, true, true)]
    public async Task Each_output_switch_changes_only_its_own(
        string switchName,
        bool colour,
        bool pager,
        bool timing
    )
    {
        await Assert
            .That(CommandLine.Parse(["st", switchName], Cwd).Output)
            .IsEqualTo(new OutputOptions(colour, pager, timing));
    }

    /// <summary>
    /// The output switches are stripped before the command is read, or <c>sv st --no-color</c>
    /// becomes a status request for a path named <c>--no-color</c>.
    /// </summary>
    [Test]
    public async Task An_output_switch_never_reaches_the_command_as_a_path()
    {
        var parsed = CommandLine.Parse(["st", "--no-color", "--no-pager", "--timing"], Cwd);

        await AssertStatus(parsed.Command, [Cwd], includeUnmodified: false, includeIgnored: false);
        await Assert
            .That(parsed.Output)
            .IsEqualTo(new OutputOptions(Colour: false, Pager: false, Timing: true));
    }

    [Test]
    public async Task An_output_switch_before_the_command_works_the_same_as_after_it()
    {
        var parsed = CommandLine.Parse(["--no-color", "st"], Cwd);

        await Assert.That(parsed.Command).IsTypeOf<StatusCommand>();
        await Assert.That(parsed.Output.Colour).IsFalse();
    }

    [Test]
    public async Task Add_takes_every_path_it_was_given_and_makes_them_absolute()
    {
        var command = (AddCommand)Command(["add", "hero.png", "../src/a.txt"]);

        await Assert
            .That(command.Paths)
            .IsEquivalentTo([Path.Combine(Cwd, "hero.png"), Path.GetFullPath("/wc/src/a.txt")]);
    }

    /// <summary>
    /// No default target. The obvious guess — the current directory — schedules a build output
    /// tree the first time somebody types it in the wrong folder.
    /// </summary>
    [Test]
    public async Task Add_with_no_path_asks_rather_than_defaulting_to_the_working_directory()
    {
        await Assert.That(Command(["add"])).IsTypeOf<HelpCommand>();
    }

    [Test]
    public async Task An_unknown_option_is_never_read_as_a_path_to_add()
    {
        await Assert.That(Command(["add", "--recursive", "a.txt"])).IsTypeOf<HelpCommand>();
    }

    [Test]
    public async Task Revert_takes_paths_and_asks_first_unless_told_otherwise()
    {
        var command = (RevertCommand)Command(["revert", "hero.png"]);

        await Assert.That(command.Paths).IsEquivalentTo([Path.Combine(Cwd, "hero.png")]);
        await Assert.That(command.AlreadyConfirmed).IsFalse();
    }

    [Test]
    [Arguments("-y")]
    [Arguments("--yes")]
    public async Task Revert_can_be_told_not_to_ask(string flag)
    {
        var command = (RevertCommand)Command(["revert", flag, "hero.png"]);

        await Assert.That(command.Paths).IsEquivalentTo([Path.Combine(Cwd, "hero.png")]);
        await Assert.That(command.AlreadyConfirmed).IsTrue();
    }

    /// <summary>
    /// The worst command to read an option as a path in: <c>sv revert</c> destroys work, and a
    /// misspelt <c>--yes</c> taken as a filename would revert something nobody named.
    /// </summary>
    [Test]
    public async Task An_unknown_option_is_never_read_as_a_path_to_revert()
    {
        var command = Command(["revert", "--force"]);

        await Assert.That(command).IsTypeOf<HelpCommand>();
        await Assert.That(((HelpCommand)command).Complaint).Contains("--force");
    }

    /// <summary>
    /// The accident this whole command is guarding against. Reverting the working directory
    /// because no path was given is exactly the thing that loses somebody's afternoon.
    /// </summary>
    [Test]
    public async Task Revert_with_no_path_refuses_rather_than_defaulting_to_the_working_directory()
    {
        await Assert.That(Command(["revert"])).IsTypeOf<HelpCommand>();
        await Assert.That(Command(["revert", "--yes"])).IsTypeOf<HelpCommand>();
    }

    [Test]
    [Arguments("rm")]
    [Arguments("remove")]
    [Arguments("delete")]
    public async Task Remove_takes_every_path_it_is_given(string verb)
    {
        var command = (RemoveCommand)Command([verb, "hero.png", "villain.png"]);

        await Assert
            .That(command.Paths)
            .IsEquivalentTo([Path.Combine(Cwd, "hero.png"), Path.Combine(Cwd, "villain.png")]);
        await Assert.That(command.AlreadyConfirmed).IsFalse();
    }

    [Test]
    [Arguments("-y")]
    [Arguments("--yes")]
    public async Task Remove_can_be_told_not_to_ask(string flag)
    {
        var command = (RemoveCommand)Command(["rm", flag, "hero.png"]);

        await Assert.That(command.Paths).IsEquivalentTo([Path.Combine(Cwd, "hero.png")]);
        await Assert.That(command.AlreadyConfirmed).IsTrue();
    }

    [Test]
    public async Task An_unknown_option_is_never_read_as_a_path_to_remove()
    {
        var command = Command(["rm", "--force"]);

        await Assert.That(command).IsTypeOf<HelpCommand>();
        await Assert.That(((HelpCommand)command).Complaint).Contains("--force");
    }

    /// <summary>
    /// Revert's accident one degree worse: defaulting to the working directory would take the whole
    /// project off disk as well as out of SVN.
    /// </summary>
    [Test]
    public async Task Remove_with_no_path_refuses_rather_than_defaulting_to_the_working_directory()
    {
        await Assert.That(Command(["rm"])).IsTypeOf<HelpCommand>();
        await Assert.That(Command(["rm", "--yes"])).IsTypeOf<HelpCommand>();
    }

    [Test]
    [Arguments("mv")]
    [Arguments("move")]
    [Arguments("rename")]
    public async Task Move_resolves_both_of_its_paths(string verb)
    {
        var command = (MoveCommand)Command([verb, "hero.png", "art/protagonist.png"]);

        await Assert.That(command.Source).IsEqualTo(Path.Combine(Cwd, "hero.png"));
        await Assert
            .That(command.Destination)
            .IsEqualTo(Path.GetFullPath(Path.Combine(Cwd, "art", "protagonist.png")));
    }

    /// <summary>
    /// Both sides of "exactly two". One path has no destination to mean, and three is
    /// <c>svn move</c>'s move-into-a-directory, which fails differently and is not this command.
    /// </summary>
    [Test]
    [Arguments("mv")]
    [Arguments("mv hero.png")]
    [Arguments("mv a.png b.png c")]
    public async Task Move_takes_exactly_two_paths(string commandLine)
    {
        await Assert.That(Command(commandLine.Split(' '))).IsTypeOf<HelpCommand>();
    }

    [Test]
    public async Task An_unknown_option_is_never_read_as_a_path_to_rename()
    {
        var command = Command(["mv", "--force", "hero.png"]);

        await Assert.That(command).IsTypeOf<HelpCommand>();
        await Assert.That(((HelpCommand)command).Complaint).Contains("--force");
    }

    [Test]
    [Arguments("ci")]
    [Arguments("commit")]
    public async Task Commit_defaults_to_the_working_directory_when_no_path_was_given(string verb)
    {
        var command = (CommitCommand)Command([verb, "-m", "re-export hero"]);

        await Assert.That(command.Paths).IsEquivalentTo([Cwd]);
        await Assert.That(command.Message).IsEqualTo("re-export hero");
    }

    [Test]
    public async Task Commit_sends_only_the_paths_it_was_given()
    {
        var command = (CommitCommand)Command([
            "commit",
            "hero.png",
            "hero.psd",
            "--message",
            "both",
        ]);

        await Assert
            .That(command.Paths)
            .IsEquivalentTo([Path.Combine(Cwd, "hero.png"), Path.Combine(Cwd, "hero.psd")]);
        await Assert.That(command.Message).IsEqualTo("both");
    }

    /// <summary>
    /// The daemon that runs the commit has no terminal to open an editor in, so the message has to
    /// arrive on the command line. Committing with an empty one would be worse than refusing.
    /// </summary>
    [Test]
    [Arguments(new[] { "commit" }, "no message flag at all")]
    [Arguments(new[] { "commit", "-m" }, "the flag with nothing after it")]
    [Arguments(new[] { "commit", "-m", "   " }, "whitespace only")]
    [Arguments(new[] { "commit", "-m", "" }, "empty")]
    public async Task Commit_without_a_usable_message_refuses(string[] arguments, string why)
    {
        await Assert.That(Command(arguments)).IsTypeOf<HelpCommand>().Because(why);
    }

    /// <summary>A log message beginning with a dash is a message, not an option.</summary>
    [Test]
    public async Task The_word_after_the_message_flag_is_taken_whatever_it_looks_like()
    {
        var command = (CommitCommand)Command(["commit", "-m", "--force is not a flag here"]);

        await Assert.That(command.Message).IsEqualTo("--force is not a flag here");
    }

    [Test]
    public async Task An_unknown_option_is_never_read_as_a_path_to_commit()
    {
        await Assert.That(Command(["commit", "-m", "x", "--force"])).IsTypeOf<HelpCommand>();
    }

    [Test]
    public async Task Pick_defaults_to_the_working_directory_when_no_path_was_given()
    {
        var command = (PickCommand)Command(["pick", "-m", "the good bits"]);

        await Assert.That(command.Path).IsEqualTo(Cwd);
        await Assert.That(command.Message).IsEqualTo("the good bits");
    }

    [Test]
    public async Task Pick_walks_the_path_it_was_given()
    {
        var command = (PickCommand)Command(["pick", "art", "--message", "hero only"]);

        await Assert.That(command.Path).IsEqualTo(Path.Combine(Cwd, "art"));
        await Assert.That(command.Message).IsEqualTo("hero only");
    }

    /// <summary>
    /// The walk is one ordered pass in which a directory has to come before its children, so two
    /// listings would have to be merged before it could start.
    /// </summary>
    [Test]
    public async Task Pick_takes_at_most_one_path()
    {
        await Assert.That(Command(["pick", "art", "src", "-m", "x"])).IsTypeOf<HelpCommand>();
    }

    [Test]
    [Arguments(new[] { "pick" }, "no message flag at all")]
    [Arguments(new[] { "pick", "-m" }, "the flag with nothing after it")]
    [Arguments(new[] { "pick", "-m", "   " }, "whitespace only")]
    public async Task Pick_without_a_usable_message_refuses(string[] arguments, string why)
    {
        await Assert.That(Command(arguments)).IsTypeOf<HelpCommand>().Because(why);
    }

    [Test]
    public async Task An_unknown_option_is_never_read_as_a_path_to_pick()
    {
        await Assert.That(Command(["pick", "-m", "x", "--force"])).IsTypeOf<HelpCommand>();
    }

    /// <summary>
    /// The one command that changes a working copy and still defaults to the current directory.
    /// <c>sv add</c> and <c>sv revert</c> refuse to guess because guessing wrong schedules or
    /// destroys something; an update brings work in and takes nothing away.
    /// </summary>
    [Test]
    [Arguments("up")]
    [Arguments("update")]
    public async Task Update_defaults_to_the_working_directory(string verb)
    {
        await Assert.That(Command([verb])).IsEqualTo(new UpdateCommand(Cwd));
    }

    [Test]
    public async Task Update_resolves_the_path_it_was_given()
    {
        await Assert
            .That(Command(["up", "characters"]))
            .IsEqualTo(new UpdateCommand(Path.GetFullPath(Path.Combine(Cwd, "characters"))));
    }

    /// <summary>
    /// SVN updates several targets independently and reports a revision for each, so two paths
    /// would leave "what revision is this now" with two answers and no way to pick.
    /// </summary>
    [Test]
    public async Task Update_takes_at_most_one_path()
    {
        await Assert.That(Command(["up", "src", "art"])).IsTypeOf<HelpCommand>();
    }

    [Test]
    public async Task An_unknown_option_is_never_read_as_a_path_to_update()
    {
        var command = Command(["up", "--force"]);

        await Assert.That(command).IsTypeOf<HelpCommand>();
        await Assert.That(((HelpCommand)command).Complaint).Contains("--force");
    }

    [Test]
    public async Task Lock_resolves_every_file_it_was_given_and_respects_other_holders()
    {
        var command = (LockCommand)Command(["lock", "hero.png", "villain.png"]);

        await Assert
            .That(command.Paths)
            .IsEquivalentTo([
                Path.GetFullPath(Path.Combine(Cwd, "hero.png")),
                Path.GetFullPath(Path.Combine(Cwd, "villain.png")),
            ]);
        await Assert.That(command.Comment).IsNull();
        await Assert.That(command.Foreign).IsEqualTo(ForeignLock.Respected);
    }

    [Test]
    [Arguments("-m")]
    [Arguments("--message")]
    public async Task A_lock_comment_is_taken_from_the_word_after_the_flag(string flag)
    {
        var command = (LockCommand)Command(["lock", flag, "retouching the hero", "hero.png"]);

        await Assert.That(command.Comment).IsEqualTo("retouching the hero");
        await Assert
            .That(command.Paths)
            .IsEquivalentTo([Path.GetFullPath(Path.Combine(Cwd, "hero.png"))]);
    }

    /// <summary>
    /// Unlike a commit message, a lock comment is optional — <c>svn lock</c> takes a lock without
    /// one. Requiring it here would be Subverted inventing a rule SVN does not have.
    /// </summary>
    [Test]
    public async Task A_lock_with_no_comment_is_a_lock_and_not_a_complaint()
    {
        await Assert.That(Command(["lock", "hero.png"])).IsTypeOf<LockCommand>();
    }

    [Test]
    public async Task A_message_flag_with_nothing_after_it_is_refused()
    {
        await Assert.That(Command(["lock", "hero.png", "-m"])).IsTypeOf<HelpCommand>();
    }

    [Test]
    public async Task Stealing_is_asked_for_by_name_and_is_never_the_default()
    {
        await Assert
            .That(((LockCommand)Command(["lock", "--steal", "hero.png"])).Foreign)
            .IsEqualTo(ForeignLock.Overridden);
        await Assert
            .That(((LockCommand)Command(["lock", "hero.png"])).Foreign)
            .IsEqualTo(ForeignLock.Respected);
    }

    /// <summary>
    /// The only default target available is the current directory, and a directory is the one thing
    /// SVN refuses to lock — so guessing would produce a command that can only fail.
    /// </summary>
    [Test]
    public async Task Lock_needs_at_least_one_file_rather_than_defaulting_to_the_directory()
    {
        await Assert.That(Command(["lock"])).IsTypeOf<HelpCommand>();
        await Assert.That(Command(["unlock"])).IsTypeOf<HelpCommand>();
    }

    [Test]
    public async Task Unlock_resolves_its_paths_and_leaves_other_holders_alone()
    {
        var command = (UnlockCommand)Command(["unlock", "hero.png"]);

        await Assert
            .That(command.Paths)
            .IsEquivalentTo([Path.GetFullPath(Path.Combine(Cwd, "hero.png"))]);
        await Assert.That(command.Foreign).IsEqualTo(ForeignLock.Respected);
    }

    [Test]
    public async Task Breaking_is_asked_for_by_name_and_is_never_the_default()
    {
        await Assert
            .That(((UnlockCommand)Command(["unlock", "--break", "hero.png"])).Foreign)
            .IsEqualTo(ForeignLock.Overridden);
    }

    /// <summary>
    /// The two words are not interchangeable: you steal a lock by taking it and break one by
    /// releasing it. Accepting either for both would let <c>sv unlock --steal</c> look like it did
    /// something it cannot do.
    /// </summary>
    [Test]
    [Arguments(new[] { "lock", "--break", "hero.png" }, "you steal a lock, you do not break it")]
    [Arguments(new[] { "unlock", "--steal", "hero.png" }, "and you break one rather than steal it")]
    [Arguments(new[] { "unlock", "-m", "why", "hero.png" }, "there is no comment on a release")]
    public async Task Each_command_takes_only_its_own_options(string[] arguments, string why)
    {
        await Assert.That(Command(arguments)).IsTypeOf<HelpCommand>().Because(why);
    }

    [Test]
    [Arguments("lock")]
    [Arguments("unlock")]
    public async Task An_unknown_option_is_never_read_as_a_path_to_lock(string verb)
    {
        var command = Command([verb, "--force"]);

        await Assert.That(command).IsTypeOf<HelpCommand>();
        await Assert.That(((HelpCommand)command).Complaint).Contains("--force");
    }

    [Test]
    [Arguments("--working", ConflictResolution.Working)]
    [Arguments("--mine", ConflictResolution.Mine)]
    [Arguments("--theirs", ConflictResolution.Theirs)]
    [Arguments("--base", ConflictResolution.Base)]
    public async Task Each_resolution_is_asked_for_by_its_own_word(
        string flag,
        ConflictResolution expected
    )
    {
        var command = (ResolveCommand)Command(["resolve", flag, "hero.png"]);

        await Assert.That(command.Resolution).IsEqualTo(expected);
        await Assert
            .That(command.Paths)
            .IsEquivalentTo([Path.GetFullPath(Path.Combine(Cwd, "hero.png"))]);
    }

    /// <summary>
    /// SVN's own default is to ask, and the daemon that runs this has no terminal to ask in — so
    /// there is no version to fall back to and guessing one picks a side of somebody's conflict.
    /// </summary>
    [Test]
    public async Task Resolve_without_a_version_is_refused_rather_than_defaulted()
    {
        var command = Command(["resolve", "hero.png"]);

        await Assert.That(command).IsTypeOf<HelpCommand>();
        await Assert.That(((HelpCommand)command).Complaint).Contains("--working");
    }

    /// <summary>
    /// Two different versions in one command line is a person who has not decided yet, and running
    /// either of them is worse than saying so.
    /// </summary>
    [Test]
    public async Task Two_different_versions_at_once_are_refused()
    {
        await Assert
            .That(Command(["resolve", "--mine", "--theirs", "hero.png"]))
            .IsTypeOf<HelpCommand>();
    }

    /// <summary>
    /// Saying the same one twice is not a contradiction, and refusing it would be a rule nobody
    /// asked for.
    /// </summary>
    [Test]
    public async Task The_same_version_twice_is_still_that_version()
    {
        var command = (ResolveCommand)Command(["resolve", "--mine", "--mine", "hero.png"]);

        await Assert.That(command.Resolution).IsEqualTo(ConflictResolution.Mine);
    }

    /// <summary>
    /// Unlike <c>sv lock</c>, a directory is a perfectly good resolve target — but defaulting to
    /// the current one would let a bare <c>sv resolve --theirs</c> overwrite the whole tree.
    /// </summary>
    [Test]
    public async Task Resolve_needs_at_least_one_path_rather_than_defaulting_to_the_directory()
    {
        await Assert.That(Command(["resolve", "--mine"])).IsTypeOf<HelpCommand>();
    }

    [Test]
    [Arguments("-y")]
    [Arguments("--yes")]
    public async Task Resolve_takes_a_confirmation_up_front(string flag)
    {
        var command = (ResolveCommand)Command(["resolve", "--theirs", flag, "hero.png"]);

        await Assert.That(command.AlreadyConfirmed).IsTrue();
    }

    [Test]
    public async Task Resolve_is_not_confirmed_unless_it_was_said()
    {
        var command = (ResolveCommand)Command(["resolve", "--theirs", "hero.png"]);

        await Assert.That(command.AlreadyConfirmed).IsFalse();
    }

    /// <summary>
    /// Which versions discard work nobody else has a copy of, and so have to ask first. Keeping
    /// mine drops only the incoming revision, which is still in the repository.
    /// </summary>
    [Test]
    [Arguments(ConflictResolution.Theirs, true)]
    [Arguments(ConflictResolution.Base, true)]
    [Arguments(ConflictResolution.Mine, false)]
    [Arguments(ConflictResolution.Working, false)]
    public async Task Only_the_versions_that_discard_local_work_ask_first(
        ConflictResolution resolution,
        bool expected
    )
    {
        var command = new ResolveCommand(["hero.png"], resolution, AlreadyConfirmed: false);

        await Assert.That(command.OverwritesLocalWork).IsEqualTo(expected);
    }

    [Test]
    public async Task An_unknown_option_is_never_read_as_a_path_to_resolve()
    {
        var command = Command(["resolve", "--mine", "--force"]);

        await Assert.That(command).IsTypeOf<HelpCommand>();
        await Assert.That(((HelpCommand)command).Complaint).Contains("--force");
    }

    /// <summary>
    /// The current directory is a safe default here and nowhere else: the path picks a working copy
    /// rather than a subtree, so the worst a wrong one can do is name a different checkout.
    /// </summary>
    [Test]
    public async Task Cleanup_with_no_path_cleans_the_working_copy_you_are_standing_in()
    {
        var command = (CleanupCommand)Command(["cleanup"]);

        await Assert.That(command.Path).IsEqualTo(Path.GetFullPath(Cwd));
    }

    [Test]
    public async Task Cleanup_resolves_the_path_it_was_given()
    {
        var command = (CleanupCommand)Command(["cleanup", "art"]);

        await Assert.That(command.Path).IsEqualTo(Path.GetFullPath("art", Cwd));
    }

    /// <summary>
    /// One working copy per run. Two paths would be two cleanups, and the report says what one of
    /// them released.
    /// </summary>
    [Test]
    public async Task Cleanup_takes_at_most_one_path()
    {
        await Assert.That(Command(["cleanup", "art", "src"])).IsTypeOf<HelpCommand>();
    }

    [Test]
    [Arguments("-y")]
    [Arguments("--yes")]
    public async Task Cleanup_takes_a_confirmation_up_front(string flag)
    {
        var command = (CleanupCommand)Command(["cleanup", flag]);

        await Assert.That(command.AlreadyConfirmed).IsTrue();
    }

    [Test]
    public async Task Cleanup_is_not_confirmed_unless_it_was_said()
    {
        var command = (CleanupCommand)Command(["cleanup"]);

        await Assert.That(command.AlreadyConfirmed).IsFalse();
    }

    /// <summary>
    /// The guard that stops an option being read as a path — which here would send the daemon off
    /// to find a working copy at a directory named <c>--force</c>.
    /// </summary>
    [Test]
    public async Task An_unknown_option_is_never_read_as_a_path_to_clean()
    {
        var command = Command(["cleanup", "--force"]);

        await Assert.That(command).IsTypeOf<HelpCommand>();
        await Assert.That(((HelpCommand)command).Complaint).Contains("--force");
    }

    private static CliCommand Command(string[] arguments) =>
        CommandLine.Parse(arguments, Cwd).Command;

    private static async Task AssertStatus(
        CliCommand command,
        string[] paths,
        bool includeUnmodified,
        bool includeIgnored
    )
    {
        await Assert.That(command).IsTypeOf<StatusCommand>();
        var status = (StatusCommand)command;
        await Assert.That(status.Paths).IsEquivalentTo(paths);
        await Assert.That(status.IncludeUnmodified).IsEqualTo(includeUnmodified);
        await Assert.That(status.IncludeIgnored).IsEqualTo(includeIgnored);
    }
}
