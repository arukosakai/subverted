namespace Subverted.App.ViewModels;

/// <param name="Path">What was opened.</param>
/// <param name="Name">Its last segment, which is what the sidebar shows.</param>
/// <param name="IsCurrent">The one on screen now.</param>
public sealed record RecentWorkingCopy(string Path, string Name, bool IsCurrent);
