using Emberdrop.Plugins;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace Emberdrop.WorkUnits;

/// <summary>
/// What the host learns, by reflection and without running any plugin code, about a work unit class's typed input and output:
/// the type names and their JSON Schemas. Only text leaves this class, so nothing in the result references the plugin's types.
/// </summary>
internal sealed record WorkUnitTypeInfo(string? InputTypeName, string? OutputTypeName, string? InputSchema, string? OutputSchema)
{
    public static WorkUnitTypeInfo For(Type workUnit)
    {
        var (input, output) = TypesOf(workUnit);
        return new WorkUnitTypeInfo(input?.FullName, output?.FullName, SchemaOf(input), SchemaOf(output));
    }

    /// <summary>
    /// Finds the closed <c>WorkUnit&lt;TInput, TOutput&gt;</c> the class derives from. <see cref="NoInput"/> and <see cref="NoOutput"/>
    /// count as none, and so does a class that implements <see cref="IWorkUnit"/> directly (its input is then an untyped JSON string).
    /// </summary>
    internal static (Type? Input, Type? Output) TypesOf(Type workUnit)
    {
        for (var type = workUnit.BaseType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(WorkUnit<,>))
            {
                var arguments = type.GetGenericArguments();
                return (arguments[0] == typeof(NoInput) ? null : arguments[0], arguments[1] == typeof(NoOutput) ? null : arguments[1]);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Strictly reads <paramref name="json"/> as the input type of <paramref name="workUnit"/>, the way <c>WorkUnit&lt;TInput, TOutput&gt;</c>
    /// will. Returns an error message fit to show to a user, or <see langword="null"/> when the input is acceptable.
    /// </summary>
    public static string? ValidateInput(Type workUnit, string workUnitName, string? json)
    {
        var (input, _) = TypesOf(workUnit);
        if (input is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return $"Work unit '{workUnitName}' requires input of type {input.Name}, but none was given.";
        }

        try
        {
            // Fresh options, dropped afterwards: see WorkJson.CreateOptions.
            return JsonSerializer.Deserialize(json, input, WorkJson.CreateOptions()) is null
                ? $"The input of work unit '{workUnitName}' is JSON null; it requires {input.Name}."
                : null;
        }
        catch (JsonException ex)
        {
            return $"The input of work unit '{workUnitName}' is not a valid {input.Name}: {ex.Message}";
        }
    }

    private static string? SchemaOf(Type? type)
    {
        if (type is null)
        {
            return null;
        }

        JsonNode schema = JsonSchemaExporter.GetJsonSchemaAsNode(WorkJson.CreateOptions(), type, new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true });
        return schema.ToJsonString();
    }
}
