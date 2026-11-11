using Emberdrop.Plugins;
using System.Text.Json;

namespace Emberdrop.WorkUnits;

/// <summary>Interprets the triggers a manifest declares with the trigger kinds a host knows.</summary>
internal static class ManifestTriggers
{
    public static IReadOnlyList<Trigger> Read(PluginWorkUnitManifest workUnit, TriggerKindRegistry kinds, List<string> problems)
    {
        var result = new List<Trigger>();
        var index = 0;
        foreach (var element in workUnit.Triggers)
        {
            var label = $"Work unit '{workUnit.Name}' triggers[{index++}]";
            try
            {
                var trigger = kinds.FromJson(element);
                if (trigger is OneOffTrigger)
                {
                    problems.Add($"{label}: a one-off trigger is a single point in time and cannot be a default that ships with a plugin; use a recurring or manual trigger.");
                }
                else if (!result.Contains(trigger))
                {
                    result.Add(trigger);
                }
            }
            catch (FormatException ex)
            {
                problems.Add($"{label}: {ex.Message}");
            }
            catch (JsonException ex)
            {
                problems.Add($"{label}: {ex.Message}");
            }
        }

        if (result.OfType<RecurringTrigger>().Count() > 1)
        {
            problems.Add($"Work unit '{workUnit.Name}' declares more than one recurring trigger; v0.1 supports one default schedule per work unit. Operators can add more.");
        }

        return result;
    }
}
