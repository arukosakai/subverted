using Subverted.App.Infrastructure;

namespace Subverted.App.Tests;

/// <summary>
/// What reveal-in-file-manager starts, per platform, and what it falls back to when the change is
/// gone from disk. Starting the process itself is left to the real app, where a window opening is
/// what there is to see.
/// </summary>
public sealed class RevealTests
{
    private static readonly string Hero = Path.Combine(Path.GetTempPath(), "art dir", "hero.png");

    [Test]
    public async Task Explorer_is_told_to_select_the_path_as_a_separate_argument()
    {
        var command = RevealCommand.For(Hero, FileManager.Explorer, isDirectory: false);

        await Assert.That(command.FileName).IsEqualTo("explorer.exe");
        await Assert.That(string.Join("|", command.Arguments)).IsEqualTo($"/select,|{Hero}");
    }

    [Test]
    public async Task Finder_is_told_to_reveal_the_path()
    {
        var command = RevealCommand.For(Hero, FileManager.Finder, isDirectory: false);

        await Assert.That(command.FileName).IsEqualTo("open");
        await Assert.That(string.Join("|", command.Arguments)).IsEqualTo($"-R|{Hero}");
    }

    /// <summary>xdg-open cannot select, so a file's folder is the nearest thing to showing it.</summary>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Elsewhere_the_folder_is_opened_a_file_s_or_the_folder_itself(bool isDirectory)
    {
        var command = RevealCommand.For(Hero, FileManager.FreeDesktop, isDirectory);

        await Assert.That(command.FileName).IsEqualTo("xdg-open");
        await Assert
            .That(command.Arguments)
            .IsEquivalentTo(new[] { isDirectory ? Hero : Path.GetDirectoryName(Hero)! });
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

        await Assert.That(SystemFileRevealer.ThisPlatform).IsEqualTo(expected);
    }

    [Test]
    public async Task A_path_on_disk_is_shown_as_it_is()
    {
        using var folder = new Scratch();
        var file = Path.Combine(folder.Path, "hero.png");
        File.WriteAllText(file, "");

        await Assert.That(SystemFileRevealer.NearestPresent(file)).IsEqualTo(file);
        await Assert.That(SystemFileRevealer.NearestPresent(folder.Path)).IsEqualTo(folder.Path);
    }

    /// <summary>A missing file inside a missing folder: the nearest folder still there is shown.</summary>
    [Test]
    public async Task A_path_gone_from_disk_falls_back_to_the_nearest_folder_still_there()
    {
        using var folder = new Scratch();
        var gone = Path.Combine(folder.Path, "gonedir", "sub", "gone.png");

        await Assert.That(SystemFileRevealer.NearestPresent(gone)).IsEqualTo(folder.Path);
    }

    /// <summary>
    /// A drive that was unplugged is the real case, but no machine is sure to lack one; a relative
    /// name that is not there runs out of folders to try the same way on every platform.
    /// </summary>
    [Test]
    public async Task A_path_with_no_folder_left_to_try_has_nothing_to_show_and_starts_nothing()
    {
        var nowhere = $"subverted-nowhere-{Guid.NewGuid():N}";

        await Assert.That(SystemFileRevealer.NearestPresent(nowhere)).IsNull();
        await new SystemFileRevealer(FileManager.FreeDesktop).RevealAsync(nowhere);
    }

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
