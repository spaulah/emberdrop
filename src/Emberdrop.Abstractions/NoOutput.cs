namespace Emberdrop;

/// <summary>The output type of a work unit that produces no output.</summary>
public sealed record NoOutput
{
    /// <summary>The only value.</summary>
    public static NoOutput Instance { get; } = new();
}
