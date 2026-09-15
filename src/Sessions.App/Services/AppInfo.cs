using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Sessions.App.Services;

/// <summary>Build identity and local locations shown in About. Version and stage come from Directory.Build.props.</summary>
public sealed record AppInfo(string Version, string Stage, string RepositoryUrl, string DataFolder, string InstallFolder)
{
    public static string DefaultDataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sessions");

    public static AppInfo Current { get; } = FromAssembly(typeof(AppInfo).Assembly);

    internal static AppInfo FromAssembly(Assembly assembly)
    {
        // The SDK can append build metadata such as "+commit" to the informational version.
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational?.Split('+')[0] ?? assembly.GetName().Version?.ToString(3) ?? "";
        string Metadata(string key) => assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)?.Value ?? "";
        return new AppInfo(version, Metadata("ReleaseStage"), Metadata("RepositoryUrl").TrimEnd('/'), DefaultDataFolder, AppContext.BaseDirectory);
    }
}
