namespace UProjectHub.Windows.Storage;

public sealed record AppDataPaths(
    string RootDirectory,
    string SettingsFile,
    string SettingsBackupFile,
    string ProjectCacheFile,
    string EngineCacheFile,
    string LogDirectory,
    string LogFile);
