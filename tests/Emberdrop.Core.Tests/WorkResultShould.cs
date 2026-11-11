using FluentAssertions;

namespace Emberdrop.Core.Tests;

public class WorkResultShould
{
    [Fact]
    public void ReportSuccessWithoutAnOutput()
    {
        var result = WorkResult.Succeeded();

        (result.IsSuccess, result.OutputJson, result.ErrorCode, result.ErrorMessage).Should().Be((true, null, null, null));
    }

    [Fact]
    public void CarryAnOutputAsCamelCaseJson()
    {
        WorkResult.Succeeded(new { SentCount = 3 }).OutputJson.Should().Be("""{"sentCount":3}""");
    }

    [Fact]
    public void ReportAFailureWithACodeAndAMessage()
    {
        var result = WorkResult.Failed("smtp-unreachable", "Could not reach smtp.example.org.");

        (result.IsSuccess, result.OutputJson, result.ErrorCode, result.ErrorMessage)
            .Should().Be((false, null, "smtp-unreachable", "Could not reach smtp.example.org."));
    }

    [Theory]
    [InlineData("", "message")]
    [InlineData("code", " ")]
    public void RefuseAFailureWithoutACodeOrMessage(string code, string message)
    {
        var act = () => WorkResult.Failed(code, message);

        act.Should().Throw<ArgumentException>();
    }
}
