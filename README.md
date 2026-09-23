# Subverted

A Subversion client that does not feel like a punishment.

[![CI](https://github.com/arukosakai/subverted/actions/workflows/ci.yml/badge.svg)](https://github.com/arukosakai/subverted/actions/workflows/ci.yml)
[![License: WTFPL](https://img.shields.io/badge/license-WTFPL-brightgreen.svg)](LICENSE)

SVN handles big binary-heavy repositories and file locking well. What's painful is that status on
a large checkout is slow, every operation is a server round-trip, and the GUI clients mostly give
the same model a nicer skin.

Subverted keeps a daemon resident next to your working copy so `sv st` answers from a warm index,
and puts a CLI (`sv`) and a desktop app on top of it. The long-term plan (local history, file-level
staging, locking workflows) is in [`docs/PLAN.md`](docs/PLAN.md).

**The working copy stays the truth.** Subverted reads SVN's metadata and shells out to `svn` for
everything that changes it. Plain `svn` keeps working in the same folder at all times. If
Subverted dies, nothing is lost and nobody is blocked.

## Status

Early, and honest about it.

- **M0, reading working copies directly:** done. Status matches `svn status` on the fixture
  working copies, with two known divergences documented in PLAN.md.
- **M1, daemon and CLI:** in progress, mostly working. Warm `sv st` on a 100k-file checkout is
  well under the 50 ms target on the daemon side.
- **M2, the GUI:** in progress. Live status and diff work, and the rest of the day-to-day flow is
  landing one slice at a time.

It has been built and used on **Windows**. The Linux and macOS bundles come out of the release
workflow, but nobody has run them yet, and the test suite is advisory on those platforms in CI
until it has been seen green there.

## Install

Grab your platform's download from
[Releases](https://github.com/arukosakai/subverted/releases). Every download has the desktop app
and `sv` together.

- **Windows:** run `subverted-<version>-win-x64-setup.exe`. It installs for your user only (no
  admin prompt), into `%LOCALAPPDATA%\Programs\Subverted`, and adds that folder to your PATH, so
  `sv` works in any new terminal. The uninstaller takes it back off. There's also a plain `.zip`
  if you'd rather not install.
- **Linux / macOS:** unpack the `.tar.gz` and run `./install.sh`. It copies everything to
  `~/.local/share/subverted`, puts `sv` and `subverted` in `~/.local/bin`, and adds that to PATH
  in your shell profile if it isn't there yet. On Linux it also adds a menu entry.
  `./install.sh --uninstall` removes all of it. Set `SUBVERTED_PREFIX` to install somewhere
  other than `~/.local`.

Whichever way you install, the three executables have to stay in the same folder:

| File | What it is |
| --- | --- |
| `sv` / `sv.exe` | the command line |
| `Subverted` / `Subverted.exe` | the desktop app |
| `subverted-daemon` | the background process both of them talk to; starts itself on first use |

The bundles are self-contained, so you don't need .NET installed. You **do** need the `svn`
command line on `PATH`, since everything except `sv st` goes through it:

- **Windows:** [SlikSVN](https://sliksvn.com/download/), or TortoiseSVN with "command line client
  tools" ticked in the installer
- **macOS:** `brew install subversion`
- **Linux:** `apt install subversion` (or your distro's equivalent)

On macOS the bundle isn't signed and isn't a `.app` yet, so start the GUI with `subverted` from a
terminal. Gatekeeper will complain until you clear the quarantine flag, once, after unpacking:

```sh
xattr -dr com.apple.quarantine subverted-*-osx-arm64
```

Builds of every push to `master` are attached to the
[CI runs](https://github.com/arukosakai/subverted/actions/workflows/ci.yml) as artifacts, if you
want something newer than the last release.

## Using it

```sh
sv st                       # what changed, from the daemon's warm index
sv d art/hero.png           # diff of local changes
sv log -l 5                 # last five revisions
sv up                       # pull in everyone else's work
sv lock art/hero.psd -m "repainting the sky"
sv commit -m "new sky" --mark   # also adds new (?) and deletes missing (!) files
sv pick -m "just the fix"   # walk the changes one at a time, commit what you pick
sv daemon status            # what the daemon holds, and whether it's watching
```

`sv --help` lists everything. Anything that throws work away (`revert`, `rm`, `resolve --theirs`)
shows you what it's about to do and asks first.

For the GUI, run `Subverted` and open any folder inside a working copy.

## Building from source

You need the .NET SDK pinned in [`global.json`](global.json) and `svn` on `PATH`.

```sh
dotnet tool restore
dotnet build Subverted.slnx
bash tools/run-tests.sh          # runs each test project's own executable
dotnet csharpier format .        # before every commit
```

Use `tools/run-tests.sh` rather than `dotnet test`. Under this SDK `dotnet test` sometimes
discovers zero tests and reports that as success. [`CLAUDE.md`](CLAUDE.md) has the details.

To produce the same layout a release ships:

```sh
dotnet publish src/Subverted.Daemon -c Release -o artifacts/bin
dotnet publish src/Subverted.Cli    -c Release -o artifacts/bin
dotnet publish src/Subverted.App    -c Release -o artifacts/bin
```

Releases are cut by pushing a `v*` tag. The workflow runs CI, bundles win-x64, linux-x64 and
osx-arm64, builds the Windows installer from [`installer/`](installer), and attaches it all to
a GitHub release. Tags with a `-` in them (`v0.2.0-beta.1`) are
marked as prereleases.

## Layout

```
src/Subverted.Core       status model, depends on nothing
src/Subverted.Svn        the only project that knows SVN exists (wc.db reads, the svn CLI)
src/Subverted.Protocol   messages between the daemon and the front-ends
src/Subverted.Daemon     holds the index, watches the disk, runs svn on the front-ends' behalf
src/Subverted.Frontend   finds or starts the daemon and talks to it
src/Subverted.Cli        sv
src/Subverted.App        the Avalonia desktop app
tools/                   probes, benchmarks and fixture builders
```

[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) explains how the pieces fit and records every
design decision, including the ones that were measured and turned out wrong.
[`CLAUDE.md`](CLAUDE.md) is the working agreement for contributors: it's strict, because this
tool touches the place people's unpushed work lives.

## License

[WTFPL](LICENSE). Do what you want with it.
