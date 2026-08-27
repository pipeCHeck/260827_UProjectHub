using UProjectHub.Core.Time;

namespace UProjectHub.Core.Tests.Time;

public sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
