namespace Subverted.App.Presentation;

/// <summary>A file SVN would not diff because it is marked binary.</summary>
/// <param name="MimeType">The <c>svn:mime-type</c> SVN named, or <c>null</c>.</param>
/// <param name="IsOnlyFile">
/// The diff is the selected file's alone, so the row's size and "open in app" describe it. In a
/// directory's diff, even one holding a single file, they describe the directory and are left out.
/// </param>
public sealed record DiffBinaryRow(string Path, string? MimeType, bool IsOnlyFile) : DiffRow
{
    public string MimeTypeText => MimeType ?? "No MIME type recorded";

    public override string AutomationName => $"Binary file, {MimeTypeText}";
}
