using Subverted.Core;

namespace Subverted.Protocol;

/// <param name="Route">
/// Which kind of rename this turned out to be. Only the two that do the move reach a front-end —
/// the three that refuse come back as an <see cref="ErrorResponse"/> instead, because they mean
/// nothing happened.
/// </param>
/// <param name="Notifications">SVN's own text: an <c>A</c> line for the destination, a <c>D</c> for the source.</param>
public sealed record MoveResponse(MoveRoute Route, string Notifications) : DaemonResponse;
