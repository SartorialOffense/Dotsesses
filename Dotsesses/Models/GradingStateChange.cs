namespace Dotsesses.Models;

/// <summary>
/// The payload of a GradingSession's LastChange notification: the
/// new immutable GradingState plus the object that initiated the
/// change. Read-only consumers ignore Originator; read-and-write
/// consumers (drag handlers) call IsFrom(this) to bail on
/// self-initiated changes. See ADR-0010.
/// </summary>
public sealed record GradingStateChange(object? Originator, GradingState State)
{
    public bool IsFrom(object? obj) => obj is not null && ReferenceEquals(obj, Originator);
}
