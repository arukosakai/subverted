namespace Subverted.App.Presentation;

/// <summary>Opens a file's property changes, which SVN prints after its content.</summary>
public sealed record DiffPropertySectionRow(string Path) : DiffRow
{
    public override string AutomationName => "Property changes";
}
