using System.Text.Json.Serialization;

namespace Subverted.Protocol;

/// <summary>
/// Everything a front-end can ask the daemon. Designed for inheritance, but only from inside this
/// assembly: the private protected constructor closes the set so the wire discriminator can stay
/// exhaustive.
/// </summary>
/// <remarks>
/// The discriminator is <c>$kind</c>, not <c>kind</c>, for the reason given on
/// <see cref="DaemonResponse"/>; both sides use the same name so neither has to remember which.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(StatusRequest), "status")]
[JsonDerivedType(typeof(LogRequest), "log")]
[JsonDerivedType(typeof(DiffRequest), "diff")]
[JsonDerivedType(typeof(AddRequest), "add")]
[JsonDerivedType(typeof(RevertRequest), "revert")]
[JsonDerivedType(typeof(DeleteRequest), "delete")]
[JsonDerivedType(typeof(MoveRequest), "move")]
[JsonDerivedType(typeof(CommitRequest), "commit")]
[JsonDerivedType(typeof(UpdateRequest), "update")]
[JsonDerivedType(typeof(LockRequest), "lock")]
[JsonDerivedType(typeof(UnlockRequest), "unlock")]
[JsonDerivedType(typeof(ResolveRequest), "resolve")]
[JsonDerivedType(typeof(CleanupRequest), "cleanup")]
[JsonDerivedType(typeof(DaemonInfoRequest), "daemon-info")]
[JsonDerivedType(typeof(ShutdownRequest), "shutdown")]
public abstract record DaemonRequest
{
    private protected DaemonRequest() { }
}
