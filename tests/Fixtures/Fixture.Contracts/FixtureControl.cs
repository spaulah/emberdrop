using System.Collections.Concurrent;

namespace Fixture.Contracts;

/// <summary>
/// Shared state that lets a test coordinate with fixture plugin work units. It lives in the contract assembly, which both
/// the host and every plugin resolve from the default context, so they all see the same instance. Tests use unique
/// names (a new Guid) for everything they put here, so tests do not interfere with each other.
/// </summary>
public static class FixtureControl
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource> Gates = new();
    private static readonly ConcurrentDictionary<string, TaskCompletionSource> StartSignals = new();
    private static readonly ConcurrentDictionary<string, string> Outcomes = new();
    private static readonly ConcurrentDictionary<string, int> Counts = new();
    private static readonly ConcurrentDictionary<string, string> Versions = new();
    private static readonly ConcurrentDictionary<string, bool> StopFlags = new();

    /// <summary>A static event on a host-side type. A plugin that subscribes and never unsubscribes pins its own load context.</summary>
    public static event EventHandler? HostEvent;

    public static int HostEventSubscriberCount => HostEvent?.GetInvocationList().Length ?? 0;

    public static void ClearHostEventSubscribers() => HostEvent = null;

    public static TaskCompletionSource Gate(string name) => Gates.GetOrAdd(name, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));

    public static void OpenGate(string name) => Gate(name).TrySetResult();

    public static Task Started(string name) => StartSignal(name).Task;

    public static void SignalStarted(string name) => StartSignal(name).TrySetResult();

    public static void Record(string name, string outcome) => Outcomes[name] = outcome;

    public static string? OutcomeOf(string name) => Outcomes.TryGetValue(name, out var outcome) ? outcome : null;

    public static void Count(string id, string version)
    {
        Counts.AddOrUpdate(id, 1, (_, n) => n + 1);
        Versions[id] = version;
    }

    public static int CountOf(string id) => Counts.TryGetValue(id, out var n) ? n : 0;

    public static string? VersionOf(string id) => Versions.TryGetValue(id, out var v) ? v : null;

    public static IReadOnlyDictionary<string, int> AllCounts => Counts;

    public static bool ShouldStop(string name) => StopFlags.TryGetValue(name, out var stop) && stop;

    public static void StopThread(string name) => StopFlags[name] = true;

    private static TaskCompletionSource StartSignal(string name) => StartSignals.GetOrAdd(name, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));

    /// <summary>Records that something with this name happened, for a test to read back.</summary>
    public static void Mark(string name) => Record(name, "yes");

    public static bool IsMarked(string name) => OutcomeOf(name) == "yes";
}
