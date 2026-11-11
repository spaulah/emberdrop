using Emberdrop.Plugins;
namespace Emberdrop.WorkUnits;

/// <summary>Describes one work unit that a plugin provides.</summary>
/// <param name="Name">The work unit's logical name, unique within the plugin.</param>
/// <param name="TypeName">The full name of the CLR type that implements the work unit, as a string.</param>
/// <param name="AssemblyName">The simple name of the assembly that contains the work unit.</param>
/// <param name="DefaultSchedule">The cron schedule the plugin suggests, if any.</param>
/// <param name="RequiredConfiguration">Configuration keys the work unit needs.</param>
/// <param name="InputSchemaVersion">The schema version of the stored input (<see cref="WorkContext.Input"/>) the work unit understands.</param>
/// <param name="Kind">The kind of work unit (<c>work-unit</c> by default).</param>
/// <param name="Description">The manifest's description, if any.</param>
/// <param name="Triggers">The triggers the manifest declares. They are defaults; operator-created triggers are stored separately.</param>
/// <param name="Capabilities">The capabilities the manifest declares. Informational in v0.1 (see docs/concepts/capabilities.md).</param>
/// <param name="InputTypeName">The full name of the input type, or <see langword="null"/> when the work unit takes no input.</param>
/// <param name="OutputTypeName">The full name of the output type, or <see langword="null"/> when the work unit produces no output.</param>
/// <param name="InputSchema">The JSON Schema of the input type, or <see langword="null"/>.</param>
/// <param name="OutputSchema">The JSON Schema of the output type, or <see langword="null"/>.</param>
public sealed record PluginWorkUnitDescriptor(
    string Name,
    string TypeName,
    string AssemblyName,
    string? DefaultSchedule,
    IReadOnlyList<string> RequiredConfiguration,
    int InputSchemaVersion = 1,
    string Kind = "work-unit",
    string? Description = null,
    IReadOnlyList<Trigger>? Triggers = null,
    IReadOnlyList<string>? Capabilities = null,
    string? InputTypeName = null,
    string? OutputTypeName = null,
    string? InputSchema = null,
    string? OutputSchema = null);
