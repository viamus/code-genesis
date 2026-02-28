using CodeGenesis.Engine.Claude;
using CodeGenesis.Engine.Config;
using CodeGenesis.Engine.Pipeline;
using CodeGenesis.Engine.Steps;
using FluentAssertions;
using NSubstitute;

namespace CodeGenesis.Engine.Tests.Steps;

public class DynamicStepTests
{
    private readonly IClaudeRunner _claude = Substitute.For<IClaudeRunner>();

    private static StepConfig MakeConfig(
        string name = "test-step",
        string prompt = "do something",
        string? outputKey = null,
        bool optional = false,
        string? failIf = null,
        string? failMessage = null) => new()
    {
        Name = name,
        Prompt = prompt,
        OutputKey = outputKey,
        Optional = optional,
        FailIf = failIf,
        FailMessage = failMessage
    };

    private static PipelineContext MakeContext() =>
        new() { TaskDescription = "test" };

    private DynamicStep MakeStep(
        StepConfig? config = null,
        string? model = null,
        RetryPolicy? retryPolicy = null) =>
        new(_claude, config ?? MakeConfig(), "resolved prompt", null, model, null, retryPolicy);

    #region Properties

    [Fact]
    public void Name_ReturnsStepConfigName()
    {
        var step = MakeStep(MakeConfig(name: "my-step"));

        step.Name.Should().Be("my-step");
    }

    [Fact]
    public void Description_ReturnsStepConfigDescription()
    {
        var config = MakeConfig();
        config.Description = "My description";
        var step = MakeStep(config);

        step.Description.Should().Be("My description");
    }

    [Fact]
    public void Description_FallsBackToName()
    {
        var step = MakeStep(MakeConfig(name: "fallback"));

        step.Description.Should().Be("fallback");
    }

    #endregion

    #region Success execution

    [Fact]
    public async Task ExecuteAsync_Success_ReturnsSuccessResult()
    {
        _claude.RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeResponse
            {
                Success = true,
                Result = "done!",
                InputTokens = 100,
                OutputTokens = 50,
                CostUsd = 0.01,
                Duration = TimeSpan.FromSeconds(2)
            });

