namespace UProjectHub.Core.Diagnostics;

public interface IAppLogger
{
    void Info(string message);

    void Warning(string message);

    void Error(string message);

    void Error(string message, Exception exception);
}
