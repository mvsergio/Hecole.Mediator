using System.Collections.ObjectModel;

namespace Hecole.Mediator;

/// <summary>
/// Thrown by <c>VerifyHecoleMediator</c> when the mediator configuration has problems. The message lists every
/// problem found, one per line; <see cref="Problems"/> holds the same list.
/// </summary>
public sealed class MediatorConfigurationException : Exception
{
    internal MediatorConfigurationException(IEnumerable<string> problems)
        : this(new ReadOnlyCollection<string>(problems.ToArray()))
    {
    }

    private MediatorConfigurationException(ReadOnlyCollection<string> problems)
        : base(Format(problems))
    {
        Problems = problems;
    }

    /// <summary>
    /// The problems found, one entry each, in the order they appear in <see cref="Exception.Message"/>.
    /// The list is a read-only copy.
    /// </summary>
    public IReadOnlyList<string> Problems { get; }

    private static string Format(IReadOnlyList<string> problems)
        => $"The Hecole.Mediator configuration has {problems.Count} problem(s):" + Environment.NewLine
            + string.Join(Environment.NewLine, problems.Select(static p => "- " + p));
}
