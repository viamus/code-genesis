using CodeGenesis.Engine.Config;
using FluentAssertions;

namespace CodeGenesis.Engine.Tests.Config;

public class StepEntryTests
{
    [Fact]
    public void IsSimpleStep_WithNameOnly_ReturnsTrue()
    {
        var entry = new StepEntry { Name = "step1", Prompt = "do something" };

        entry.IsSimpleStep.Should().BeTrue();
        entry.IsForeach.Should().BeFalse();
        entry.IsParallel.Should().BeFalse();
        entry.IsParallelForeach.Should().BeFalse();
        entry.IsApproval.Should().BeFalse();
    }

    [Fact]
    public void IsForeach_WithForeachConfig_ReturnsTrue()
    {
        var entry = new StepEntry
        {
            Foreach = new ForeachConfig { Collection = "a,b,c" }
        };

        entry.IsForeach.Should().BeTrue();
        entry.IsSimpleStep.Should().BeFalse();
    }

    [Fact]
    public void IsParallel_WithParallelConfig_ReturnsTrue()
    {
        var entry = new StepEntry
        {
            Parallel = new ParallelConfig()
        };

        entry.IsParallel.Should().BeTrue();
        entry.IsSimpleStep.Should().BeFalse();
    }

    [Fact]
    public void IsParallelForeach_WithConfig_ReturnsTrue()
    {
        var entry = new StepEntry
        {
            ParallelForeach = new ParallelForeachConfig { Collection = "x,y" }
        };

        entry.IsParallelForeach.Should().BeTrue();
        entry.IsSimpleStep.Should().BeFalse();
    }

    [Fact]
    public void IsApproval_WithConfig_ReturnsTrue()
    {
        var entry = new StepEntry
        {
            Approval = new ApprovalConfig { Name = "review" }
        };

        entry.IsApproval.Should().BeTrue();
        entry.IsSimpleStep.Should().BeFalse();
    }

    [Fact]
    public void IsUsePipeline_WithUsePipeline_ReturnsTrue()
    {
        var entry = new StepEntry
        {
            Name = "sub",
            UsePipeline = "./child.yml"
        };

        entry.IsUsePipeline.Should().BeTrue();
        entry.IsSimpleStep.Should().BeFalse();
        entry.IsForeach.Should().BeFalse();
        entry.IsParallel.Should().BeFalse();
    }

    [Fact]
    public void IsUsePipeline_WithoutUsePipeline_ReturnsFalse()
    {
        var entry = new StepEntry { Name = "step1", Prompt = "do something" };

        entry.IsUsePipeline.Should().BeFalse();
    }

    [Fact]
    public void IsSimpleStep_WithUsePipeline_ReturnsFalse()
    {
        var entry = new StepEntry
        {
            Name = "sub",
            UsePipeline = "./child.yml"
        };

        entry.IsSimpleStep.Should().BeFalse();
    }

    [Fact]
    public void IsSimpleStep_NullNameNoComposite_ReturnsFalse()
    {
        var entry = new StepEntry { Prompt = "orphan prompt" };

        entry.IsSimpleStep.Should().BeFalse();
    }

    [Fact]
    public void ToStepConfig_MapsAllFields()
    {
        var entry = new StepEntry
        {
            Name = "build",
            Agent = "coder",
            Description = "Build the app",
            SystemPrompt = "You are a builder",
            Prompt = "Build it",
            Model = "claude-sonnet-4-6",
            Context = "./agents/builder",
            MaxTurns = 10,
            OutputKey = "build_result",
            AllowedTools = ["Bash", "Read"],
            Optional = true,
            FailIf = "ERROR",
            FailMessage = "Build failed",
            RetryMax = 3,
            RetryBackoffSeconds = 15,
            RateLimitPauseSeconds = 30,
            RateLimitMaxPauses = 2
        };

        var config = entry.ToStepConfig();

        config.Name.Should().Be("build");
        config.Agent.Should().Be("coder");
        config.Description.Should().Be("Build the app");
        config.SystemPrompt.Should().Be("You are a builder");
        config.Prompt.Should().Be("Build it");
        config.Model.Should().Be("claude-sonnet-4-6");
        config.Context.Should().Be("./agents/builder");
        config.MaxTurns.Should().Be(10);
        config.OutputKey.Should().Be("build_result");
        config.AllowedTools.Should().Equal("Bash", "Read");
        config.Optional.Should().BeTrue();
        config.FailIf.Should().Be("ERROR");
        config.FailMessage.Should().Be("Build failed");
        config.RetryMax.Should().Be(3);
        config.RetryBackoffSeconds.Should().Be(15);
        config.RateLimitPauseSeconds.Should().Be(30);
        config.RateLimitMaxPauses.Should().Be(2);
    }

    [Fact]
    public void ToStepConfig_NullFields_DefaultsCorrectly()
    {
        var entry = new StepEntry { Name = "minimal", Prompt = "go" };

        var config = entry.ToStepConfig();

        config.Name.Should().Be("minimal");
        config.Prompt.Should().Be("go");
        config.Agent.Should().BeNull();
        config.Description.Should().BeNull();
        config.SystemPrompt.Should().BeNull();
        config.Model.Should().BeNull();
        config.MaxTurns.Should().BeNull();
        config.OutputKey.Should().BeNull();
        config.AllowedTools.Should().BeNull();
        config.McpServers.Should().BeNull();
        config.Optional.Should().BeFalse();
        config.FailIf.Should().BeNull();
        config.RetryMax.Should().BeNull();
    }
}
