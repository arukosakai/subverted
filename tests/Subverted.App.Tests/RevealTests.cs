using Subverted.App.Infrastructure;

namespace Subverted.App.Tests;

/// <summary>
/// Which revealer each platform gets, what the command-line ones start, and what every one falls
/// back to when the change is gone from disk. Opening a file manager window is left to the real
/// app, where a window opening is what there is to see.
/// </summary>
public sealed class RevealTests
{
    private static readonly string Hero = Path.Combine(Path.GetTempPath(), "art dir", "hero.png");

    [Test]
    public async Task Finder_is_told_to_reveal_the_path()
    {
        var command = RevealCommand.Finder(Hero);

        await Assert.That(command.FileName).IsEqualTo("open");
        await Assert.That(string.Join("|", command.Arguments)).IsEqualTo($"-R|{Hero}");
    }

    /// <summary>xdg-open cannot select, so a file's folder is the nearest thing to showing it.</summary>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Elsewhere_the_folder_is_opened_a_file_s_or_the_folder_itself(bool isDirectory)
    {
        var command = RevealCommand.FreeDesktop(Hero, isDirectory);

        await Assert.That(command.FileName).IsEqualTo("xdg-open");
        await Assert
            .That(string.Join("|", command.Arguments))
            .IsEqualTo(isDirectory ? Hero : Path.GetDirectoryName(Hero)!);
    }

    [Test]
    [Arguments(FileManager.Explorer, typeof(ShellFileRevealer))]
    [Arguments(FileManager.Finder, typeof(ProcessFileRevealer))]
    [Arguments(FileManager.FreeDesktop, typeof(ProcessFileRevealer))]
    public async Task Each_platform_reveals_its_own_way(FileManager fileManager, Type revealer)
    {
        await Assert.That(FileRevealers.For(fileManager).GetType()).IsEqualTo(revealer);
    }

    [Test]
    [Arguments(FileManager.Explorer, "Show in Explorer")]
    [Arguments(FileManager.Finder, "Reveal in Finder")]
    [Arguments(FileManager.FreeDesktop, "Open containing folder")]
    public async Task The_menu_says_it_in_the_platform_s_words(FileManager fileManager, string text)
    {
        await Assert.That(RevealMenuText.For(fileManager)).IsEqualTo(text);
    }

    [Test]
    public async Task This_machine_has_the_file_manager_of_its_platform()
    {
        var expected =
            OperatingSystem.IsWindows() ? FileManager.Explorer
            : OperatingSystem.IsMacOS() ? FileManager.Finder
            : FileManager.FreeDesktop;

        await Assert.That(FileRevealers.ThisPlatform).IsEqualTo(expected);
    }

    [Test]
    public async Task A_path_on_disk_is_shown_as_it_is()
    {
        using var folder = new Scratch();
        var file = Path.Combine(folder.Path, "hero.png");
        File.WriteAllText(file, "");

        await Assert.That(NearestPresentPath.Of(file)).IsEqualTo(file);
        await Assert.That(NearestPresentPath.Of(folder.Path)).IsEqualTo(folder.Path);
    }

    /// <summary>A missing file inside a missing folder: the nearest folder still there is shown.</summary>
    [Test]
    public async Task A_path_gone_from_disk_falls_back_to_the_nearest_folder_still_there()
    {
        using var folder = new Scratch();
        var gone = Path.Combine(folder.Path, "gonedir", "sub", "gone.png");

        await Assert.That(NearestPresentPath.Of(gone)).IsEqualTo(folder.Path);
    }

    /// <summary>
    /// A drive that was unplugged is the real case, but no machine is sure to lack one; a relative
    /// name that is not there runs out of folders to try the same way on every platform.
    /// </summary>
    [Test]
    public async Task A_path_with_no_folder_left_to_try_has_nothing_to_show()
    {
        await Assert.That(NearestPresentPath.Of(Nowhere())).IsNull();
    }

    [Test]
    public async Task With_nothing_to_show_neither_revealer_starts_anything()
    {
        var asked = 0;
        var process = new ProcessFileRevealer(
            (path, _) =>
            {
                asked++;
                return RevealCommand.Finder(path);
            }
        );

        await process.RevealAsync(Nowhere());
        await new ShellFileRevealer().RevealAsync(Nowhere());

        await Assert.That(asked).IsEqualTo(0);
    }

    private static string Nowhere() => $"subverted-nowhere-{Guid.NewGuid():N}";

    private sealed class Scratch : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"subverted-reveal-{Guid.NewGuid():N}"
            );

        public Scratch() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
