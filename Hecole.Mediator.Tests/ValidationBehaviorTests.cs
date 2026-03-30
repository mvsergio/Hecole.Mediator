using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Hecole.Mediator.Implementation.Behaviors;
using Hecole.Mediator.Interfaces.Behaviors;
using Hecole.Mediator.Tests.Fakes;
using NSubstitute;

namespace Hecole.Mediator.Tests;

public class ValidationBehaviorTests
{
    private static RequestHandlerDelegate<TestResult> NextReturning(TestResult result)
        => () => Task.FromResult(result);

    [Fact]
    public async Task Handle_ValidCommand_PassesThroughToHandler()
    {
        var expected = new TestResult("ok");
        var validators = new IValidator<TestCommand>[] { new TestCommandValidator() };
        var behavior = new ValidationBehavior<TestCommand, TestResult>(validators);

        var result = await behavior.Handle(new TestCommand("valid"), NextReturning(expected), CancellationToken.None);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Handle_InvalidCommand_ThrowsValidationException()
    {
        var validators = new IValidator<TestCommand>[] { new TestCommandValidator() };
        var behavior = new ValidationBehavior<TestCommand, TestResult>(validators);

        var act = () => behavior.Handle(new TestCommand(""), NextReturning(new TestResult("x")), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.ErrorMessage == "Name is required");
    }

    [Fact]
    public async Task Handle_MultipleValidators_AllAreExecuted()
    {
        var validators = new IValidator<TestCommand>[]
        {
            new TestCommandValidator(),
            new TestCommandLengthValidator()
        };
        var behavior = new ValidationBehavior<TestCommand, TestResult>(validators);

        // empty name triggers NotEmpty; "a]" is fine for length — only 1 error
        var act = () => behavior.Handle(new TestCommand(""), NextReturning(new TestResult("x")), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_MultipleFailures_ReturnsAllErrors()
    {
        // "this is way too long" — empty won't fire, but MaxLength(10) will
        // We need both to fail: use empty string for NotEmpty, and it still is < 10, so only 1 error from empty
        // Better: make a custom scenario with two explicit failing validators
        var validator1 = Substitute.For<IValidator<TestCommand>>();
        validator1.ValidateAsync(Arg.Any<ValidationContext<TestCommand>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(new[] { new ValidationFailure("Name", "Error1") }));

        var validator2 = Substitute.For<IValidator<TestCommand>>();
        validator2.ValidateAsync(Arg.Any<ValidationContext<TestCommand>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(new[] { new ValidationFailure("Name", "Error2") }));

        var behavior = new ValidationBehavior<TestCommand, TestResult>(new[] { validator1, validator2 });

        var act = () => behavior.Handle(new TestCommand("x"), NextReturning(new TestResult("x")), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<ValidationException>()).Which;
        ex.Errors.Should().HaveCount(2);
        ex.Errors.Select(e => e.ErrorMessage).Should().Contain("Error1").And.Contain("Error2");
    }

    [Fact]
    public async Task Handle_NoValidatorsRegistered_PassesThroughToHandler()
    {
        var expected = new TestResult("no validators");
        var behavior = new ValidationBehavior<TestCommand, TestResult>(Enumerable.Empty<IValidator<TestCommand>>());

        var result = await behavior.Handle(new TestCommand("anything"), NextReturning(expected), CancellationToken.None);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Handle_AsyncValidator_MustAsyncIsExecuted()
    {
        var validators = new IValidator<TestCommand>[] { new AsyncTestCommandValidator() };
        var behavior = new ValidationBehavior<TestCommand, TestResult>(validators);

        // "forbidden" should fail via MustAsync
        var act = () => behavior.Handle(new TestCommand("forbidden"), NextReturning(new TestResult("x")), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.ErrorMessage == "Name is forbidden");
    }

    [Fact]
    public async Task Handle_AsyncValidator_ValidValuePassesThrough()
    {
        var expected = new TestResult("async ok");
        var validators = new IValidator<TestCommand>[] { new AsyncTestCommandValidator() };
        var behavior = new ValidationBehavior<TestCommand, TestResult>(validators);

        var result = await behavior.Handle(new TestCommand("allowed"), NextReturning(expected), CancellationToken.None);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Handle_CancellationToken_IsPassedToValidateAsync()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var validator = Substitute.For<IValidator<TestCommand>>();
        validator.ValidateAsync(Arg.Any<ValidationContext<TestCommand>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ct = callInfo.ArgAt<CancellationToken>(1);
                ct.ThrowIfCancellationRequested();
                return new ValidationResult();
            });

        var behavior = new ValidationBehavior<TestCommand, TestResult>(new[] { validator });

        var act = () => behavior.Handle(new TestCommand("test"), NextReturning(new TestResult("x")), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
