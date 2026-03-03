using System.Runtime.InteropServices;

namespace NorthernRange.Config;

public static class AppPaths
{
    private const string AppName = "northernrange";

    public static string GetConfigDir()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppName);

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            AppName);
    }

    public static string GetConfigFilePath() =>
        Path.Combine(GetConfigDir(), "config.json");

    public static string GetClientSecretsPath() =>
        Path.Combine(GetConfigDir(), "client_secrets.json");

    public static string GetTokenStorePath() =>
        Path.Combine(GetConfigDir(), "tokens");

    public static string GetLogDir() =>
        Path.Combine(GetConfigDir(), "logs");

    public static string GetUserInfoPath() =>
        Path.Combine(GetTokenStorePath(), "user_info.json");

    public static void EnsureDirectoriesExist()
    {
        var configDir = GetConfigDir();
        var logDir = GetLogDir();
        var tokenDir = GetTokenStorePath();

        foreach (var dir in new[] { configDir, logDir, tokenDir })
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                TrySetUnixPermissions(dir);
            }
        }
    }

    private static void TrySetUnixPermissions(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch
        {
            Console.Error.WriteLine($"Warning: Could not set permissions on {path}. Ensure it is accessible only by your user.");
        }
    }
}
