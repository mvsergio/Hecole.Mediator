namespace Hecole.Mediator;

/// <summary>
/// A type with a single value; the response of a request that produces no response value.
/// </summary>
public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>, IComparable
{
    /// <summary>
    /// The single value of <see cref="Unit"/>.
    /// </summary>
    public static readonly Unit Value = default;

    /// <summary>
    /// A completed task whose result is <see cref="Value"/>.
    /// </summary>
    public static System.Threading.Tasks.Task<Unit> Task { get; } = System.Threading.Tasks.Task.FromResult(Value);

    /// <summary>
    /// Compares with another <see cref="Unit"/>; always equal.
    /// </summary>
    /// <param name="other">The other value.</param>
    /// <returns>Always <c>0</c>.</returns>
    public int CompareTo(Unit other) => 0;

    /// <summary>
    /// Compares with another object; always equal.
    /// </summary>
    /// <param name="obj">The other object.</param>
    /// <returns>Always <c>0</c>.</returns>
    int IComparable.CompareTo(object? obj) => 0;

    /// <summary>
    /// Tells whether two <see cref="Unit"/> values are equal; always <see langword="true"/>.
    /// </summary>
    /// <param name="other">The other value.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public bool Equals(Unit other) => true;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Unit;

    /// <inheritdoc />
    public override int GetHashCode() => 0;

    /// <summary>
    /// Returns <c>()</c>.
    /// </summary>
    /// <returns>The text <c>()</c>.</returns>
    public override string ToString() => "()";

    /// <summary>
    /// Equality; always <see langword="true"/>.
    /// </summary>
    /// <param name="first">The first value.</param>
    /// <param name="second">The second value.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public static bool operator ==(Unit first, Unit second) => true;

    /// <summary>
    /// Inequality; always <see langword="false"/>.
    /// </summary>
    /// <param name="first">The first value.</param>
    /// <param name="second">The second value.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public static bool operator !=(Unit first, Unit second) => false;
}
