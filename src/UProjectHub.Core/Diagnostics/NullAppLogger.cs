namespace UProjectHub.Core.Diagnostics;

public sealed class NullAppLogger : IAppLogger
{
    public void Info(string message)
    {
    }

    public void Warning(string message)
    {
    }

    public void Error(string message)
    {
    }

    public void Error(string message, Exception exception)
    {
    }
}
