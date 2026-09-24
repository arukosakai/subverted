namespace Subverted.App.Presentation;

/// <param name="Range">As SVN prints it — <c>@@ -12,6 +12,7 @@</c>, or <c>## -1 +1 ##</c> for a property.</param>
public sealed record DiffHunkRow(string Range) : DiffRow
{
    public override string AutomationName => Range;
}
