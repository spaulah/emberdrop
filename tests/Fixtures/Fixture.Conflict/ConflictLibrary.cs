using System.Runtime.Loader;

namespace Fixture.Conflict;

/// <summary>A library that exists in two incompatible versions. It reports which copy is actually running.</summary>
public static class ConflictLibrary
{
    public static string Describe()
    {
        var assembly = typeof(ConflictLibrary).Assembly;
        var context = AssemblyLoadContext.GetLoadContext(assembly)?.Name;
        return $"Fixture.Conflict {assembly.GetName().Version} in context '{context}'";
    }
}
