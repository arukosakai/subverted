namespace Subverted.App.ViewModels;

/// <summary>Puts text where the person's next paste will find it.</summary>
public interface ITextClipboard
{
    Task CopyAsync(string text);
}
