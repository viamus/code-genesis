namespace CodeGenesis.Engine.Claude;

public enum ClaudeProgressType { Thinking, ToolUse }

public sealed record ClaudeProgressEvent(ClaudeProgressType Type, string Message);
