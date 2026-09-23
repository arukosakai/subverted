namespace Subverted.Core;

/// <param name="Route">What the rename turned out to be. Three of its values mean nothing was done.</param>
/// <param name="Notifications">
/// SVN's own text — an <c>A</c> line for the destination and a <c>D</c> for the source. Empty when
/// <paramref name="Route"/> is one that refuses, because then no client ran.
/// </param>
public sealed record MoveOutcome(MoveRoute Route, string Notifications);
