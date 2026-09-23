namespace Subverted.App.Infrastructure;

/// <summary>What the context menu calls revealing a path, in each platform's own words.</summary>
public static class RevealMenuText
{
    /// <summary>The application resource the view's menu item reads its header from.</summary>
    public const string ResourceKey = "Text.Reveal";

    public static string For(FileManager fileManager) =>
        fileManager switch
        {
            FileManager.Explorer => "Show in Explorer",
            FileManager.Finder => "Reveal in Finder",
            _ => "Open containing folder",
        };
}
