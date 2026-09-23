using System.Globalization;
using Subverted.App.ViewModels;
using Subverted.App.Views;

namespace Subverted.App.Tests;

public sealed class HistoryConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Test]
    public async Task Each_part_of_the_history_list_shows_only_in_its_own_state()
    {
        await Assert.That(Shows(HistoryStateIs.Loading, HistoryState.Loading)).IsTrue();
        await Assert.That(Shows(HistoryStateIs.Loading, HistoryState.Ready)).IsFalse();
        await Assert.That(Shows(HistoryStateIs.Ready, HistoryState.Ready)).IsTrue();
        await Assert.That(Shows(HistoryStateIs.Unreachable, HistoryState.Unreachable)).IsTrue();
        await Assert.That(Shows(HistoryStateIs.Failed, HistoryState.Failed)).IsTrue();
        await Assert.That(Shows(HistoryStateIs.Failed, HistoryState.Unreachable)).IsFalse();
    }

    [Test]
    public async Task Something_that_is_not_a_state_shows_nothing()
    {
        await Assert
            .That(HistoryStateIs.Ready.Convert("Ready", typeof(bool), null, Culture) is true)
            .IsFalse();
    }

    [Test]
    public async Task A_state_converter_does_not_convert_back()
    {
        await Assert
            .That(() => HistoryStateIs.Ready.ConvertBack(true, typeof(HistoryState), null, Culture))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task A_revision_not_in_the_copy_is_drawn_fainter_and_any_other_at_full_strength()
    {
        await Assert
            .That(HistoryRowOpacity.ForNotInCopy.Convert(true, typeof(double), null, Culture))
            .IsEqualTo(HistoryRowOpacity.NotInCopy);
        await Assert
            .That(HistoryRowOpacity.ForNotInCopy.Convert(false, typeof(double), null, Culture))
            .IsEqualTo(1.0);
        await Assert
            .That(HistoryRowOpacity.ForNotInCopy.Convert(null, typeof(double), null, Culture))
            .IsEqualTo(1.0);
    }

    [Test]
    public async Task The_opacity_converter_does_not_convert_back()
    {
        await Assert
            .That(() =>
                HistoryRowOpacity.ForNotInCopy.ConvertBack(1.0, typeof(bool), null, Culture)
            )
            .Throws<NotSupportedException>();
    }

    private static bool Shows(HistoryStateIs converter, HistoryState state) =>
        converter.Convert(state, typeof(bool), null, Culture) is true;
}
