namespace UProjectHub.Windows.Launching;

public interface IWebUrlLauncher
{
    LaunchResult Open(string url);
}

public sealed class WebUrlLauncher(IProcessLauncher processLauncher)
    : IWebUrlLauncher
{
    private readonly IProcessLauncher _processLauncher = processLauncher
        ?? throw new ArgumentNullException(nameof(processLauncher));

    public LaunchResult Open(string url)
    {
        var normalized = NormalizeSafeUrl(url);
        return normalized is null
            ? LaunchResult.Failed("Only safe HTTP or HTTPS URLs can be opened.")
            : _processLauncher.Launch(new ProcessRequest(
                normalized,
                useShellExecute: true));
    }

    internal static string? NormalizeSafeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps
                && uri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    internal static string RedactCredentialsForDisplay(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.UserInfo))
        {
            return value;
        }

        var sanitized = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
        };
        return sanitized.Uri.AbsoluteUri;
    }
}
