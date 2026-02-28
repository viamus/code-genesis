using CodeGenesis.Engine.Config;
using CodeGenesis.Engine.Pipeline;
using FluentAssertions;

namespace CodeGenesis.Engine.Tests.Pipeline;

public class RetryPolicyTests
{
    [Fact]
    public void Resolve_NoOverrides_ReturnsDefaults()
    {
        var step = new StepConfig { Name = "test", Prompt = "test" };

        var policy = RetryPolicy.Resolve(step, null);

        policy.MaxRetries.Should().Be(0);
        policy.BackoffSeconds.Should().Be(10);
        policy.RateLimitPauseSeconds.Should().Be(60);
        policy.MaxRateLimitPauses.Should().Be(5);
    }

    [Fact]
    public void Resolve_GlobalSettings_OverridesDefaults()
    {
        var step = new StepConfig { Name = "test", Prompt = "test" };
        var global = new PipelineSettings
        {
            RetryMax = 3,
            RetryBackoffSeconds = 20,
            RateLimitPauseSeconds = 120,
            RateLimitMaxPauses = 10
        };

        var policy = RetryPolicy.Resolve(step, global);

        policy.MaxRetries.Should().Be(3);
        policy.BackoffSeconds.Should().Be(20);
        policy.RateLimitPauseSeconds.Should().Be(120);
        policy.MaxRateLimitPauses.Should().Be(10);
    }

    [Fact]
    public void Resolve_StepSettings_OverridesGlobal()
    {
        var step = new StepConfig
        {
            Name = "test",
            Prompt = "test",
            RetryMax = 5,
            RetryBackoffSeconds = 30,
            RateLimitPauseSeconds = 90,
            RateLimitMaxPauses = 2
        };
        var global = new PipelineSettings
        {
            RetryMax = 3,
            RetryBackoffSeconds = 20,
            RateLimitPauseSeconds = 120,
            RateLimitMaxPauses = 10
        };

        var policy = RetryPolicy.Resolve(step, global);

        policy.MaxRetries.Should().Be(5);
        policy.BackoffSeconds.Should().Be(30);
        policy.RateLimitPauseSeconds.Should().Be(90);
        policy.MaxRateLimitPauses.Should().Be(2);
    }

    [Fact]
    public void Resolve_PartialStepOverride_FallsBackToGlobalThenDefaults()
    {
        var step = new StepConfig
        {
            Name = "test",
            Prompt = "test",
            RetryMax = 2
        };
        var global = new PipelineSettings
        {
            RetryBackoffSeconds = 15
        };

        var policy = RetryPolicy.Resolve(step, global);

        policy.MaxRetries.Should().Be(2);        // step
        policy.BackoffSeconds.Should().Be(15);    // global
        policy.RateLimitPauseSeconds.Should().Be(60); // default
        policy.MaxRateLimitPauses.Should().Be(5);     // default
    }
}
