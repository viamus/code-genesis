using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace CodeGenesis.Engine.Cli;

/// <summary>
/// Bridges Microsoft.Extensions.DependencyInjection into Spectre.Console.Cli.
/// </summary>
public sealed class TypeRegistrar(IServiceProvider services) : ITypeRegistrar
{
    public ITypeResolver Build() => new TypeResolver(services);

    public void Register(Type service, Type implementation) { }
    public void RegisterInstance(Type service, object implementation) { }
    public void RegisterLazy(Type service, Func<object> factory) { }
}

internal sealed class TypeResolver(IServiceProvider provider) : ITypeResolver
{
    public object? Resolve(Type? type) =>
        type is null ? null : provider.GetService(type);
}
