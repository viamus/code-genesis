using CodeGenesis.Engine.Claude;
using FluentAssertions;

namespace CodeGenesis.Engine.Tests.Claude;

public class ClaudeResponseTests
{
    private static readonly TimeSpan TestDuration = TimeSpan.FromSeconds(5);

    #region FailureKind

    [Fact]
    public void FailureKind_SuccessResponse_ReturnsNone()
    {
        var response = new ClaudeResponse { Success = true };

        response.FailureKind.Should().Be(ClaudeFailureKind.None);
    }

    [Theory]
    [InlineData("timed out waiting for response")]
    [InlineData("Process timed out")]
    public void FailureKind_TimeoutWithExitCodeMinus1_ReturnsTimeout(string errorMsg)
    {
        var response = new ClaudeResponse
        {
            Success = false,
            ExitCode = -1,
            ErrorMessage = errorMsg
        };

        response.FailureKind.Should().Be(ClaudeFailureKind.Timeout);
    }

    [Fact]
    public void FailureKind_TimeoutWithoutExitCodeMinus1_IsNotTimeout()
    {
        var response = new ClaudeResponse
        {
            Success = false,
            ExitCode = 1,
            ErrorMessage = "timed out"
        };

        response.FailureKind.Should().NotBe(ClaudeFailureKind.Timeout);
    }

    [Theory]
    [InlineData("rate limit exceeded")]
    [InlineData("429 Too Many Requests")]
    [InlineData("server overloaded")]
    [InlineData("too many requests")]
    public void FailureKind_RateLimitMessages_ReturnsRateLimit(string errorMsg)
    {
        var response = new ClaudeResponse
        {
            Success = false,
            ExitCode = 1,
            ErrorMessage = errorMsg
        };

        response.FailureKind.Should().Be(ClaudeFailureKind.RateLimit);
    }

    [Fact]
    public void FailureKind_MaxTurnsMessage_ReturnsMaxTurns()
    {
        var response = new ClaudeResponse
        {
            Success = false,
            ExitCode = 1,
            ErrorMessage = "max_turns reached"
        };

        response.FailureKind.Should().Be(ClaudeFailureKind.MaxTurns);
    }

    [Fact]
    public void FailureKind_GenericError_ReturnsOther()
    {
        var response = new ClaudeResponse
        {
            Success = false,
            ExitCode = 1,
            ErrorMessage = "something unexpected"
        };

        response.FailureKind.Should().Be(ClaudeFailureKind.Other);
    }

    [Fact]
    public void FailureKind_NullErrorMessage_ReturnsOther()
    {
        var response = new ClaudeResponse
        {
            Success = false,
            ExitCode = 1,
            ErrorMessage = null
        };

        response.FailureKind.Should().Be(ClaudeFailureKind.Other);
    }

    #endregion

    #region FromJson

    [Fact]
    public void FromJson_SuccessResponse_ParsesCorrectly()
    {
        var json = """
        {
            "result": "Hello, world!",
            "usage": {
                "input_tokens": 100,
                "output_tokens": 50
            },
            "total_cost_usd": 0.005,
            "num_turns": 1
        }
        """;

        var response = ClaudeResponse.FromJson(json, TestDuration);

        response.Success.Should().BeTrue();
        response.Result.Should().Be("Hello, world!");
        response.InputTokens.Should().Be(100);
        response.OutputTokens.Should().Be(50);
        response.CostUsd.Should().Be(0.005);
        response.Duration.Should().Be(TestDuration);
        response.ExitCode.Should().Be(0);
    }

    [Fact]
    public void FromJson_WithCacheTokens_IncludesInInputTotal()
    {
        var json = """
        {
            "result": "cached response",
            "usage": {
                "input_tokens": 100,
                "cache_creation_input_tokens": 50,
                "cache_read_input_tokens": 25,
                "output_tokens": 30
            }
        }
        """;

        var response = ClaudeResponse.FromJson(json, TestDuration);

        response.Success.Should().BeTrue();
        response.InputTokens.Should().Be(175); // 100 + 50 + 25
        response.OutputTokens.Should().Be(30);
    }

    [Fact]
    public void FromJson_ErrorMaxTurns_ReturnsFailure()
    {
        var json = """
        {
            "subtype": "error_max_turns",
            "result": "max turns exceeded",
            "usage": {
                "input_tokens": 500,
                "output_tokens": 200
            },
            "num_turns": 5
        }
        """;

        var response = ClaudeResponse.FromJson(json, TestDuration);

        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Contain("max_turns limit");
        response.ErrorMessage.Should().Contain("5 turn(s)");
        response.InputTokens.Should().Be(500);
        response.OutputTokens.Should().Be(200);
    }

    [Fact]
    public void FromJson_IsError_ReturnsFailure()
    {
        var json = """
        {
            "is_error": true,
            "result": "Something broke",
            "usage": {
                "input_tokens": 10,
                "output_tokens": 5
            }
        }
        """;

        var response = ClaudeResponse.FromJson(json, TestDuration);

        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Contain("Something broke");
    }

    [Fact]
    public void FromJson_InvalidJson_ReturnsParseFailure()
    {
        var json = "not valid json {{{";

        var response = ClaudeResponse.FromJson(json, TestDuration);

        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Contain("Failed to parse");
        response.RawOutput.Should().Be(json);
        response.Duration.Should().Be(TestDuration);
    }

    [Fact]
    public void FromJson_MissingUsage_DefaultsToZeroTokens()
    {
        var json = """{"result": "minimal"}""";

        var response = ClaudeResponse.FromJson(json, TestDuration);

        response.Success.Should().BeTrue();
        response.InputTokens.Should().Be(0);
        response.OutputTokens.Should().Be(0);
        response.CostUsd.Should().BeNull();
    }

    [Fact]
    public void FromJson_MissingResult_ReturnsNullResult()
    {
        var json = """{"usage": {"input_tokens": 10, "output_tokens": 5}}""";

        var response = ClaudeResponse.FromJson(json, TestDuration);

        response.Success.Should().BeTrue();
        response.Result.Should().BeNull();
    }

    #endregion

    #region Failure factory

    [Fact]
    public void Failure_CreatesFailedResponse()
    {
        var response = ClaudeResponse.Failure("bad stuff", 1, TestDuration);

        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Be("bad stuff");
        response.ExitCode.Should().Be(1);
        response.Duration.Should().Be(TestDuration);
        response.Result.Should().BeNull();
        response.RawOutput.Should().BeNull();
    }

    #endregion
}
