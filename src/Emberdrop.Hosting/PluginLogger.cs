using Microsoft.Extensions.Logging;

namespace Emberdrop.Plugins;

/// <summary>
/// <c>ILogger&lt;T&gt;</c> for plugin types, forwarding to the host's logger factory by category name. It is created inside
/// the plugin's own container, so the closed generic over the plugin's type is owned by the plugin and freed with it.
/// </summary>
/// <typeparam name="T">The plugin type the logger is for.</typeparam>
internal sealed class PluginLogger<T> : ILogger<T>
{
    private readonly ILogger _inner;

    public PluginLogger(ILoggerFactory factory)
    {
        _inner = factory.CreateLogger(typeof(T).FullName ?? typeof(T).Name);
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => _inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _inner.Log(logLevel, eventId, state, exception, formatter);
}
