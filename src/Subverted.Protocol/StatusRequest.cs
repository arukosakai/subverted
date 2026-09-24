namespace Subverted.Protocol;

/// <param name="WorkingCopyPath">
/// Any absolute path inside the working copy; the daemon resolves it to the root itself.
/// </param>
/// <param name="IncludeUnmodified">
/// Report clean nodes too — <c>svn status -v</c>. Off by default because the answer is otherwise
/// one line per file on a hundred-thousand-file checkout.
/// </param>
/// <param name="IncludeIgnored">Report ignored nodes — <c>svn status --no-ignore</c>.</param>
/// <param name="Scope">
/// Absolute paths to list, each with everything beneath it, as <c>svn status PATH...</c> lists them.
/// Null lists the whole working copy — which a preview needs, because it filters for every target
/// itself and a listing scoped to one of them would hide what the others are about to lose.
/// </param>
/// <param name="HeldScan">
/// The <see cref="StatusResponse.ScanId"/> of a listing the caller already holds for this same
/// request. If the answer would be read from that scan, it comes back as
/// <see cref="StatusUnchangedResponse"/> instead of the entries again. Null always gets the entries,
/// and so does every value from a daemon that predates the field, which ignores it.
/// </param>
public sealed record StatusRequest(
    string WorkingCopyPath,
    bool IncludeUnmodified,
    bool IncludeIgnored,
    IReadOnlyList<string>? Scope = null,
    Guid? HeldScan = null
) : DaemonRequest;
