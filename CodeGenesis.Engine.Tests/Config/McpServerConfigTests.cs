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

    [Fact]
    public void ResolveTemplates_ResolvesDescription()
    {
        var config = new McpServerConfig
        {
            Command = "npx",
            Description = "Access {{system_name}} tickets"
        };

        var resolved = config.ResolveTemplates(s => s.Replace("{{system_name}}", "Jira"));

        resolved.Description.Should().Be("Access Jira tickets");
    }

    [Fact]
    public void ResolveTemplates_ResolvesParameterDescriptionAndExample()
    {
        var config = new McpServerConfig
        {
            Command = "npx",
            Parameters = new Dictionary<string, McpParameterConfig>
            {
                ["project_key"] = new()
                {
                    Description = "The {{board}} project key",
                    Example = "{{default_project}}-123"
                }
            }
        };

        var resolved = config.ResolveTemplates(s => s
            .Replace("{{board}}", "Jira")
            .Replace("{{default_project}}", "PROJ"));

        resolved.Parameters!["project_key"].Description.Should().Be("The Jira project key");
        resolved.Parameters["project_key"].Example.Should().Be("PROJ-123");
    }

    [Fact]
    public void ResolveTemplates_NullDescription_RemainsNull()
    {
        var config = new McpServerConfig { Command = "npx", Description = null };

        var resolved = config.ResolveTemplates(s => s);

        resolved.Description.Should().BeNull();
    }

    [Fact]
    public void ResolveTemplates_NullParameters_RemainsNull()
    {
        var config = new McpServerConfig { Command = "npx", Parameters = null };

        var resolved = config.ResolveTemplates(s => s);

        resolved.Parameters.Should().BeNull();
    }

    [Fact]
    public void ResolveTemplates_DoesNotMutateOriginalDescriptionOrParameters()
    {
        var config = new McpServerConfig
        {
            Command = "npx",
            Description = "{{service}}",
            Parameters = new Dictionary<string, McpParameterConfig>
            {
                ["key"] = new() { Description = "{{desc}}", Example = "{{ex}}" }
            }
        };

        config.ResolveTemplates(s => "resolved");

        config.Description.Should().Be("{{service}}");
        config.Parameters["key"].Description.Should().Be("{{desc}}");
        config.Parameters["key"].Example.Should().Be("{{ex}}");
    }
}
