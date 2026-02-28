using CodeGenesis.Engine.Pipeline;
using FluentAssertions;

namespace CodeGenesis.Engine.Tests.Pipeline;

public class PipelineContextTests
{
    [Fact]
    public void Constructor_InitializesWithRequiredTaskDescription()
    {
        var ctx = new PipelineContext { TaskDescription = "Build something" };

        ctx.TaskDescription.Should().Be("Build something");
        ctx.WorkingDirectory.Should().BeNull();
        ctx.StepOutputs.Should().BeEmpty();
    }

    [Fact]
    public void Metrics_DefaultToZero()
    {
        var ctx = new PipelineContext { TaskDescription = "test" };

        ctx.TotalInputTokens.Should().Be(0);
        ctx.TotalOutputTokens.Should().Be(0);
        ctx.TotalCostUsd.Should().Be(0);
        ctx.TotalDuration.Should().Be(TimeSpan.Zero);
        ctx.StepsCompleted.Should().Be(0);
        ctx.StepsFailed.Should().Be(0);
    }

    [Fact]
    public void FailureInfo_DefaultsToNull()
    {
        var ctx = new PipelineContext { TaskDescription = "test" };

        ctx.FailedStepName.Should().BeNull();
        ctx.FailureReason.Should().BeNull();
    }

    [Fact]
    public void StepOutputs_CanStoreAndRetrieveValues()
    {
        var ctx = new PipelineContext { TaskDescription = "test" };

        ctx.StepOutputs["plan"] = "my plan output";
        ctx.StepOutputs["code"] = "my code output";

        ctx.StepOutputs.Should().HaveCount(2);
        ctx.StepOutputs["plan"].Should().Be("my plan output");
        ctx.StepOutputs["code"].Should().Be("my code output");
    }

    [Fact]
    public void Metrics_CanBeAccumulated()
    {
        var ctx = new PipelineContext { TaskDescription = "test" };

        ctx.TotalInputTokens += 100;
        ctx.TotalOutputTokens += 50;
        ctx.TotalCostUsd += 0.01;
        ctx.StepsCompleted++;

        ctx.TotalInputTokens += 200;
        ctx.TotalOutputTokens += 75;
        ctx.TotalCostUsd += 0.02;
        ctx.StepsCompleted++;

        ctx.TotalInputTokens.Should().Be(300);
        ctx.TotalOutputTokens.Should().Be(125);
        ctx.TotalCostUsd.Should().BeApproximately(0.03, 0.0001);
        ctx.StepsCompleted.Should().Be(2);
    }

    [Fact]
    public void StatusUpdate_CanBeSetAndInvoked()
    {
        var capturedMessage = "";
        var ctx = new PipelineContext
        {
            TaskDescription = "test",
            StatusUpdate = msg => capturedMessage = msg
        };

        ctx.StatusUpdate?.Invoke("Working on step 1...");

        capturedMessage.Should().Be("Working on step 1...");
    }
}
