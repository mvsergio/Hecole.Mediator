using Hecole.Mediator.Behaviors;
using Hecole.Mediator.NotificationPublishers;
using Hecole.Mediator.Tests.Fakes;
using Hecole.Mediator.Tests.Readme;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hecole.Mediator.Tests;

/// <summary>
/// Runs the code samples of README.md and README.pt-BR.md. The types of the samples are in
/// tests/Hecole.Mediator.Tests.Readme/ReadmeTypes.cs; the statements are in the tests below, written exactly as in the
/// READMEs. <see cref="EveryReadmeSample_IsInTheTestSources"/> fails when a ```csharp block of either README is not
/// found, line by line, in one of those two files.
/// </summary>
public sealed class ReadmeExamplesTests
{
    private static readonly CancellationToken cancellationToken = CancellationToken.None;

    [Fact]
    public async Task QuickStart_Scan()
    {
        var services = new ServiceCollection();

        services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssemblyContaining<CreateOrder>());

        using var host = Host(services);
        var endpoint = new OrdersEndpoint(host.Get<ISender>());
        var orderId = await endpoint.Create("ACME", 10m, cancellationToken);
        Assert.NotEqual(Guid.Empty, orderId.Value);
    }

    [Fact]
    public async Task QuickStart_Explicit()
    {
        var services = new ServiceCollection();

        services.AddHecoleMediator(_ => { });
        services.AddRequestHandler<CreateOrder, OrderId, CreateOrderHandler>();
        services.AddRequestHandler<CancelOrder, CancelOrderHandler>();
        services.AddNotificationHandler<OrderCreated, SendConfirmationEmail>();
        services.AddNotificationHandler<OrderCreated, UpdateSalesReport>();

        using var host = Host(services);
        var orderId = await new OrdersEndpoint(host.Get<ISender>()).Create("ACME", 10m, cancellationToken);
        Assert.NotEqual(Guid.Empty, orderId.Value);
        Assert.Equal(2, host.Get<IEnumerable<INotificationHandler<OrderCreated>>>().Count());
    }

    [Fact]
    public async Task RequestWithoutResponse_AndSendObject()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssemblyContaining<CreateOrder>());
        using var host = Host(services);
        var sender = host.Get<ISender>();
        var orderId = await sender.Send(new CreateOrder("ACME", 10m), cancellationToken);

        await sender.Send(new CancelOrder(orderId.Value), cancellationToken);

        object request = new CreateOrder("ACME", 10m);
        object? response = await sender.Send(request, cancellationToken); // an OrderId
        object? none = await sender.Send((object)new CancelOrder(orderId.Value), cancellationToken); // Unit.Value

        Assert.IsType<OrderId>(response);
        Assert.Equal(Unit.Value, none);
    }

    [Fact]
    public async Task Publish_RunsBothHandlersInRegistrationOrder()
    {
        var services = new ServiceCollection();
        var recording = new RecordingPublisher(new ForeachAwaitPublisher());
        services.AddSingleton<INotificationPublisher>(recording); // kept by AddHecoleMediator (TryAdd)
        services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssemblyContaining<CreateOrder>());
        using var host = Host(services);
        var publisher = host.Get<IPublisher>();
        var orderId = new OrderId(Guid.NewGuid());

        await publisher.Publish(new OrderCreated(orderId.Value), cancellationToken);

        Assert.Equal([typeof(SendConfirmationEmail), typeof(UpdateSalesReport)], recording.HandlerTypes);
    }

    [Fact]
    public async Task Publish_WithTaskWhenAllPublisher()
    {
        var services = new ServiceCollection();

        services.AddHecoleMediator(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CreateOrder>();
            cfg.NotificationPublisher = new TaskWhenAllPublisher();
        });

        using var host = Host(services);
        Assert.IsType<TaskWhenAllPublisher>(host.Get<INotificationPublisher>());
        await host.Get<IPublisher>().Publish(new OrderCreated(Guid.NewGuid()), cancellationToken);
    }

    [Fact]
    public async Task Behaviors_RunInRegistrationOrder()
    {
        var services = new ServiceCollection();
        var sink = new LogSink();
        services.AddSingleton(sink).AddSingleton(typeof(ILogger<>), typeof(FakeLogger<>));

        services.AddHecoleMediator(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CreateOrder>();
            cfg.AddOpenBehavior(typeof(StopwatchBehavior<,>)); // outermost
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>)); // runs after StopwatchBehavior, before the handler
        });

        using var host = Host(services);

        // The log observations below hold in either order (a validation failure stops both a StopwatchBehavior outside
        // it and one inside it from logging), so the order is read where the pipeline reads it: the behaviors the
        // container returns for the request, the first being the outermost (PipelineOrderingTests runs that order).
        Assert.Equal(
            [typeof(StopwatchBehavior<,>), typeof(ValidationBehavior<,>)],
            host.Get<IEnumerable<IPipelineBehavior<CreateOrder, OrderId>>>().Select(b => b.GetType().GetGenericTypeDefinition()));

        var sender = host.Get<ISender>();
        await sender.Send(new CreateOrder("ACME", 10m), cancellationToken);
        Assert.Contains(sink.Entries, e => e.Message.StartsWith("CreateOrder took ", StringComparison.Ordinal));

        // The validation failure goes through StopwatchBehavior (outer) before reaching the caller: no "took" entry.
        await Assert.ThrowsAsync<RequestValidationException>(() => sender.Send(new RegisterCustomer("x", 1), cancellationToken));
        Assert.DoesNotContain(sink.Entries, e => e.Message.StartsWith("RegisterCustomer took ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuiltInBehaviors()
    {
        var services = new ServiceCollection();
        var sink = new LogSink();
        services.AddSingleton(sink).AddSingleton(typeof(ILogger<>), typeof(FakeLogger<>));

        services.AddHecoleMediator(cfg => cfg
            .RegisterServicesFromAssemblyContaining<CreateOrder>()
            .AddOpenBehavior(typeof(UnhandledExceptionBehavior<,>))
            .AddOpenBehavior(typeof(LoggingBehavior<,>))
            .AddOpenBehavior(typeof(PerformanceBehavior<,>))
            .AddOpenBehavior(typeof(ValidationBehavior<,>)));
        services.AddSingleton(new PerformanceBehaviorOptions { Threshold = TimeSpan.FromSeconds(1) });

        using var host = Host(services);
        await host.Get<ISender>().Send(new CreateOrder("ACME", 10m), cancellationToken);
        Assert.Equal([1001, 1002], sink.Entries.Select(e => e.EventId.Id));

        var errors = await new CustomersEndpoint(host.Get<ISender>()).Register("not-an-email", 12, cancellationToken);
        Assert.Equal(2, errors.Count);
        Assert.StartsWith("Email: ", errors[0], StringComparison.Ordinal);
        Assert.StartsWith("Age: ", errors[1], StringComparison.Ordinal);
        Assert.Empty(await new CustomersEndpoint(host.Get<ISender>()).Register("ana@example.com", 30, cancellationToken));
    }

    [Fact]
    public void StartupVerification()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssemblyContaining<CreateOrder>());

        using var provider = services.BuildServiceProvider();
        provider.VerifyHecoleMediator(typeof(CreateOrder).Assembly);
    }

    [Fact]
    public void StartupVerification_ReportsARequestWithoutHandler()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        services.AddRequestHandler<CreateOrder, OrderId, CreateOrderHandler>();
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<MediatorConfigurationException>(() => provider.VerifyHecoleMediator(typeof(CreateOrder).Assembly));

        Assert.Contains(exception.Problems, p => p.Contains(nameof(CancelOrder), StringComparison.Ordinal));
        Assert.Contains(exception.Problems, p => p.Contains(nameof(RegisterCustomer), StringComparison.Ordinal));
    }

    [Fact]
    public void CatalogPolicyCheck()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg
            .RegisterServicesFromAssemblyContaining<CreateOrder>()
            .AddOpenBehavior(typeof(PermissionBehavior<,>))
            .AddOpenBehavior(typeof(TransactionBehavior<,>)));

        using var provider = services.BuildServiceProvider();
        CommandPolicy.Check(provider.GetRequiredService<IRequestCatalog>());

        var catalog = provider.GetRequiredService<IRequestCatalog>();
        Assert.Equal(
            [typeof(PermissionBehavior<ShipOrder, Unit>), typeof(TransactionBehavior<ShipOrder, Unit>)],
            catalog.Find(typeof(ShipOrder))!.Behaviors);
        Assert.Equal([typeof(PermissionBehavior<,>), typeof(TransactionBehavior<,>)], catalog.Find(typeof(CreateOrder))!.SkippedBehaviors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CatalogPolicyCheck_FailsWhenTheOrderIsWrongOrABehaviorIsMissing(bool missing)
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CreateOrder>();
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
            if (!missing)
                cfg.AddOpenBehavior(typeof(PermissionBehavior<,>));
        });
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => CommandPolicy.Check(provider.GetRequiredService<IRequestCatalog>()));

        Assert.StartsWith(nameof(ShipOrder), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryReadmeSample_IsInTheTestSources()
    {
        var root = RepositoryRoot();
        var sources = new[]
        {
            Path.Combine(root, "tests", "Hecole.Mediator.Tests", "ReadmeExamplesTests.cs"),
            Path.Combine(root, "tests", "Hecole.Mediator.Tests.Readme", "ReadmeTypes.cs"),
        }.Select(path => Lines(File.ReadAllText(path))).ToList();

        var english = CSharpBlocks(File.ReadAllText(Path.Combine(root, "README.md")));
        var portuguese = CSharpBlocks(File.ReadAllText(Path.Combine(root, "README.pt-BR.md")));

        Assert.NotEmpty(english);
        Assert.Equal(english, portuguese);
        foreach (var block in english)
        {
            Assert.True(
                sources.Any(source => ContainsSequence(source, block)),
                "README sample not found in the test sources:" + Environment.NewLine + string.Join(Environment.NewLine, block));
        }
    }

    private static List<List<string>> CSharpBlocks(string markdown)
    {
        var blocks = new List<List<string>>();
        List<string>? current = null;
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            if (current is null)
            {
                if (line == "```csharp")
                    current = [];
            }
            else if (line == "```")
            {
                blocks.Add(current);
                current = null;
            }
            else if (line.Length > 0)
            {
                current.Add(line);
            }
        }

        Assert.Null(current);
        return blocks;
    }

    private static List<string> Lines(string text)
        => text.Split('\n').Select(static l => l.Trim()).Where(static l => l.Length > 0).ToList();

    private static bool ContainsSequence(List<string> source, List<string> block)
    {
        for (var start = 0; start + block.Count <= source.Count; start++)
        {
            var i = 0;
            while (i < block.Count && source[start + i] == block[i])
                i++;
            if (i == block.Count)
                return true;
        }

        return false;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Hecole.Mediator.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Repository root (Hecole.Mediator.slnx) not found above " + AppContext.BaseDirectory);
    }

    private static ReadmeHost Host(ServiceCollection services)
        => new(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }));

    private sealed class ReadmeHost(ServiceProvider root) : IDisposable
    {
        private readonly IServiceScope _scope = root.CreateScope();

        public T Get<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

        public void Dispose()
        {
            _scope.Dispose();
            root.Dispose();
        }
    }

    private sealed class RecordingPublisher(INotificationPublisher inner) : INotificationPublisher
    {
        public List<Type> HandlerTypes { get; } = [];

        public Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
        {
            var executors = handlerExecutors.ToList();
            HandlerTypes.AddRange(executors.Select(static e => e.HandlerInstance.GetType()));
            return inner.Publish(executors, notification, cancellationToken);
        }
    }
}
