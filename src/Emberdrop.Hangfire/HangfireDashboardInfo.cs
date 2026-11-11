namespace Emberdrop.Hangfire;

/// <summary>Records where the Hangfire dashboard is mounted, if the host chose to mount it.</summary>
public sealed class HangfireDashboardInfo
{
    /// <summary>The dashboard path, or <see langword="null"/> when the dashboard is not mounted.</summary>
    public string? Path { get; internal set; }
}
