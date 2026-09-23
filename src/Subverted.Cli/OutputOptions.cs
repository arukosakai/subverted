namespace Subverted.Cli;

/// <param name="Colour">The user did not turn colour off. The terminal still gets the final say.</param>
/// <param name="Pager">The user did not turn paging off.</param>
/// <param name="Timing">
/// Print where the time went. The M1 speed criterion is about the daemon's share of it, and that
/// share is invisible without asking.
/// </param>
public sealed record OutputOptions(bool Colour, bool Pager, bool Timing);
