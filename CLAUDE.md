# Subverted — working agreement

A cross-platform SVN client for a small game studio. Read `docs/PLAN.md` for why this exists and
`docs/ARCHITECTURE.md` for how the pieces fit. This file is how we write the code.

These rules are strict on purpose. This tool touches people's working copies — the place their
unpushed work lives. A bug here does not fail a build, it loses a day of someone's art.

## Commands

```bash
dotnet tool restore                                   # once, after clone
dotnet build Subverted.slnx
tests/<Name>/bin/Debug/net10.0/<Name>.exe             # 3290 across seven, all must pass; needs `svn`
bash tools/run-tests.sh                               # all seven in one go — what CI runs
dotnet test                                           # often reports zero — read the note below
dotnet test --coverage --coverage-output-format cobertura
dotnet csharpier format .                             # before every commit
dotnet run --project tools/Subverted.Probe -- <path>  # dump status for a working copy
tools/compare-with-svn.sh <path>                      # diff the probe against `svn status`

# `sv`, the app and the daemon land in one directory — both front-ends start it from beside themselves.
dotnet publish src/Subverted.Daemon -c Release -o artifacts/bin
dotnet publish src/Subverted.Cli    -c Release -o artifacts/bin
dotnet publish src/Subverted.App    -c Release -o artifacts/bin   # Subverted.exe, the M2 GUI
artifacts/bin/sv st <path> --timing                   # --timing splits daemon time from startup
artifacts/bin/sv daemon status                        # what it holds, and whether it is watching

# Performance. A working copy answers very differently depending on whether SVN's recorded mtimes
# still match disk, so a number is meaningless without saying which state it was taken in.
tools/measure-100k.ps1 <path>                         # cold / warm / `svn status`, three runs each
tools/move-mtimes.ps1 <path>                          # → State B: nothing settles without hashing
tools/restore-mtimes.py <path>                        # → State A: back to fresh-checkout timings
tools/make-asset-fixture.ps1                          # a tree of MB-sized binaries, not 29-byte ones
dotnet run tools/measure-pristine-compare.cs -- <wc>  # hash vs pristine compare, cache evicted
```

A timing taken on 29-byte files measures per-file overhead and says nothing about throughput; the
two answer differently and both are real. Say which fixture a number came from.

A tree that fits in RAM is a third state nobody names, and it silently doubles some numbers. Every
figure taken before D18 was cached. If a measurement reads files, say whether the cache was dropped
— `measure-pristine-compare.cs` does it with `FILE_FLAG_NO_BUFFERING` and proves it worked by
reporting a read-only pass alongside, which is the pattern to copy rather than trusting the flag.

The run that always works is a test project's own executable —
`tests/<Name>/bin/Debug/net10.0/<Name>.exe`. There are seven, and together they are the suite.

**`dotnet test` reports "zero tests ran" (exit 5) for all four projects, intermittently, and the
cause is still unknown.** It happened on 2026-09-21, appeared to clear, then came back the same
day in a session that changed no C# at all — so the suite is ruled out as the cause. It does not
recover from a clean, a `dotnet build-server shutdown` or a wiped `TestResults`, and `--solution`
makes no difference.

What the second occurrence pinned down, which narrows it usefully:

- `--list-tests` also returns zero, in ~300 ms. It is not that tests run and fail to report —
  **discovery itself comes back empty** under the orchestrator.
- The same assembly run *directly* is green both ways: `<Name>.exe`, and
  `dotnet <Name>.dll` through the shared host. So the apphost, the assembly and
  Microsoft.Testing.Platform are all fine, and the fault is in the MSBuild orchestration layer.

So: **when `dotnet test` says zero, do not believe it and do not chase it — run the seven
executables.** That is the run that settles it either way, and it is the number to quote.

`dotnet test --project <one>` silently runs zero tests under this SDK, always, not intermittently.

