namespace UProjectHub.Core.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
