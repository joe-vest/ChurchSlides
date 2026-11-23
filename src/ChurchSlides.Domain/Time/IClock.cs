namespace ChurchSlides.Domain.Time;

/// <summary>All time access goes through this (CLAUDE.md rule 10), so tests can control it.</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
