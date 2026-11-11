namespace Emberdrop;

/// <summary>The input type of a work unit that takes no input.</summary>
public sealed record NoInput
{
    /// <summary>The only value.</summary>
    public static NoInput Instance { get; } = new();
}