`dotnet test` needs the `test.runner` opt-in in `global.json` — .NET 10 removed the VSTest path and
TUnit is Microsoft.Testing.Platform only. Don't remove it.

## Architecture constraints

The dependency rules in `docs/ARCHITECTURE.md` are not suggestions. In particular:

- `Subverted.Core` depends on nothing. A package reference there means the design went wrong.
- `Subverted.Svn` is the **only** project that knows SVN exists. Nothing else references
  `Microsoft.Data.Sqlite`, shells out to `svn`, or knows what `op_depth` means.
- Front-ends (`Cli`, `App`) talk to the daemon through `Frontend` and `Protocol`. They never
  reference `Subverted.Svn`. Two implementations of status is how TortoiseSVN got slow.

Adding a project reference that crosses these lines needs a human decision first.

## SOLID, concretely

Not as a slogan — as things that are checkable in review.

**Single responsibility.** If describing a class needs the word "and", it is two classes. Class
names ending in `Manager`, `Helper`, `Utils`, or `Processor` are rejected: they name a bag, not a
responsibility. `NodeStatusResolver` resolves node status. That is the standard.

**Open/closed.** Extend by adding a type, not by adding a flag to an existing method. A `bool`
parameter that switches behaviour is a new implementation wearing a disguise.

**Liskov.** Every class is `sealed` unless it was deliberately designed for inheritance, in which
case say so in its XML doc. Composition first; inheritance needs a reason.

**Interface segregation.** Interfaces are defined by the consumer and named for what the consumer
needs, not for what the implementation is. An interface with more than four members needs a
justification. `IWorkingCopyReader` beats `ISvnService`.

**Dependency inversion.** Constructor injection only. No service locator, no static mutable state,
no `new`-ing an I/O type inside logic. If a class needs the clock or the filesystem, it takes them
as a dependency.

**The tell:** if something is hard to test, the fix is almost always to split the decision from
the I/O — not to reach for a mock. `NodeStatusResolver` is the worked example:
`WorkingCopyFileIndex` does the filesystem access and hands the resolver a `NodeSnapshot?`, so
every status rule is a pure function of two arguments and needs no test double at all.

## Readability

Written for the person reading it in eight months, not for the compiler.

- Methods do one thing at one level of abstraction. If you are mixing "walk the tree" with
  "decide whether this file changed", split them.
- Name things after what they mean in the domain: `IsAbsentFromWorkingCopy`, not `IsValid`.
- Prefer a named local over a clever expression. Prefer an early return over nesting.
- Model absence with `null` or an option-shaped type, not with a flag plus fields that are only
  sometimes meaningful.
- **One top-level type per file, named after it.** A second `public` or `internal` type at file
  scope gets its own file, however small — a one-line record, an enum, a delegate. Nested private
  helpers stay with the class they belong to, because they are part of it. Finding `ErrorResponse`
  should mean opening `ErrorResponse.cs`, and a closed hierarchy is still a set of files, not one
  file that happens to hold them all.
- Formatting is CSharpier's job. Do not argue with it, do not hand-format around it.

## Comments

Code first, comments second. Naming and small methods carry the explanation. Before writing a
comment, try renaming the thing — if that fixes it, you did not need the comment.

When one *is* warranted, it is one of two cases:

1. **XML doc on classes and methods** — what is guaranteed, what can throw, what a return value
   means. This is the contract other code reads.
2. **A genuinely non-obvious line** — an ordering constraint, a workaround, a reason the naive
   version is wrong. The *why*, never the *what*.

Rules: 2–3 lines maximum. Say it once. Keep it true — if you change the behaviour, fix the comment
in the same edit. No ticket references, ever. No `// increment counter`. No TODOs that hedge
between two options; unresolved questions go to the human, not into the source.

## Testing

TUnit, in `tests/<ProjectName>.Tests`, one test project per source project.

**Tests must be mutation-resistant.** The bar is not coverage — it is that flipping any single
decision in the code makes at least one test go red. Concretely:

