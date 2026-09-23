using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Subverted.App.Infrastructure;
using Subverted.App.ViewModels;
using Subverted.App.Views;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.App;

/// <summary>The composition root: the one place real I/O is wired to the view models.</summary>
public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Resources[RevealMenuText.ResourceKey] = RevealMenuText.For(FileRevealers.ThisPlatform);

        // Headless tests run the app without a desktop lifetime and build their own windows.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateMainWindow(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static MainWindow CreateMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var channel = new DaemonChannel(
            DaemonSocketPath.FromEnvironment(),
            DaemonChannel.ExecutableNextTo(AppContext.BaseDirectory)
        );
        var status = new DaemonWorkingCopyStatus(channel);
        var diffs = new DaemonWorkingCopyDiff(channel);
        var sizes = new FileSizeReader();
        var launcher = new SystemFileLauncher(() => desktop.MainWindow);
        var revealer = FileRevealers.For(FileRevealers.ThisPlatform);
        var clipboard = new WindowClipboard(() => desktop.MainWindow);

        var viewModel = new MainWindowViewModel(
            new RecentWorkingCopiesFile(RecentWorkingCopiesFile.DefaultPath),
            new StorageFolderPicker(() => desktop.MainWindow),
            path => new WorkingCopyViewModel(
                path,
                status,
                new DiffPaneViewModel(diffs, sizes, launcher, TimeProvider.System),
                launcher,
                revealer,
                clipboard
            ),
            TimeProvider.System,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal
        );

        return new MainWindow(viewModel);
    }
}
