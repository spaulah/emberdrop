namespace Emberdrop.Providers.ContractTests;

public static class Eventually
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Polls until <paramref name="condition"/> holds; fails the test with <paramref name="what"/> on timeout.</summary>
    public static async Task Async(Func<Task<bool>> condition, string what, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new Xunit.Sdk.XunitException($"Timed out waiting for: {what}");
            }

            await Task.Delay(25);
        }
    }

    public static Task Async(Func<bool> condition, string what, TimeSpan? timeout = null) =>
        Async(() => Task.FromResult(condition()), what, timeout);
}
