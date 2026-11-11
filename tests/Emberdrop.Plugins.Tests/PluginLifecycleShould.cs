using Emberdrop.Plugins;
using Emberdrop.WorkUnits;
using FluentAssertions;

namespace Emberdrop.Plugins.Tests;

public class PluginLifecycleShould
{
    /// <summary>The state machine written out in full, so a change to the table has to be a deliberate change here too.</summary>
    private static readonly Dictionary<PluginState, PluginState[]> Expected = new()
    {
        [PluginState.Staged] = [PluginState.Validated, PluginState.Failed, PluginState.Quarantined],
        [PluginState.Validated] = [PluginState.Active, PluginState.Failed, PluginState.Quarantined],
        [PluginState.Active] = [PluginState.Draining],
        [PluginState.Draining] = [PluginState.Unloading],
        [PluginState.Unloading] = [PluginState.Unloaded],
        [PluginState.Unloaded] = [],
        [PluginState.Failed] = [PluginState.Quarantined],
        [PluginState.Quarantined] = [],
    };

    public static TheoryData<PluginState, PluginState> AllPairs()
    {
        var data = new TheoryData<PluginState, PluginState>();
        foreach (var from in Enum.GetValues<PluginState>())
        {
            foreach (var to in Enum.GetValues<PluginState>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void AllowExactlyTheTransitionsOfTheDesign(PluginState from, PluginState to)
    {
        PluginLifecycle.CanMove(from, to).Should().Be(Expected[from].Contains(to), $"{from} -> {to}");
    }

    [Fact]
    public void CoverEveryState()
    {
        Expected.Keys.Should().BeEquivalentTo(Enum.GetValues<PluginState>());
    }

    [Fact]
    public void LetTheOnlyWayOutOfActiveBeDraining()
    {
        PluginLifecycle.CanMove(PluginState.Active, PluginState.Unloading).Should().BeFalse("a version must drain before it unloads");
        PluginLifecycle.CanMove(PluginState.Active, PluginState.Unloaded).Should().BeFalse();
        PluginLifecycle.CanMove(PluginState.Draining, PluginState.Active).Should().BeFalse("a retiring version is never revived");
    }

    [Theory]
    [InlineData(PluginState.Staged, true)]
    [InlineData(PluginState.Validated, true)]
    [InlineData(PluginState.Active, true)]
    [InlineData(PluginState.Draining, true)]
    [InlineData(PluginState.Unloading, true)]
    [InlineData(PluginState.Unloaded, false)]
    [InlineData(PluginState.Failed, false)]
    [InlineData(PluginState.Quarantined, false)]
    public void KnowWhichStatesStillOccupyAVersionNumber(PluginState state, bool live)
    {
        PluginLifecycle.IsLive(state).Should().Be(live);
        PluginLifecycle.IsTerminal(state).Should().Be(!live);
    }
}
