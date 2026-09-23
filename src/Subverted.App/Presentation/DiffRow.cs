namespace Subverted.App.Presentation;

/// <summary>
/// One row of a flattened diff, as the virtualised line list draws it. A closed hierarchy: the
/// <c>Diff*Row</c> types beside this file are the only kinds of row there are.
/// </summary>
public abstract record DiffRow
{
    private protected DiffRow() { }
}
