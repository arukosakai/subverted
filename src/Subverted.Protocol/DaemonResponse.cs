using System.Text.Json.Serialization;

namespace Subverted.Protocol;

/// <summary>
/// Everything the daemon can answer with. Designed for inheritance, but only from inside this
/// assembly — see <see cref="DaemonRequest"/> for why the set is closed.
/// </summary>
/// <remarks>
/// The discriminator is <c>$kind</c>, not <c>kind</c>: <see cref="ErrorResponse"/> has a
/// <c>Kind</c> of its own and a message type may not carry a property named after it.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(StatusResponse), "status")]
[JsonDerivedType(typeof(LogResponse), "log")]
[JsonDerivedType(typeof(DiffResponse), "diff")]
[JsonDerivedType(typeof(AddResponse), "add")]
[JsonDerivedType(typeof(RevertResponse), "revert")]
[JsonDerivedType(typeof(DeleteResponse), "delete")]
[JsonDerivedType(typeof(MoveResponse), "move")]
[JsonDerivedType(typeof(CommitResponse), "commit")]
[JsonDerivedType(typeof(CommitSelectionResponse), "commit-selection")]
[JsonDerivedType(typeof(SelectionNotCommittedResponse), "selection-not-committed")]
[JsonDerivedType(typeof(UpdateResponse), "update")]
[JsonDerivedType(typeof(LockResponse), "lock")]
[JsonDerivedType(typeof(UnlockResponse), "unlock")]
[JsonDerivedType(typeof(ResolveResponse), "resolve")]
[JsonDerivedType(typeof(CleanupResponse), "cleanup")]
[JsonDerivedType(typeof(DaemonInfoResponse), "daemon-info")]
[JsonDerivedType(typeof(AcknowledgedResponse), "ack")]
[JsonDerivedType(typeof(ErrorResponse), "error")]
[JsonDerivedType(typeof(WorkingCopyRevisionResponse), "working-copy-revision")]
public abstract record DaemonResponse
{
    private protected DaemonResponse() { }
}
