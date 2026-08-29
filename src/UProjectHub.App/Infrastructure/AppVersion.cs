using System.Reflection;

namespace UProjectHub.App.Infrastructure;

public static class AppVersion
{
    public static string Display { get; } = GetDisplayVersion();

    private static string GetDisplayVersion()
    {
        var informationalVersion = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return string.IsNullOrWhiteSpace(informationalVersion)
            ? "v0.0.0 r"
            : $"v{informationalVersion.Split('+')[0]}";
    }
}
