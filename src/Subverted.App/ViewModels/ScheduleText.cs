using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>What a commit selection marked before sending, as a phrase.</summary>
public static class ScheduleText
{
    /// <returns>For example "1 rename recorded, 2 added, 1 deleted"; <c>null</c> when nothing was marked.</returns>
    public static string? Of(SelectionSchedule schedule)
    {
        List<string> parts = [];
        if (schedule.Moved.Count > 0)
        {
            parts.Add(
                schedule.Moved.Count == 1
                    ? "1 rename recorded"
                    : $"{schedule.Moved.Count} renames recorded"
            );
        }

        if (schedule.Added.Count > 0)
        {
            parts.Add($"{schedule.Added.Count} added");
        }

        if (schedule.Deleted.Count > 0)
        {
            parts.Add($"{schedule.Deleted.Count} deleted");
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
