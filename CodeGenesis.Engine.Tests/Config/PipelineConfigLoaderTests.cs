using CodeGenesis.Engine.Config;
using FluentAssertions;

namespace CodeGenesis.Engine.Tests.Config;

public class PipelineConfigLoaderTests
{
    #region ResolveTemplate

    [Fact]
    public void ResolveTemplate_NoPlaceholders_ReturnsOriginal()
    {
        var result = PipelineConfigLoader.ResolveTemplate("plain text", []);

        result.Should().Be("plain text");
    }

    [Fact]
    public void ResolveTemplate_SinglePlaceholder_Resolves()
    {
        var vars = new Dictionary<string, string> { ["name"] = "Alice" };

        var result = PipelineConfigLoader.ResolveTemplate("Hello {{name}}!", vars);

        result.Should().Be("Hello Alice!");
    }

    [Fact]
    public void ResolveTemplate_MultiplePlaceholders_ResolvesAll()
    {
        var vars = new Dictionary<string, string>
        {
            ["lang"] = "C#",
            ["framework"] = ".NET"
        };

        var result = PipelineConfigLoader.ResolveTemplate("Build with {{lang}} on {{framework}}", vars);

        result.Should().Be("Build with C# on .NET");
    }

    [Fact]
    public void ResolveTemplate_UnresolvedPlaceholder_LeavesAsIs()
    {
        var vars = new Dictionary<string, string> { ["known"] = "yes" };

        var result = PipelineConfigLoader.ResolveTemplate("{{known}} and {{unknown}}", vars);

        result.Should().Be("yes and {{unknown}}");
    }

    [Fact]
    public void ResolveTemplate_PlaceholderWithSpaces_TrimsKey()
    {
        var vars = new Dictionary<string, string> { ["key"] = "value" };

        var result = PipelineConfigLoader.ResolveTemplate("{{ key }}", vars);

        result.Should().Be("value");
    }

    [Fact]
    public void ResolveTemplate_DottedKey_Resolves()
    {
        var vars = new Dictionary<string, string> { ["steps.plan"] = "my plan" };

        var result = PipelineConfigLoader.ResolveTemplate("Plan: {{steps.plan}}", vars);

        result.Should().Be("Plan: my plan");
    }

    [Fact]
    public void ResolveTemplate_EmptyString_ReturnsEmpty()
    {
        var result = PipelineConfigLoader.ResolveTemplate("", []);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ResolveTemplate_RepeatedPlaceholder_ResolvesAll()
    {
        var vars = new Dictionary<string, string> { ["x"] = "1" };

        var result = PipelineConfigLoader.ResolveTemplate("{{x}} + {{x}} = 2", vars);

        result.Should().Be("1 + 1 = 2");
    }

    #endregion

    #region LoadFromFile

    [Fact]
    public void LoadFromFile_FileNotFound_ThrowsFileNotFoundException()
    {
        var act = () => PipelineConfigLoader.LoadFromFile("/nonexistent/pipeline.yaml");

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void LoadFromFile_ValidYaml_LoadsConfig()
    {
        var yaml = """
            pipeline:
              name: Test Pipeline
              description: A test
              version: "1.0"
            steps:
              - name: step1
                prompt: Do something
            """;

        var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.yaml");
        File.WriteAllText(path, yaml);
        try
        {
            var config = PipelineConfigLoader.LoadFromFile(path);

            config.Pipeline.Name.Should().Be("Test Pipeline");
            config.Pipeline.Description.Should().Be("A test");
            config.Pipeline.Version.Should().Be("1.0");
            config.Steps.Should().HaveCount(1);
            config.Steps[0].Name.Should().Be("step1");
            config.Steps[0].Prompt.Should().Be("Do something");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromFile_EmptySteps_ThrowsInvalidOperation()
    {
        var yaml = """
            pipeline:
              name: Empty
            steps: []
            """;

        var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.yaml");
        File.WriteAllText(path, yaml);
        try
        {
            var act = () => PipelineConfigLoader.LoadFromFile(path);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*at least one step*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromFile_StepMissingName_ThrowsInvalidOperation()
    {
        var yaml = """
            pipeline:
              name: Bad
            steps:
              - prompt: Do something
            """;

        var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.yaml");
        File.WriteAllText(path, yaml);
        try
        {
            var act = () => PipelineConfigLoader.LoadFromFile(path);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*missing a 'name'*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromFile_StepMissingPromptAndContext_ThrowsInvalidOperation()
    {
        var yaml = """
            pipeline:
              name: Bad
            steps:
              - name: empty-step
            """;

        var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.yaml");
        File.WriteAllText(path, yaml);
        try
        {
            var act = () => PipelineConfigLoader.LoadFromFile(path);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*must have either a 'prompt' or a 'context'*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromFile_ForeachMissingCollection_ThrowsInvalidOperation()
    {
        var yaml = """
            pipeline:
              name: Bad
            steps:
              - foreach:
                  steps:
                    - name: sub
                      prompt: do
            """;

        var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.yaml");
        File.WriteAllText(path, yaml);
        try
        {
            var act = () => PipelineConfigLoader.LoadFromFile(path);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*missing 'collection'*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromFile_ParallelMissingBranches_ThrowsInvalidOperation()
    {
        var yaml = """
            pipeline:
              name: Bad
            steps:
              - parallel:
                  branches: []
            """;

        var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.yaml");
        File.WriteAllText(path, yaml);
        try
        {
            var act = () => PipelineConfigLoader.LoadFromFile(path);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*at least one branch*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromFile_WithInputsAndOutputs_ParsesCorrectly()
    {
        var yaml = """
            pipeline:
              name: Full
            inputs:
              task:
                description: The task
                default: hello
            steps:
              - name: step1
                prompt: "{{task}}"
            outputs:
              result:
                source: step1
                description: The result
            """;

        var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.yaml");
        File.WriteAllText(path, yaml);
        try
        {
            var config = PipelineConfigLoader.LoadFromFile(path);

            config.Inputs.Should().ContainKey("task");
            config.Inputs["task"].Default.Should().Be("hello");
            config.Outputs.Should().ContainKey("result");
            config.Outputs["result"].Source.Should().Be("step1");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromFile_McpServerWithDescriptionAndParameters_ParsesCorrectly()
    {
        var yaml = """
            pipeline:
              name: MCP Test
            settings:
              mcp_servers:
                jira:
                  command: npx
                  args: ["-y", "@anthropic/mcp-jira"]
                  description: "Search and manage Jira tickets"
                  parameters:
                    project_key:
                      description: "The Jira project key"
                      example: "PROJ-123"
                  env:
                    JIRA_TOKEN: "secret"
            steps:
              - name: step1
                prompt: Do something
            """;

        var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.yaml");
        File.WriteAllText(path, yaml);
        try
        {
            var config = PipelineConfigLoader.LoadFromFile(path);

            var jira = config.Settings.McpServers!["jira"];
            jira.Command.Should().Be("npx");
            jira.Description.Should().Be("Search and manage Jira tickets");
            jira.Parameters.Should().ContainKey("project_key");
            jira.Parameters!["project_key"].Description.Should().Be("The Jira project key");
            jira.Parameters["project_key"].Example.Should().Be("PROJ-123");
        }
        finally
        {
            File.Delete(path);
        }
    }

    #endregion
}
