namespace Emberdrop.Scheduling;

/// <summary>
/// Declares what a scheduler engine supports so callers can check before asking,
/// instead of discovering a gap through an exception.
/// </summary>
/// <param name="EngineName">Human-readable engine name.</param>
/// <param name="Features">The optional features the engine implements.</param>
/// <param name="DashboardPath">Path of the engine's own management UI on this host, or <see langword="null"/> if there is none.</param>
public sealed record SchedulerCapabilities(string EngineName, SchedulerFeatures Features, string? DashboardPath = null)
{
    /// <summary>Returns whether every flag in <paramref name="feature"/> is supported.</summary>
    public bool Supports(SchedulerFeatures feature) => (Features & feature) == feature;
}
