using System.Collections.Concurrent;

namespace Emberdrop.Plugins;

/// <summary>Collects what plugin code declares through <see cref="IPluginActivity"/>. Holds only strings and times.</summary>
internal sealed class PluginActivityTracker : IPluginActivity
{
    private readonly ConcurrentDictionary<Guid, PluginActivityInfo> _active = new();

    public IDisposable Track(string kind, string description)
    {
        var id = Guid.NewGuid();
        _active[id] = new PluginActivityInfo(kind, description, DateTimeOffset.UtcNow);
        return new Registration(_active, id);
    }

    public IReadOnlyList<PluginActivityInfo> Snapshot() => _active.Values.OrderBy(a => a.StartedAt).ToList();

    private sealed class Registration(ConcurrentDictionary<Guid, PluginActivityInfo> active, Guid id) : IDisposable
    {
        public void Dispose() => active.TryRemove(id, out _);
    }
}
