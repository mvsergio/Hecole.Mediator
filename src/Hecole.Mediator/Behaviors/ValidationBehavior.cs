using System.ComponentModel.DataAnnotations;

namespace Hecole.Mediator.Behaviors;

/// <summary>
/// Optional pipeline behavior that validates the request with <c>System.ComponentModel.DataAnnotations</c> before the
/// next step: <see cref="Validator.TryValidateObject(object, ValidationContext, ICollection{ValidationResult}?, bool)"/>
/// with <c>validateAllProperties: true</c>. A valid request goes on to the next step; an invalid one throws
/// <see cref="RequestValidationException"/> with every error, once each, and the next step does not run. Enters the
/// pipeline only when registered: <c>cfg.AddOpenBehavior(typeof(ValidationBehavior&lt;,&gt;))</c>.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="ValidationContext"/> receives the <see cref="IServiceProvider"/> of the scope that resolved the
/// behavior, so a <see cref="ValidationAttribute"/> or an <see cref="IValidatableObject"/> can call
/// <see cref="ValidationContext.GetService(Type)"/>.
/// </para>
/// <para>Limits of the .NET validator (this behavior does not work around them):</para>
/// <list type="bullet">
/// <item><description>Only the request's own properties are validated: it does not descend into nested objects or into the items of collections.</description></item>
/// <item><description>In a positional record, an attribute on a parameter applies to the parameter only; write <c>[property: Required]</c> so the validator sees it.</description></item>
/// <item><description>Class-level attributes and <see cref="IValidatableObject.Validate(ValidationContext)"/> run only when every property is valid.</description></item>
/// <item><description>Validation is synchronous; there is no asynchronous rule.</description></item>
/// </list>
/// </remarks>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Response type.</typeparam>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>Creates the behavior.</summary>
    /// <param name="serviceProvider">Provider handed to the <see cref="ValidationContext"/>.</param>
    public ValidationBehavior(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    /// <exception cref="RequestValidationException">The request is invalid.</exception>
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(next);

        var context = new ValidationContext(request, _serviceProvider, items: null);
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, context, errors, validateAllProperties: true))
        {
            throw new RequestValidationException(typeof(TRequest), errors);
        }

        return next();
    }
}
