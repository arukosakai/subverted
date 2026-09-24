using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.App.Views;

namespace Subverted.App.Tests;

public sealed class ConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>Exact token values, per variant: a converter that returned any brush would pass less.</summary>
    [Test]
    [Arguments("Dark", "Solid", "#FF34D399")]
    [Arguments("Dark", "Soft", "#2634D399")]
    [Arguments("Light", "Solid", "#FF0F9F6E")]
    [Arguments("Light", "Soft", "#1F0F9F6E")]
    public async Task A_tone_resolves_to_its_themes_brush(
        string variant,
        string kind,
        string expected
    )
    {
        var colour = await HeadlessApp.Session.Dispatch(
            () =>
            {
                Application.Current!.RequestedThemeVariant =
                    variant == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
                var converter = kind == "Soft" ? ToneBrush.Soft : ToneBrush.Solid;
                var brush = (ISolidColorBrush)
                    converter.Convert(ChangeTone.Added, typeof(IBrush), null, Culture)!;
                return Task.FromResult(brush.Color);
            },
            CancellationToken.None
        );

        await Assert.That(colour).IsEqualTo(Color.Parse(expected));
    }

    [Test]
    [Arguments("not a tone")]
    [Arguments(null)]
    public async Task Anything_but_a_tone_draws_nothing(object? value)
    {
        await Assert
            .That(ToneBrush.Solid.Convert(value, typeof(IBrush), null, Culture))
            .IsEqualTo(Brushes.Transparent);
    }

    /// <summary>A tone the theme has no brush for is drawn clear rather than failing the frame.</summary>
    [Test]
    public async Task A_tone_without_a_brush_draws_nothing()
    {
        var brush = await HeadlessApp.Session.Dispatch(
            () =>
                Task.FromResult(
                    ToneBrush.Solid.Convert((ChangeTone)99, typeof(IBrush), null, Culture)
                ),
            CancellationToken.None
        );

        await Assert.That(brush).IsEqualTo(Brushes.Transparent);
    }

    [Test]
    [Arguments(WorkingCopyState.Loading, true)]
    [Arguments(WorkingCopyState.Ready, false)]
    public async Task A_state_shows_only_what_belongs_to_it(WorkingCopyState state, bool expected)
    {
        await Assert
            .That(StateIs.Loading.Convert(state, typeof(bool), null, Culture))
            .IsEqualTo(expected);
    }

    [Test]
    public async Task Anything_but_a_state_shows_nothing()
    {
        await Assert
            .That((bool)StateIs.Loading.Convert("Loading", typeof(bool), null, Culture)!)
            .IsFalse();
    }

    [Test]
    public async Task Neither_converter_pretends_to_convert_back()
    {
        await Assert
            .That(() => ToneBrush.Solid.ConvertBack(Brushes.Red, typeof(ChangeTone), null, Culture))
            .Throws<NotSupportedException>();
        await Assert
            .That(() => StateIs.Loading.ConvertBack(true, typeof(WorkingCopyState), null, Culture))
            .Throws<NotSupportedException>();
    }
}
