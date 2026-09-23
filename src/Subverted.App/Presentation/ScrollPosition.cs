namespace Subverted.App.Presentation;

/// <summary>When a scrolled list is close enough to its end that the next page should be asked for.</summary>
public static class ScrollPosition
{
    /// <param name="offset">How far down the list is scrolled.</param>
    /// <param name="viewport">How much of it is visible.</param>
    /// <param name="extent">How long it is.</param>
    /// <param name="lead">How far before the end to start asking, so the page lands before the end is reached.</param>
    public static bool IsNearEnd(double offset, double viewport, double extent, double lead) =>
        offset + viewport >= extent - lead;
}
