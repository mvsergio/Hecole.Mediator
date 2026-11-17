using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Hecole.Mediator.Behaviors;

/// <summary>
/// Thrown by <see cref="ValidationBehavior{TRequest, TResponse}"/> when a request is invalid. Carries every error
/// found, once each, in <see cref="Errors"/>. Derives from <see cref="ValidationException"/>, so
/// <c>catch (ValidationException)</c> also catches it; the inherited <see cref="ValidationException.ValidationResult"/>
/// is the first error. The request itself is not kept (<see cref="ValidationException.Value"/> is
/// <see langword="null"/>), so logging the exception does not write the request.
/// </summary>
public sealed class RequestValidationException : ValidationException
{
    internal RequestValidationException(Type requestType, IList<ValidationResult> errors)
        : this(requestType, new ReadOnlyCollection<ValidationResult>(errors.ToArray()))
    {
    }

    private RequestValidationException(Type requestType, ReadOnlyCollection<ValidationResult> errors)
        : base(errors[0], validatingAttribute: null, value: null)
    {
        Errors = errors;
        _message = Format(requestType, errors);
    }

    private readonly string _message;

    /// <summary>Every validation error of the request, in the order the validator reported them. Never empty.</summary>
    public IReadOnlyList<ValidationResult> Errors { get; }

    /// <inheritdoc />
    public override string Message => _message;

    private static string Format(Type requestType, ReadOnlyCollection<ValidationResult> errors)
    {
        var sb = new StringBuilder()
            .Append("Validation failed for ").Append(requestType.Name).Append(": ")
            .Append(errors.Count).Append(errors.Count == 1 ? " error." : " errors.");
        foreach (var error in errors)
        {
            sb.AppendLine().Append(" - ");
            if (error.MemberNames.Any())
                sb.Append(string.Join(", ", error.MemberNames)).Append(": ");
            sb.Append(error.ErrorMessage);
        }

        return sb.ToString();
    }
}
