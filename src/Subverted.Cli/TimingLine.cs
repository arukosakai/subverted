using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// Where an answer's time went. The split is the point: the daemon's share is what M1's speed
/// criterion is about, and everything else is this process starting up.
/// </summary>
public static class TimingLine
{
    public static string For(StatusResponse response, double totalMilliseconds) =>
        $"({(response.ServedFromWarmIndex ? "warm" : "cold")}, "
        + $"daemon {response.ServerElapsedMilliseconds:F1} ms, "
        + $"sv {totalMilliseconds - response.ServerElapsedMilliseconds:F0} ms, "
        + $"total {totalMilliseconds:F0} ms)";
}
