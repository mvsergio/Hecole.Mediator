using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Tests;

/// <summary>
/// Root provider (with scope validation) plus one scope; services are resolved from the scope,
/// as in a web request.
/// </summary>
internal sealed class TestHost : IDisposable
{
    private TestHost(ServiceProvider root)
    {
        Root = root;
        Scope = root.CreateScope();
    }

    public ServiceProvider Root { get; }

    public IServiceScope Scope { get; }

    public IMediator Mediator => GetRequiredService<IMediator>();

    public static TestHost Build(Action<IServiceCollection> configure, Action<HecoleMediatorConfiguration>? mediator = null)
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(mediator ?? (_ => { }));
        configure(services);
        return new TestHost(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }));
    }

    public T GetRequiredService<T>() where T : notnull => Scope.ServiceProvider.GetRequiredService<T>();

    public void Dispose()
    {
        Scope.Dispose();
        Root.Dispose();
    }
}
