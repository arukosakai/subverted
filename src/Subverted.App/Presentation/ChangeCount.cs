namespace Subverted.App.Presentation;

/// <param name="Text">Ready to print — "3 modified", "1 conflict".</param>
public sealed record ChangeCount(ChangeTone Tone, int Count, string Text);
