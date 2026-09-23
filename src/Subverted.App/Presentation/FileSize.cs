using System.Globalization;

namespace Subverted.App.Presentation;

/// <summary>A size in the units a file browser uses: binary multiples, labelled KB, MB, GB and TB.</summary>
public static class FileSize
{
    private static readonly string[] Units = ["KB", "MB", "GB", "TB"];

    public static string Format(long bytes, CultureInfo culture)
    {
        if (bytes < 1024)
        {
            return bytes == 1 ? "1 byte" : $"{bytes.ToString(culture)} bytes";
        }

        var value = bytes / 1024.0;
        var unit = 0;
        // Rounded first, so a value just under the next unit reads "1 MB" rather than "1024 KB".
        while (Math.Round(value, 1) >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value.ToString("0.#", culture)} {Units[unit]}";
    }
}
