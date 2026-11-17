using System.ComponentModel.DataAnnotations;
using Hecole.Mediator.Behaviors;
using Hecole.Mediator.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Tests;

public class ValidationBehaviorTests
{
    // Requests used only here.
    public sealed record NoRulesQuery(string? Anything) : IRequest<string>;

    public sealed record SignUpCommand(
        [property: Required] string? Name,
        [property: EmailAddress] string? Email,
        [property: Range(18, 120)] int Age) : IRequest<string>;

    public sealed record ParameterOnlyAttributeCommand([Required] string? Name) : IRequest<string>;

    public sealed class Address
    {
        [Required]
        public string? Street { get; set; }
    }

    public sealed class NestedCommand : IRequest<string>
    {
        [Required]
        public Address? Address { get; set; }
    }

    public sealed record PeriodCommand(DateOnly Start, DateOnly End) : IRequest<string>, IValidatableObject
    {
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (End < Start)
                yield return new ValidationResult("End must not be before Start.", [nameof(End)]);
            if (Start.Year < 2000)
                yield return new ValidationResult("Start must be in 2000 or later.", [nameof(Start)]);
        }
    }

    public sealed class ReservedNames
    {
        public HashSet<string> Names { get; } = ["admin", "root"];
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class NotReservedAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            var reserved = (ReservedNames?)validationContext.GetService(typeof(ReservedNames))
                ?? throw new InvalidOperationException("ReservedNames is not in the container.");
            return value is string name && reserved.Names.Contains(name)
                ? new ValidationResult($"The {validationContext.MemberName} is reserved.", [validationContext.MemberName!])
                : ValidationResult.Success;
        }
    }

    public sealed record ChooseUserNameCommand([property: NotReserved] string UserName) : IRequest<string>;

    public sealed class ServiceAwareCommand : IRequest<string>, IValidatableObject
    {
        public string UserName { get; init; } = "";

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var reserved = validationContext.GetService(typeof(ReservedNames)) as ReservedNames;
            if (reserved is null)
                yield return new ValidationResult("No ReservedNames service.");
            else if (reserved.Names.Contains(UserName))
                yield return new ValidationResult("Reserved.", [nameof(UserName)]);
        }
    }

    private static readonly IServiceProvider EmptyServices = new ServiceCollection().BuildServiceProvider();

    private static async Task<(T? Result, int NextCalls)> Run<T, TResponse>(T request, IServiceProvider? services = null)
        where T : notnull
    {
        var calls = 0;
        var behavior = new ValidationBehavior<T, TResponse>(services ?? EmptyServices);
        await behavior.Handle(request, _ =>
        {
            calls++;
            return Task.FromResult(default(TResponse)!);
        }, CancellationToken.None);
        return (request, calls);
    }

    private static async Task<(RequestValidationException Exception, int NextCalls)> RunInvalid<T>(T request, IServiceProvider? services = null)
        where T : notnull
    {
        var calls = 0;
        var behavior = new ValidationBehavior<T, string>(services ?? EmptyServices);
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() => behavior.Handle(request, _ =>
        {
            calls++;
            return Task.FromResult("next");
        }, CancellationToken.None));
        return (ex, calls);
    }

    [Fact]
    public async Task ValidRequest_ZeroErrors_CallsNextAndReturnsItsResponse()
    {
        var behavior = new ValidationBehavior<SignUpCommand, string>(EmptyServices);

        var result = await behavior.Handle(new SignUpCommand("Ana", "ana@example.com", 30), _ => Task.FromResult("handled"), CancellationToken.None);

        Assert.Equal("handled", result);
    }

    [Fact]
    public async Task RequestWithoutAttributes_PassesThrough()
    {
        var (_, nextCalls) = await Run<NoRulesQuery, string>(new NoRulesQuery(null));

        Assert.Equal(1, nextCalls);
    }

    [Fact]
    public async Task OneError_ThrowsWithThatErrorAndDoesNotCallNext()
    {
        var (ex, nextCalls) = await RunInvalid(new SignUpCommand(null, "ana@example.com", 30));

        Assert.Equal(0, nextCalls);
        var error = Assert.Single(ex.Errors);
        Assert.Equal([nameof(SignUpCommand.Name)], error.MemberNames);
        Assert.Same(error, ex.ValidationResult);
    }

    [Fact]
    public async Task SeveralErrors_AllReportedOnceEach()
    {
        var (ex, nextCalls) = await RunInvalid(new SignUpCommand(null, "not-an-email", 7));

        Assert.Equal(0, nextCalls);
        Assert.Equal(3, ex.Errors.Count);
        Assert.Equal(
            [nameof(SignUpCommand.Name), nameof(SignUpCommand.Email), nameof(SignUpCommand.Age)],
            ex.Errors.Select(e => e.MemberNames.Single()));
        Assert.Equal(ex.Errors.Count, ex.Errors.Select(e => e.ErrorMessage).Distinct().Count());
    }

    [Fact]
    public async Task SameBehaviorInstance_ValidatesEachCallIndependently()
    {
        var behavior = new ValidationBehavior<SignUpCommand, string>(EmptyServices);

        var first = await Assert.ThrowsAsync<RequestValidationException>(() => behavior.Handle(new SignUpCommand(null, null, 30), _ => Task.FromResult(""), default));
        var second = await Assert.ThrowsAsync<RequestValidationException>(() => behavior.Handle(new SignUpCommand(null, null, 30), _ => Task.FromResult(""), default));

        Assert.Single(first.Errors);
        Assert.Single(second.Errors);
    }

    [Fact]
    public async Task Exception_IsCatchableAsNativeValidationException_AndDoesNotKeepTheRequest()
    {
        var behavior = new ValidationBehavior<SignUpCommand, string>(EmptyServices);

        var ex = await Assert.ThrowsAnyAsync<ValidationException>(() => behavior.Handle(new SignUpCommand(null, null, 30), _ => Task.FromResult(""), default));

        var typed = Assert.IsType<RequestValidationException>(ex);
        Assert.Null(typed.Value);
        Assert.Null(typed.ValidationAttribute);
        Assert.Contains(nameof(SignUpCommand), typed.Message, StringComparison.Ordinal);
        Assert.Contains("1 error.", typed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Errors_IsReadOnly()
    {
        var behavior = new ValidationBehavior<SignUpCommand, string>(EmptyServices);

        var ex = await Assert.ThrowsAsync<RequestValidationException>(() => behavior.Handle(new SignUpCommand(null, null, 30), _ => Task.FromResult(""), default));

        var list = Assert.IsAssignableFrom<IList<ValidationResult>>(ex.Errors);
        Assert.True(list.IsReadOnly);
    }

    [Fact]
    public async Task ValidatableObject_AllItsErrorsAreReported()
    {
        var (ex, nextCalls) = await RunInvalid(new PeriodCommand(new DateOnly(1999, 5, 2), new DateOnly(1999, 5, 1)));

        Assert.Equal(0, nextCalls);
        Assert.Equal(["End must not be before Start.", "Start must be in 2000 or later."], ex.Errors.Select(e => e.ErrorMessage));
    }

    [Fact]
    public async Task ValidatableObject_Valid_PassesThrough()
    {
        var (_, nextCalls) = await Run<PeriodCommand, string>(new PeriodCommand(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2)));

        Assert.Equal(1, nextCalls);
    }

    [Fact]
    public async Task Attribute_UsesServiceFromValidationContext()
    {
        var services = new ServiceCollection().AddSingleton<ReservedNames>().BuildServiceProvider();

        var (ex, _) = await RunInvalid(new ChooseUserNameCommand("admin"), services);
        var (_, nextCalls) = await Run<ChooseUserNameCommand, string>(new ChooseUserNameCommand("ana"), services);

        Assert.Equal("The UserName is reserved.", Assert.Single(ex.Errors).ErrorMessage);
        Assert.Equal(1, nextCalls);
    }

    [Fact]
    public async Task ThroughMediator_ValidationContextGetsTheScopeProvider()
    {
        using var host = TestHost.Build(services => services
            .AddScoped<ReservedNames>()
            .AddRequestHandler<ServiceAwareCommand, string, EchoHandler>()
            .AddRequestHandler<ChooseUserNameCommand, string, EchoHandler>(),
            cfg => cfg.AddOpenBehavior(typeof(ValidationBehavior<,>)));

        // Scoped service is only reachable from the scope provider (the root would refuse it with ValidateScopes).
        var ok = await host.Mediator.Send(new ServiceAwareCommand { UserName = "ana" });
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() => host.Mediator.Send(new ServiceAwareCommand { UserName = "root" }));
        var attr = await Assert.ThrowsAsync<RequestValidationException>(() => host.Mediator.Send(new ChooseUserNameCommand("admin")));

        Assert.Equal("ok", ok);
        Assert.Equal("Reserved.", Assert.Single(ex.Errors).ErrorMessage);
        Assert.Single(attr.Errors);
    }

    [Fact]
    public async Task Limit_PositionalRecordAttributeWithoutPropertyTarget_IsNotSeen()
    {
        var (_, nextCalls) = await Run<ParameterOnlyAttributeCommand, string>(new ParameterOnlyAttributeCommand(null));

        Assert.Equal(1, nextCalls);
    }

    [Fact]
    public async Task Limit_NestedObjects_AreNotValidated()
    {
        var (_, nextCalls) = await Run<NestedCommand, string>(new NestedCommand { Address = new Address { Street = null } });
        var (ex, _) = await RunInvalid(new NestedCommand { Address = null });

        Assert.Equal(1, nextCalls);
        Assert.Single(ex.Errors);
    }

    [Fact]
    public async Task VoidRequest_ThroughMediator_IsValidated()
    {
        using var host = TestHost.Build(services => services
            .AddSingleton<Recorder>()
            .AddRequestHandler<RequiredVoidCommand, RequiredVoidHandler>(),
            cfg => cfg.AddOpenBehavior(typeof(ValidationBehavior<,>)));

        await host.Mediator.Send(new RequiredVoidCommand("ok"));
        await Assert.ThrowsAsync<RequestValidationException>(() => host.Mediator.Send(new RequiredVoidCommand(null)));

        Assert.Equal(["ok"], host.GetRequiredService<Recorder>().Entries);
    }

    public sealed record RequiredVoidCommand([property: Required] string? Name) : IRequest;

    public sealed class RequiredVoidHandler(Recorder recorder) : IRequestHandler<RequiredVoidCommand>
    {
        public Task Handle(RequiredVoidCommand request, CancellationToken cancellationToken)
        {
            recorder.Add(request.Name!);
            return Task.CompletedTask;
        }
    }

    public sealed class EchoHandler : IRequestHandler<ServiceAwareCommand, string>, IRequestHandler<ChooseUserNameCommand, string>
    {
        public Task<string> Handle(ServiceAwareCommand request, CancellationToken cancellationToken) => Task.FromResult("ok");

        public Task<string> Handle(ChooseUserNameCommand request, CancellationToken cancellationToken) => Task.FromResult("ok");
    }
}
