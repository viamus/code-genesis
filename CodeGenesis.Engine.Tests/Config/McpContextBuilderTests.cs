using CodeGenesis.Engine.Config;
using FluentAssertions;

namespace CodeGenesis.Engine.Tests.Config;

public class McpContextBuilderTests
{
    [Fact]
    public void Build_NoDocumentedServers_ReturnsNull()
    {
        var servers = new Dictionary<string, McpServerConfig>
        {
            ["server1"] = new() { Command = "npx" }
        };

        var result = McpContextBuilder.Build(servers);

        result.Should().BeNull();
    }

    [Fact]
    public void Build_EmptyDictionary_ReturnsNull()
    {
        var result = McpContextBuilder.Build(new Dictionary<string, McpServerConfig>());

        result.Should().BeNull();
    }

    [Fact]
    public void Build_WithDescription_IncludesServerSection()
    {
        var servers = new Dictionary<string, McpServerConfig>
        {
            ["jira"] = new()
            {
                Command = "npx",
                Description = "Search and manage Jira tickets"
            }
        };

        var result = McpContextBuilder.Build(servers);

        result.Should().NotBeNull();
        result.Should().Contain("## Available MCP Tools");
        result.Should().Contain("### jira");
        result.Should().Contain("Search and manage Jira tickets");
    }

    [Fact]
    public void Build_WithParameters_IncludesParameterList()
    {
        var servers = new Dictionary<string, McpServerConfig>
        {
            ["jira"] = new()
            {
                Command = "npx",
                Description = "Manage tickets",
                Parameters = new Dictionary<string, McpParameterConfig>
                {
                    ["project_key"] = new()
                    {
                        Description = "The Jira project key",
                        Example = "PROJ-123"
                    }
                }
            }
        };

        var result = McpContextBuilder.Build(servers);

        result.Should().Contain("**Parameters:**");
        result.Should().Contain("`project_key`");
        result.Should().Contain("The Jira project key");
        result.Should().Contain("`PROJ-123`");
    }

    [Fact]
    public void Build_ParameterWithoutExample_OmitsExampleClause()
    {
        var servers = new Dictionary<string, McpServerConfig>
        {
            ["svc"] = new()
            {
                Description = "A service",
                Parameters = new Dictionary<string, McpParameterConfig>
                {
                    ["id"] = new() { Description = "The ID", Example = null }
                }
            }
        };

        var result = McpContextBuilder.Build(servers);

        result.Should().Contain("`id`: The ID");
        result.Should().NotContain("example:");
    }

    [Fact]
    public void Build_ParameterWithoutDescription_OmitsDescriptionClause()
    {
        var servers = new Dictionary<string, McpServerConfig>
        {
            ["svc"] = new()
            {
                Description = "A service",
                Parameters = new Dictionary<string, McpParameterConfig>
                {
                    ["id"] = new() { Description = null, Example = "abc-123" }
                }
            }
        };

        var result = McpContextBuilder.Build(servers);

        result.Should().Contain("`id`");
        result.Should().Contain("`abc-123`");
    }

    [Fact]
    public void Build_ServerWithParametersButNoDescription_IsIncluded()
    {
        var servers = new Dictionary<string, McpServerConfig>
        {
            ["svc"] = new()
            {
                Command = "npx",
                Parameters = new Dictionary<string, McpParameterConfig>
                {
                    ["x"] = new() { Description = "A param" }
                }
            }
        };

        var result = McpContextBuilder.Build(servers);

        result.Should().NotBeNull();
        result.Should().Contain("### svc");
        result.Should().Contain("`x`");
    }

    [Fact]
    public void Build_MultipleServers_EmitsAllSections()
    {
        var servers = new Dictionary<string, McpServerConfig>
        {
            ["jira"] = new() { Description = "Jira access" },
            ["github"] = new() { Description = "GitHub access" }
        };

        var result = McpContextBuilder.Build(servers);

        result.Should().Contain("### jira");
        result.Should().Contain("### github");
    }

    [Fact]
    public void Build_UndocumentedServersAreSkipped()
    {
        var servers = new Dictionary<string, McpServerConfig>
        {
            ["documented"] = new() { Description = "Has docs" },
            ["bare"] = new() { Command = "npx" }
        };

        var result = McpContextBuilder.Build(servers);

        result.Should().Contain("### documented");
        result.Should().NotContain("### bare");
    }
}
