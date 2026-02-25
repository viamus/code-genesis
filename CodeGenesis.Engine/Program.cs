using CodeGenesis.Engine.Claude;
using CodeGenesis.Engine.Cli;
using CodeGenesis.Engine.Pipeline;
using CodeGenesis.Engine.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Spectre.Console.Cli;

// ── Serilog bootstrap ──────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.File(
        path: Path.Combine("logs", "codegenesis-.log"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("CodeGenesis Engine starting");

    // ── Configuration ──────────────────────────────────────────
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables("CODEGENESIS_")
        .Build();

    // ── DI Container ───────────────────────────────────────────
    var services = new ServiceCollection();

    services.AddLogging(builder => builder.AddSerilog());

    // Options
    services.Configure<ClaudeCliOptions>(
        configuration.GetSection("Claude"));

    // Core services
    services.AddSingleton<IClaudeRunner, ClaudeCliRunner>();
    services.AddSingleton<PipelineExecutor>();
    services.AddSingleton<IStepExecutor>(sp => sp.GetRequiredService<PipelineExecutor>());
    services.AddSingleton<PipelineRenderer>();

    // CLI commands
    services.AddTransient<RunCommand>();
    services.AddTransient<RunPipelineCommand>();

    var provider = services.BuildServiceProvider();

    // ── Spectre CLI ────────────────────────────────────────────
    var app = new CommandApp(new TypeRegistrar(provider));

    app.Configure(config =>
    {
        config.SetApplicationName("codegenesis");
        config.SetApplicationVersion("0.1.0");

        config.AddCommand<RunCommand>("run")
            .WithDescription("Run a hardcoded Plan → Execute → Validate pipeline")
            .WithExample("run", "\"Add retry logic to the HttpClient service\"")
            .WithExample("run", "\"Create unit tests for UserService\"", "--skip-validate");

        config.AddCommand<RunPipelineCommand>("run-pipeline")
            .WithDescription("Run a pipeline from a YAML configuration file")
            .WithExample("run-pipeline", "examples/hello-world.yml")
            .WithExample("run-pipeline", "examples/hello-world.yml", "--input", "task=\"Create a Python script\"");
    });

    return await app.RunAsync(args);
}
catch (Exception ex)
{
    Log.Fatal(ex, "CodeGenesis Engine terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
