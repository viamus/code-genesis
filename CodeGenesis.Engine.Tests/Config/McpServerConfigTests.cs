using CodeGenesis.Engine.Config;
using FluentAssertions;

namespace CodeGenesis.Engine.Tests.Config;

public class McpServerConfigTests
{
    [Fact]
    public void ResolveTemplates_ResolvesCommand()
    {
        var config = new McpServerConfig
        {
            Command = "{{tool_path}}/server",
            Args = [],
            Env = new()
        };

        var resolved = config.ResolveTemplates(s => s.Replace("{{tool_path}}", "/usr/bin"));

        resolved.Command.Should().Be("/usr/bin/server");
    }

    [Fact]
    public void ResolveTemplates_ResolvesArgs()
    {
        var config = new McpServerConfig
        {
            Command = "node",
            Args = ["{{script}}", "--port", "{{port}}"],
            Env = new()
        };

        var resolved = config.ResolveTemplates(s => s
            .Replace("{{script}}", "server.js")
            .Replace("{{port}}", "3000"));

        resolved.Args.Should().Equal("server.js", "--port", "3000");
    }

    [Fact]
    public void ResolveTemplates_ResolvesEnvValues()
    {
        var config = new McpServerConfig
        {
            Command = "server",
            Args = [],
            Env = new()
            {
                ["API_KEY"] = "{{api_key}}",
                ["BASE_URL"] = "{{base_url}}"
            }
        };

        var resolved = config.ResolveTemplates(s => s
            .Replace("{{api_key}}", "secret123")
            .Replace("{{base_url}}", "https://api.example.com"));

        resolved.Env["API_KEY"].Should().Be("secret123");
        resolved.Env["BASE_URL"].Should().Be("https://api.example.com");
    }

    [Fact]
    public void ResolveTemplates_DoesNotMutateOriginal()
    {
        var config = new McpServerConfig
        {
            Command = "{{cmd}}",
            Args = ["{{arg}}"],
            Env = new() { ["K"] = "{{v}}" }
        };

        config.ResolveTemplates(s => "resolved");

        config.Command.Should().Be("{{cmd}}");
        config.Args.Should().Equal("{{arg}}");
        config.Env["K"].Should().Be("{{v}}");
    }

    [Fact]
    public void ResolveTemplates_NoPlaceholders_ReturnsIdentical()
    {
        var config = new McpServerConfig
        {
            Command = "npx",
            Args = ["-y", "@modelcontextprotocol/server"],
            Env = new() { ["NODE_ENV"] = "production" }
        };

        var resolved = config.ResolveTemplates(s => s);

        resolved.Command.Should().Be("npx");
        resolved.Args.Should().Equal("-y", "@modelcontextprotocol/server");
        resolved.Env["NODE_ENV"].Should().Be("production");
    }
}
