using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Subverted.App.ViewModels;

/// <summary>
/// Every write this window made, oldest first, for the whole session and across working copies:
/// the notice beside the commit box goes away when dismissed, and this is where it is still read.
/// </summary>
public sealed partial class OutputLogViewModel(TimeProvider clock) : ObservableObject
{
    public ObservableCollection<OutputLine> Lines { get; } = [];

    public bool IsEmpty => Lines.Count == 0;

    public void Record(CommitAttempt attempt) =>
        Add(
            "Commit",
            $"{PathCount(attempt.SentRelPaths.Count)} · {FirstLineOf(attempt.Message)}",
            attempt.Notice
        );

    public void Record(RevertAttempt attempt) => Add("Revert", attempt.Target, attempt.Notice);

    [RelayCommand]
    private void Clear()
    {
        Lines.Clear();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void Add(string operation, string subject, Notice notice)
    {
        Lines.Add(new OutputLine(clock.GetLocalNow(), operation, subject, notice));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private static string PathCount(int count) =>
        count == 1 ? "1 path" : string.Create(CultureInfo.InvariantCulture, $"{count:N0} paths");

    private static string FirstLineOf(string message) =>
        message.Trim().Split('\n', 2)[0].TrimEnd('\r');
}
