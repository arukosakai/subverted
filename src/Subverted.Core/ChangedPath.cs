namespace Subverted.Core;

/// <param name="Path">
/// Repository-absolute, starting at the repository root rather than the working copy — this is
/// what a revision touched, and a revision knows nothing about anyone's checkout.
/// </param>
/// <param name="CopiedFromPath">Where a copy came from, or null when the add came from nowhere.</param>
/// <param name="CopiedFromRevision">
/// The revision copied from. Non-null exactly when <paramref name="CopiedFromPath"/> is.
/// </param>
public sealed record ChangedPath(
    string Path,
    PathChange Change,
    string? CopiedFromPath,
    long? CopiedFromRevision
);
