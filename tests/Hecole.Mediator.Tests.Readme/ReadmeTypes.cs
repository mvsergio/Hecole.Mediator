// Code samples of README.md and README.pt-BR.md. Every ```csharp block of both READMEs must appear, line by line, in this
// file or in tests/Hecole.Mediator.Tests/ReadmeExamplesTests.cs (checked by ReadmeExamplesTests). Change both together.

using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Hecole.Mediator.Behaviors;
using Microsoft.Extensions.Logging;

namespace Hecole.Mediator.Tests.Readme;

public sealed record CreateOrder(string Customer, decimal Total) : IRequest<OrderId>;

public sealed record OrderId(Guid Value);

public sealed class CreateOrderHandler : IRequestHandler<CreateOrder, OrderId>
{
    public Task<OrderId> Handle(CreateOrder request, CancellationToken cancellationToken)
        => Task.FromResult(new OrderId(Guid.NewGuid()));
}

public sealed class OrdersEndpoint(ISender sender)
{
    public Task<OrderId> Create(string customer, decimal total, CancellationToken cancellationToken)
        => sender.Send(new CreateOrder(customer, total), cancellationToken);
}

public sealed record CancelOrder(Guid OrderId) : IRequest;

public sealed class CancelOrderHandler : IRequestHandler<CancelOrder>
{
    public Task Handle(CancelOrder request, CancellationToken cancellationToken)
    {
        // Cancel the order.
        return Task.CompletedTask;
    }
}

public sealed record OrderCreated(Guid OrderId) : INotification;

public sealed class SendConfirmationEmail : INotificationHandler<OrderCreated>
{
    public Task Handle(OrderCreated notification, CancellationToken cancellationToken)
    {
        // Send the e-mail.
        return Task.CompletedTask;
    }
}

public sealed class UpdateSalesReport : INotificationHandler<OrderCreated>
{
    public Task Handle(OrderCreated notification, CancellationToken cancellationToken)
    {
        // Update the report.
        return Task.CompletedTask;
    }
}

public sealed class StopwatchBehavior<TRequest, TResponse>(ILogger<StopwatchBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
        var response = await next();
        logger.LogInformation("{Request} took {Elapsed} ms", typeof(TRequest).Name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        return response;
    }
}

public sealed record RegisterCustomer(
    [property: Required, EmailAddress] string Email,
    [property: Range(18, 130)] int Age) : IRequest<Guid>;

public sealed class RegisterCustomerHandler : IRequestHandler<RegisterCustomer, Guid>
{
    public Task<Guid> Handle(RegisterCustomer request, CancellationToken cancellationToken)
        => Task.FromResult(Guid.NewGuid());
}

public sealed class CustomersEndpoint(ISender sender)
{
    public async Task<IReadOnlyList<string>> Register(string email, int age, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new RegisterCustomer(email, age), cancellationToken);
            return [];
        }
        catch (RequestValidationException exception)
        {
            // One ValidationResult per error, in property order.
            return exception.Errors.Select(error => $"{string.Join(", ", error.MemberNames)}: {error.ErrorMessage}").ToList();
        }
    }
}

public interface ICommand;

public sealed record ShipOrder(Guid OrderId) : IRequest, ICommand;

public sealed class ShipOrderHandler : IRequestHandler<ShipOrder>
{
    public Task Handle(ShipOrder request, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class PermissionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => next(); // Check the caller's permission.
}

public sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => next(); // Run inside a transaction.
}

public static class CommandPolicy
{
    // Every command runs PermissionBehavior, then TransactionBehavior.
    public static void Check(IRequestCatalog catalog)
    {
        foreach (var request in catalog.Requests.Where(r => typeof(ICommand).IsAssignableFrom(r.RequestType)))
        {
            var permission = IndexOf(request, typeof(PermissionBehavior<,>));
            var transaction = IndexOf(request, typeof(TransactionBehavior<,>));
            if (permission < 0 || transaction < permission)
                throw new InvalidOperationException($"{request.RequestType.Name} must run PermissionBehavior before TransactionBehavior.");
        }
    }

    private static int IndexOf(RequestDescriptor request, Type openBehavior)
        => request.Behaviors.ToList().FindIndex(b => b.IsGenericType && b.GetGenericTypeDefinition() == openBehavior);
}
