using Emberdrop.Plugins;
using Emberdrop.WorkUnits;
using FluentAssertions;

namespace Emberdrop.Plugins.Tests;

public class PluginVersionConstraintShould
{
    [Theory]
    [InlineData(null, "*")]
    [InlineData("", "*")]
    [InlineData("  ", "*")]
    [InlineData("*", "*")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta.1", "1.2.3-beta.1")]
    [InlineData("^1", "^1.0.0")]
    [InlineData("^1.2", "^1.2.0")]
    [InlineData("^1.2.3", "^1.2.3")]
    public void ParseAndRenderCanonically(string? text, string canonical)
    {
        var constraint = PluginVersionConstraint.Parse(text);

        constraint.ToString().Should().Be(canonical);
        PluginVersionConstraint.Parse(canonical).Should().Be(constraint, "the canonical form parses back to the same constraint");
    }

    [Theory]
    [InlineData("^")]
    [InlineData("^a")]
    [InlineData("^1.2.3.4")]
    [InlineData("~1.2")]
    [InlineData(">=1.0")]
    [InlineData("1.2")]
    [InlineData("latest")]
    public void RejectWhatItDoesNotUnderstand(string text)
    {
        PluginVersionConstraint.TryParse(text, out _).Should().BeFalse();
        FluentActions.Invoking(() => PluginVersionConstraint.Parse(text)).Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("*", "0.0.1", true)]
    [InlineData("*", "9.9.9", true)]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.4", false)]
    [InlineData("1.2.3", "1.2.3+build", true)]
    [InlineData("^1.2.3", "1.2.3", true)]
    [InlineData("^1.2.3", "1.9.0", true)]
    [InlineData("^1.2.3", "1.2.2", false)]
    [InlineData("^1.2.3", "2.0.0", false)]
    [InlineData("^1.2.3", "0.9.0", false)]
    [InlineData("^1", "1.0.0", true)]
    [InlineData("^1", "1.99.99", true)]
    [InlineData("^1", "2.0.0-alpha", false)]
    public void SelectTheRightVersions(string constraint, string version, bool expected)
    {
        PluginVersionConstraint.Parse(constraint).IsSatisfiedBy(PluginVersion.Parse(version)).Should().Be(expected);
    }
}