- Every branch has a test that fails if the branch is inverted. Both sides, always.
- Every boundary is tested on both sides of the line. `A_single_microsecond_of_drift_is_still_drift`
  exists because `>=` versus `>` is a real bug that coverage alone would not catch.
- Assert exact values. `IsNotNull()` where you meant `IsEqualTo(x)` is a test that passes for a
  broken implementation.
- No test still passes with its assertion deleted. If it does, it is asserting nothing.
- Where rules have precedence, test the precedence — not just each rule in isolation.
- Parameterised cases include the negative case. `[Arguments("not-present")]` at op_depth 0 *and*
  at op_depth 1, because they must resolve differently.

Test names are sentences describing the rule, not `Test1` or `Resolve_Works`. A reader should be
able to derive the specification from the test list alone.

**Mutation testing is currently blocked.** Stryker.NET 5.0.0 does not support Microsoft.Testing.
Platform ([#3094](https://github.com/stryker-mutator/stryker-net/issues/3094)), which TUnit
requires, and .NET 10 removed the VSTest bridge. `stryker-config.json` and the tool manifest are in
place and will work the moment support lands. Until then the enforced gate is line and branch
coverage on pure logic, plus the design rules above. Do not present coverage numbers as if they
were a mutation score.

**Coverage expectations:** pure logic at 100% line and branch — that is the whole reason it was
extracted. I/O types are covered by integration tests against a real working copy, and are
honestly reported as uncovered until those exist.

Exactly two things are allowed to fall short of 100% branch, and both are cases where no test
could exist rather than cases where none was written:

- **A branch that only exists on the other operating system.** `OperatingSystem.IsWindows()` has
  one live side per machine. The decision underneath it must still take the platform's answer as
  an argument, so the logic itself is covered on both sides — see `ContainingRoot`.
- **A `switch` arm the type system already rules out.** A closed hierarchy with a
  `private protected` constructor still needs a discard arm to compile, and nothing can reach it.
  The hash buckets the compiler emits for a string switch are the same kind of thing.

Anything else short of 100% is a missing test. Say which of the two it is, in the PR or in
PLAN.md; "it's just a default case" is not one of them if the default is reachable.

Every bug fix starts with a failing test that reproduces it.

## Human in the loop

Stop and ask before proceeding when:

- **SVN semantics are unclear or unverified.** wc.db is Subversion's private schema. If you are
  not certain what a column means, do not encode a guess — a wrong guess here silently
  misreports someone's working copy. Ask.
- **Anything could destructively touch a working copy.** Writing, reverting, deleting, or moving
  files under a real checkout gets confirmed first, every time, regardless of how obvious it looks.

Routine refactors, test additions, and work inside an agreed design proceed without asking.

Never report something as working when it has not been run. "Builds clean and 57 tests pass",
"matches `svn status` on a fixture working copy" and "is fast enough on a 100k-file checkout" are
three different claims — `docs/PLAN.md` states which of them currently hold. Keep that honesty.

A test that passes is not evidence on its own. `Present_directory_is_unmodified` was green while
every directory in a real working copy reported as Missing, because the test handed the resolver a
snapshot shape the filesystem layer could never actually produce. When a test asserts something
about the world, check that its inputs are ones the world can generate.

Measure before optimising, and record what you disproved. Replacing the highest-`op_depth`
subquery with a window function looked like an obvious win and was 8× slower; the fix that
mattered was one tree walk instead of 20,000 stats. Both are written down in ARCHITECTURE.md so
nobody pays for them twice.

## Never

- Break the `svn` command-line escape hatch. It must keep working in the same folder, always.
- Treat a direct wc.db read as the only path. Every fast path has a documented CLI fallback.
- Make the local object store authoritative for anything. The working copy is the truth.
- Suppress a warning to make the build pass. `TreatWarningsAsErrors` is on deliberately.
- Commit without `dotnet csharpier format .` and a green `dotnet test`.
