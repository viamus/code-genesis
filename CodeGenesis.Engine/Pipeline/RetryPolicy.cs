using CodeGenesis.Engine.Config;

namespace CodeGenesis.Engine.Pipeline;

public sealed record RetryPolicy(
    int MaxRetries,
    int BackoffSeconds,
    int RateLimitPauseSeconds,
    int MaxRateLimitPauses)
{
    public static RetryPolicy Resolve(StepConfig step, PipelineSettings? global) => new(
        step.RetryMax ?? global?.RetryMax ?? 0,
        step.RetryBackoffSeconds ?? global?.RetryBackoffSeconds ?? 10,
        step.RateLimitPauseSeconds ?? global?.RateLimitPauseSeconds ?? 60,
        step.RateLimitMaxPauses ?? global?.RateLimitMaxPauses ?? 5);
}
