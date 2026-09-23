using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Subverted.App.ViewModels;

namespace Subverted.App.Views;

/// <summary>A commit's or a revert's notice, with a way to put it away. Invisible while there is none.</summary>
public sealed partial class NoticeView : UserControl
{
    public static readonly StyledProperty<Notice?> NoticeProperty = AvaloniaProperty.Register<
        NoticeView,
        Notice?
    >(nameof(Notice));

    public static readonly StyledProperty<ICommand?> DismissCommandProperty =
        AvaloniaProperty.Register<NoticeView, ICommand?>(nameof(DismissCommand));

    public NoticeView()
    {
        InitializeComponent();
        Card.DataContext = this;
    }

    public Notice? Notice
    {
        get => GetValue(NoticeProperty);
        set => SetValue(NoticeProperty, value);
    }

    public ICommand? DismissCommand
    {
        get => GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }
}
