using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Emberdrop.Core.Tests;

public class WorkUnitShould
{
    private static WorkContext ContextWith(string? input) => new()
    {
        ExecutionId = "e1",
        WorkUnitName = "adder",
        Trigger = ManualTrigger.Instance,
        Logger = NullLogger.Instance,
        Services = new ServiceCollection().BuildServiceProvider(),
        Configuration = new Dictionary<string, string?>(),
        Input = input,
    };

    [Fact]
    public async Task ReadTypedInputAndWriteTypedOutputAsJson()
    {
        var result = await new Adder().ExecuteAsync(ContextWith("""{"left":2,"right":3}"""), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.OutputJson.Should().Be("""{"sum":5}""");
    }

    [Fact]
    public async Task ReadInputCaseInsensitively()
    {
        var result = await new Adder().ExecuteAsync(ContextWith("""{"Left":1,"RIGHT":1}"""), CancellationToken.None);

        result.OutputJson.Should().Be("""{"sum":2}""");
    }

    [Fact]
    public async Task RefuseMissingInputWhenAnInputTypeIsDeclared()
    {
        var act = () => new Adder().ExecuteAsync(ContextWith(null), CancellationToken.None);

        (await act.Should().ThrowAsync<WorkInputException>()).WithMessage("*'adder'*requires input*AddInput*");
    }

    [Fact]
    public async Task RefuseInputThatDoesNotFitTheInputType()
    {
        var act = () => new Adder().ExecuteAsync(ContextWith("""{"left":"two","right":3}"""), CancellationToken.None);

        (await act.Should().ThrowAsync<WorkInputException>()).WithMessage("*'adder'*not a valid AddInput*");
    }

    [Fact]
    public async Task RefuseInputWithPropertiesTheTypeDoesNotHave()
    {
        var act = () => new Adder().ExecuteAsync(ContextWith("""{"left":1,"right":2,"extra":true}"""), CancellationToken.None);

        (await act.Should().ThrowAsync<WorkInputException>()).WithMessage("*extra*");
    }

    [Fact]
    public async Task NeedNoInputWhenTheInputTypeIsNoInput()
    {
        var result = await new Ping().ExecuteAsync(ContextWith(null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.OutputJson.Should().BeNull("NoOutput produces no payload");
    }

    private sealed record AddInput(int Left, int Right);

    private sealed record AddOutput(int Sum);

    private sealed class Adder : WorkUnit<AddInput, AddOutput>
    {
        protected override Task<AddOutput> ExecuteAsync(AddInput input, WorkContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new AddOutput(input.Left + input.Right));
    }

    private sealed class Ping : WorkUnit<NoInput, NoOutput>
    {
        protected override Task<NoOutput> ExecuteAsync(NoInput input, WorkContext context, CancellationToken cancellationToken) =>
            Task.FromResult(NoOutput.Instance);
    }
}
