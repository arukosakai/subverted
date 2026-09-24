using Subverted.Core;

namespace Subverted.Protocol;

/// <param name="Path">Any absolute path inside the working copy; the diff covers it and below.</param>
/// <param name="Context">
/// How much unchanged text to show around each change. <see langword="null"/> — and every request
/// from a front-end that predates the field — is <c>svn diff</c>'s own three lines, exactly as
/// before the field existed. <see cref="DiffResponse.Context"/> says what the answer holds.
/// </param>
public sealed record DiffRequest(string Path, DiffContext? Context = null) : DaemonRequest;
