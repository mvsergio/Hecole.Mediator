using FluentValidation;
using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;

namespace Hecole.Mediator.Implementation.Behaviors
{
    /// <summary>
    /// Pipeline behavior that validates requests using FluentValidation before reaching the handler.
    /// Supports both synchronous and asynchronous validators (MustAsync).
    /// Throws <see cref="ValidationException"/> if any validation rule fails.
    /// </summary>
    public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly IEnumerable<IValidator<TRequest>> _validators = validators;

        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            if (_validators.Any())
            {
                var context = new ValidationContext<TRequest>(request);
                var validationResults = await Task.WhenAll(
                    _validators.Select(v => v.ValidateAsync(context, cancellationToken)));
                var failures = validationResults
                    .SelectMany(r => r.Errors)
                    .Where(f => f is not null)
                    .ToList();

                if (failures.Count != 0)
                    throw new ValidationException(failures);
            }

            return await next();
        }
    }
}
