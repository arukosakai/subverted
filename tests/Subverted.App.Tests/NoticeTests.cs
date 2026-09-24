using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.App.Views;

namespace Subverted.App.Tests;

public sealed class NoticeTests
{
    /// <summary>Each outcome borrows a list tone, so a refusal never reads like a success.</summary>
    [Test]
    [Arguments(NoticeKind.Succeeded, ChangeTone.Added)]
    [Arguments(NoticeKind.LeftMarked, ChangeTone.Missing)]
    [Arguments(NoticeKind.NothingWritten, ChangeTone.Conflict)]
    [Arguments(NoticeKind.Uncertain, ChangeTone.Modified)]
    [Arguments(NoticeKind.NeedsAttention, ChangeTone.Conflict)]
    public async Task Each_kind_of_outcome_is_drawn_in_its_own_tone(
        NoticeKind kind,
        ChangeTone tone
    )
    {
        await Assert.That(new Notice(kind, "h", null, null).Tone).IsEqualTo(tone);
    }

    [Test]
    public async Task The_notice_view_shows_what_it_is_given_and_hides_when_given_nothing()
    {
        var notice = new Notice(NoticeKind.NothingWritten, "Nothing was committed", "why", "hint");
        var dismiss = new RelayCommand(() => { });

        var (given, command, visible, hiddenAfter) = await HeadlessApp.Session.Dispatch(
            () =>
            {
                var view = new NoticeView { Notice = notice, DismissCommand = dismiss };
                var window = new Window { Content = view };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var card = view.FindControl<Border>("Card")!;
                var shown = card.IsVisible;
                view.Notice = null;
                Dispatcher.UIThread.RunJobs();
                var result = (view.Notice, view.DismissCommand, shown, !card.IsVisible);
                window.Close();
                return Task.FromResult(result);
            },
            CancellationToken.None
        );

        await Assert.That(given).IsNull();
        await Assert.That(command).IsSameReferenceAs(dismiss);
        await Assert.That(visible).IsTrue();
        await Assert.That(hiddenAfter).IsTrue();
    }
}
