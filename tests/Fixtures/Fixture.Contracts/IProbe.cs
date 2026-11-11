namespace Fixture.Contracts;

/// <summary>
/// A contract shared between the test host and fixture plugins, standing in for any extra contract
/// assembly an application adds to the shared-assembly list. The host can only call a plugin through it
/// if both sides see the very same type.
/// </summary>
public interface IProbe
{
    string Describe();
}

/// <summary>
/// A host-side service that is registered in the host but is deliberately not on any plugin's allow-list, unless a test adds it.
/// It lives in the shared contract assembly so a plugin could ask for it by type if it were offered.
/// </summary>
public interface IHostOnlyService
{
    string Secret { get; }
}