        var step = MakeStep();
        var ctx = MakeContext();

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        result.Outcome.Should().Be(StepOutcome.Success);
        result.Output.Should().Be("done!");
        result.TokensUsed.Should().Be(150);
        result.CostUsd.Should().Be(0.01);
    }

    [Fact]
    public async Task ExecuteAsync_Success_AccumulatesMetricsInContext()
    {
        _claude.RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeResponse
            {
                Success = true,
                Result = "ok",
                InputTokens = 200,
                OutputTokens = 100,
                CostUsd = 0.05
            });

        var step = MakeStep();
        var ctx = MakeContext();

        await step.ExecuteAsync(ctx, CancellationToken.None);

        ctx.TotalInputTokens.Should().Be(200);
        ctx.TotalOutputTokens.Should().Be(100);
        ctx.TotalCostUsd.Should().Be(0.05);
    }

    [Fact]
    public async Task ExecuteAsync_WithOutputKey_StoresInStepOutputs()
    {
        _claude.RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeResponse
            {
                Success = true,
                Result = "plan output"
            });

        var step = MakeStep(MakeConfig(outputKey: "plan"));
        var ctx = MakeContext();

        await step.ExecuteAsync(ctx, CancellationToken.None);

        ctx.StepOutputs.Should().ContainKey("plan");
        ctx.StepOutputs["plan"].Should().Be("plan output");
    }

    [Fact]
    public async Task ExecuteAsync_WithOutputKey_NullResult_StoresEmptyString()
    {
        _claude.RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeResponse
            {
                Success = true,
                Result = null
            });

        var step = MakeStep(MakeConfig(outputKey: "result"));
        var ctx = MakeContext();

        await step.ExecuteAsync(ctx, CancellationToken.None);

        ctx.StepOutputs["result"].Should().BeEmpty();
    }

    #endregion

    #region FailIf condition

    [Fact]
    public async Task ExecuteAsync_FailIf_MatchesOutput_ReturnsFailed()
    {
        _claude.RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeResponse
            {
                Success = true,
                Result = "Found ERROR in the output"
            });

        var step = MakeStep(MakeConfig(failIf: "ERROR", failMessage: "Validation failed"));
        var ctx = MakeContext();

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        result.Outcome.Should().Be(StepOutcome.Failed);
        result.Error.Should().Be("Validation failed");
    }

    [Fact]
    public async Task ExecuteAsync_FailIf_CaseInsensitive()
    {
        _claude.RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeResponse
            {
                Success = true,
                Result = "found error in the output"
            });

        var step = MakeStep(MakeConfig(failIf: "ERROR"));
        var ctx = MakeContext();

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        result.Outcome.Should().Be(StepOutcome.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_FailIf_NoMatch_ReturnsSuccess()
    {
        _claude.RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeResponse
            {
                Success = true,
                Result = "All good, no issues"
            });

        var step = MakeStep(MakeConfig(failIf: "ERROR"));
        var ctx = MakeContext();

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        result.Outcome.Should().Be(StepOutcome.Success);
    }

    #endregion

    #region Failure paths

    [Fact]
    public async Task ExecuteAsync_NonTransientFailure_ReturnsFailedImmediately()
    {
        _claude.RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeResponse
            {
                Success = false,
                ErrorMessage = "max_turns reached",
                Duration = TimeSpan.FromSeconds(1)
            });

        var step = MakeStep(retryPolicy: new RetryPolicy(3, 1, 60, 5));
        var ctx = MakeContext();

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        result.Outcome.Should().Be(StepOutcome.Failed);
        result.Error.Should().Contain("max_turns");
        // Should NOT have retried for non-transient errors
        await _claude.Received(1).RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_OptionalStep_FailureReturnsSkipped()
    {
        _claude.RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeResponse
            {
                Success = false,
                ErrorMessage = "some error"
            });

        var step = MakeStep(MakeConfig(optional: true));
        var ctx = MakeContext();

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        result.Outcome.Should().Be(StepOutcome.Skipped);
        result.Error.Should().Be("some error");
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_RetriesWithBackoff()
    {
        var calls = 0;
        _claude.RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                if (calls <= 2)
                    return new ClaudeResponse
                    {
                        Success = false,
                        ExitCode = -1,
                        ErrorMessage = "Process timed out",
                        Duration = TimeSpan.FromSeconds(1)
                    };

                return new ClaudeResponse
                {
                    Success = true,
                    Result = "finally done"
                };
            });

        var step = MakeStep(retryPolicy: new RetryPolicy(MaxRetries: 3, BackoffSeconds: 0, RateLimitPauseSeconds: 0, MaxRateLimitPauses: 5));
        var ctx = MakeContext();

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        result.Outcome.Should().Be(StepOutcome.Success);
        calls.Should().Be(3); // 2 failures + 1 success
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_ExhaustsRetries_ReturnsFailed()
    {
        _claude.RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeResponse
            {
                Success = false,
                ExitCode = -1,
                ErrorMessage = "Process timed out",
                Duration = TimeSpan.FromSeconds(1)
            });

        var step = MakeStep(retryPolicy: new RetryPolicy(MaxRetries: 1, BackoffSeconds: 0, RateLimitPauseSeconds: 0, MaxRateLimitPauses: 5));
        var ctx = MakeContext();

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        result.Outcome.Should().Be(StepOutcome.Failed);
        // 1 initial attempt + 1 retry = 2 calls
        await _claude.Received(2).RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RateLimit_PausesAndRetries()
    {
        var calls = 0;
        _claude.RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                if (calls == 1)
                    return new ClaudeResponse
                    {
                        Success = false,
                        ExitCode = 1,
                        ErrorMessage = "429 too many requests",
                        Duration = TimeSpan.FromSeconds(1)
                    };

                return new ClaudeResponse
                {
                    Success = true,
                    Result = "ok after rate limit"
                };
            });

        var step = MakeStep(retryPolicy: new RetryPolicy(MaxRetries: 0, BackoffSeconds: 0, RateLimitPauseSeconds: 0, MaxRateLimitPauses: 3));
        var ctx = MakeContext();

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        result.Outcome.Should().Be(StepOutcome.Success);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_RateLimit_ExceedsMaxPauses_ReturnsFailed()
    {
        _claude.RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeResponse
            {
                Success = false,
                ExitCode = 1,
                ErrorMessage = "rate limit exceeded",
                Duration = TimeSpan.FromSeconds(1)
            });

        var step = MakeStep(retryPolicy: new RetryPolicy(MaxRetries: 0, BackoffSeconds: 0, RateLimitPauseSeconds: 0, MaxRateLimitPauses: 1));
        var ctx = MakeContext();

        var result = await step.ExecuteAsync(ctx, CancellationToken.None);

        result.Outcome.Should().Be(StepOutcome.Failed);
        // 1 initial + 1 rate limit pause then fail = 2 calls
        await _claude.Received(2).RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Clone

    [Fact]
    public void Clone_ReturnsIndependentCopy()
    {
        var step = MakeStep(MakeConfig(name: "original"));

        var clone = step.Clone();

        clone.Name.Should().Be("original");
        clone.UpdateResolvedPrompt("updated prompt");

        // Original should be unaffected
        step.OriginalPromptTemplate.Should().Be("do something");
    }

    #endregion

    #region Request construction

    [Fact]
    public async Task ExecuteAsync_PassesModelToRequest()
    {
        _claude.RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeResponse { Success = true, Result = "ok" });

        var step = MakeStep(model: "claude-sonnet-4-6");
        var ctx = MakeContext();

        await step.ExecuteAsync(ctx, CancellationToken.None);

        await _claude.Received(1).RunAsync(
            Arg.Is<ClaudeRequest>(r => r.Model == "claude-sonnet-4-6"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PassesWorkingDirectoryFromContext()
    {
        _claude.RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeResponse { Success = true, Result = "ok" });

        var step = MakeStep();
        var ctx = new PipelineContext
        {
            TaskDescription = "test",
            WorkingDirectory = "/my/project"
        };

        await step.ExecuteAsync(ctx, CancellationToken.None);

        await _claude.Received(1).RunAsync(
            Arg.Is<ClaudeRequest>(r => r.WorkingDirectory == "/my/project"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PassesAllowedTools()
    {
        _claude.RunAsync(Arg.Any<ClaudeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeResponse { Success = true, Result = "ok" });

        var config = MakeConfig();
        config.AllowedTools = ["Bash", "Read", "Write"];
        var step = MakeStep(config);
        var ctx = MakeContext();

        await step.ExecuteAsync(ctx, CancellationToken.None);

        await _claude.Received(1).RunAsync(
            Arg.Is<ClaudeRequest>(r =>
                r.AllowedTools.Count == 3 &&
                r.AllowedTools.Contains("Bash")),
            Arg.Any<CancellationToken>());
    }

    #endregion
}
