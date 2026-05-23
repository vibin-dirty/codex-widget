namespace CodexWidget.Core;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
