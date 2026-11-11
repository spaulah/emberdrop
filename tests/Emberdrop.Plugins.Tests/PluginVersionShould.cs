using Emberdrop.Plugins;
using Emberdrop.WorkUnits;
using FluentAssertions;

namespace Emberdrop.Plugins.Tests;

public class PluginVersionShould
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, "", "")]
    [InlineData("0.0.0", 0, 0, 0, "", "")]
    [InlineData("10.20.30-beta.1", 10, 20, 30, "beta.1", "")]
    [InlineData("1.0.0+build.5", 1, 0, 0, "", "build.5")]
    [InlineData("1.0.0-rc.1+sha.abc", 1, 0, 0, "rc.1", "sha.abc")]
    public void ParseValidVersions(string text, int major, int minor, int patch, string prerelease, string build)
    {
        var version = PluginVersion.Parse(text);

        (version.Major, version.Minor, version.Patch, version.Prerelease, version.Build).Should().Be((major, minor, patch, prerelease, build));
        version.ToString().Should().Be(text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3+")]
    [InlineData("a.b.c")]
    [InlineData("1.2.-3")]
    public void RejectInvalidVersions(string text)
    {
        PluginVersion.TryParse(text, out _).Should().BeFalse();
        var act = () => PluginVersion.Parse(text);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void OrderBySemanticVersioningPrecedence()
    {
        // The ordering example from the Semantic Versioning 2.0 specification.
        var ordered = new[]
        {
            "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0",
        }.Select(PluginVersion.Parse).ToList();

        ordered.Should().BeInAscendingOrder();
        for (var i = 0; i < ordered.Count - 1; i++)
        {
            (ordered[i] < ordered[i + 1]).Should().BeTrue($"{ordered[i]} < {ordered[i + 1]}");
        }
    }

    [Fact]
    public void IgnoreBuildMetadataWhenComparing()
    {
        var left = PluginVersion.Parse("1.0.0+a");
        var right = PluginVersion.Parse("1.0.0+b");

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
        (left == right).Should().BeTrue();
    }
}
