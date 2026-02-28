using CodeGenesis.Engine.Pipeline;
using FluentAssertions;

namespace CodeGenesis.Engine.Tests.Pipeline;

public class StepResultTests
{
    [Fact]
    public void SuccessResult_HasCorrectProperties()
    {
        var result = new StepResult
        {
            Outcome = StepOutcome.Success,
            Output = "done",
            Duration = TimeSpan.FromSeconds(5),
            TokensUsed = 150,
            CostUsd = 0.01
        };

        result.Outcome.Should().Be(StepOutcome.Success);
        result.Output.Should().Be("done");
        result.Error.Should().BeNull();
        result.Duration.Should().Be(TimeSpan.FromSeconds(5));
        result.TokensUsed.Should().Be(150);
        result.CostUsd.Should().Be(0.01);
    }

    [Fact]
    public void FailedResult_HasErrorSet()
    {
        var result = new StepResult
        {
            Outcome = StepOutcome.Failed,
            Error = "something went wrong"
        };

        result.Outcome.Should().Be(StepOutcome.Failed);
        result.Error.Should().Be("something went wrong");
        result.Output.Should().BeNull();
    }

    [Fact]
    public void SkippedResult_IsValid()
    {
        var result = new StepResult
        {
            Outcome = StepOutcome.Skipped,
            Error = "optional step failed"
        };

        result.Outcome.Should().Be(StepOutcome.Skipped);
    }

    [Fact]
    public void Record_Equality_WorksCorrectly()
    {
        var a = new StepResult
        {
            Outcome = StepOutcome.Success,
            Output = "done",
            Duration = TimeSpan.FromSeconds(1),
            TokensUsed = 100,
            CostUsd = 0.01
        };
        var b = new StepResult
        {
            Outcome = StepOutcome.Success,
            Output = "done",
            Duration = TimeSpan.FromSeconds(1),
            TokensUsed = 100,
            CostUsd = 0.01
        };

        a.Should().Be(b);
    }

    [Fact]
    public void Record_Inequality_DifferentOutcome()
    {
        var a = new StepResult { Outcome = StepOutcome.Success };
        var b = new StepResult { Outcome = StepOutcome.Failed };

        a.Should().NotBe(b);
    }

    [Fact]
    public void StepOutcome_HasExpectedValues()
    {
        Enum.GetValues<StepOutcome>().Should().Equal(
            StepOutcome.Success,
            StepOutcome.Failed,
            StepOutcome.Skipped);
    }
}
