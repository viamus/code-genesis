namespace CodeGenesis.Engine.Claude;

public interface IClaudeRunner
{
    Task<ClaudeResponse> RunAsync(ClaudeRequest request, CancellationToken ct = default);
}
