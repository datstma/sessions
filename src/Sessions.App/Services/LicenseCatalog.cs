using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sessions.App.Services;

public sealed record LicenseDocument(string Title, string Path, bool IsPackageInventory = false);

public sealed record LicenseText(string? Text, string? Problem);

/// <summary>
/// Finds the license texts that ship beside the app. Published builds add a <c>licenses</c> folder
/// (see scripts/Collect-ReleaseNotices.ps1); source builds carry only the bundled font license there.
/// </summary>
public static class LicenseCatalog
{
    internal const long MaximumBytes = 2 * 1024 * 1024;
    private static readonly string[] NoticeExtensions = [".txt", ".md", ""];

    public static IReadOnlyList<LicenseDocument> Discover(string installFolder)
    {
        var documents = new List<LicenseDocument> { new("Sessions: GNU General Public License v3", Path.Combine(installFolder, "LICENSE")) };
        var notices = Path.Combine(installFolder, "licenses");
        var inventory = Path.Combine(notices, "dependencies.json");
        if (File.Exists(inventory)) documents.Add(new("Included packages", inventory, IsPackageInventory: true));
        var summary = Path.Combine(installFolder, "THIRD-PARTY-NOTICES.txt");
        if (File.Exists(summary)) documents.Add(new("About these notices", summary));
        try
        {
            if (Directory.Exists(notices))
                documents.AddRange(Directory.EnumerateFiles(notices, "*", SearchOption.AllDirectories)
                    .Where(path => NoticeExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .Select(path => new LicenseDocument(string.Join(" · ", Path.GetRelativePath(notices, path).Split(Path.DirectorySeparatorChar)), path))
                    .OrderBy(document => document.Title, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        return documents;
    }

    public static async Task<LicenseText> ReadAsync(LicenseDocument document, CancellationToken cancellationToken = default)
    {
        try
        {
            if (new FileInfo(document.Path).Length > MaximumBytes)
                return new(null, $"This file is too large to show here. It's at {document.Path}");
            var text = await File.ReadAllTextAsync(document.Path, cancellationToken);
            return new(document.IsPackageInventory ? DescribePackages(text) : text, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(null, $"Couldn't read this file. It's at {document.Path}");
        }
    }

    private static string DescribePackages(string json)
    {
        using var inventory = JsonDocument.Parse(json);
        // PowerShell writes a single package as an object rather than a one-item array.
        var packages = inventory.RootElement.ValueKind == JsonValueKind.Array
            ? inventory.RootElement.EnumerateArray().ToArray() : [inventory.RootElement];
        var text = new StringBuilder("Packages included in this build of Sessions and the license each declares. Their notice files are listed separately.");
        foreach (var package in packages)
        {
            string Value(string name) => package.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
            var notices = package.TryGetProperty("packageNotices", out var files) && files.ValueKind == JsonValueKind.Array
                ? string.Join(", ", files.EnumerateArray().Select(file => file.GetString())) : "";
            text.Append("\n\n").Append(Value("package").Replace('/', ' '))
                .Append("\nLicense: ").Append(Value("license") is { Length: > 0 } license ? license : "not declared; see the project page")
                .Append(Value("projectUrl") is { Length: > 0 } url ? "\nProject: " + url : "")
                .Append("\nNotice files: ").Append(notices.Length > 0 ? notices : "none in the package");
        }
        return text.ToString();
    }
}
