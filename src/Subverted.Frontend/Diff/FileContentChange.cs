namespace Subverted.Frontend.Diff;

/// <summary>
/// A closed hierarchy: <see cref="TextChange"/> or <see cref="BinaryChange"/>, and nothing else.
/// </summary>
public abstract record FileContentChange
{
    private protected FileContentChange() { }
}
