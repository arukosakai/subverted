using System.Runtime.CompilerServices;

// The wc.db row shape and status rules are internal on purpose — they are Subversion's private
// schema, not our public contract — but they are exactly what needs the heaviest test coverage.
[assembly: InternalsVisibleTo("Subverted.Svn.Tests")]

// For SvnLocale, which the shared working-copy fixture runs its own svn under.
[assembly: InternalsVisibleTo("Subverted.Daemon.Tests")]
