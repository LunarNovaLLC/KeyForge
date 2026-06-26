using System.Drawing.Imaging;
using System.IO;
using KeyForge.Core.Models;
using DrawingIcon = System.Drawing.Icon;

namespace KeyForge.App;

internal static class ProfileAssetService
{
    public static string BackgroundStorageDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KeyForge",
        "backgrounds");

    public static string CopyArtwork(KeyForgeProfile profile, string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".png";
        }

        var fileName = $"background-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{extension}";
        var destination = Path.Combine(GetProfileBackgroundDirectory(profile.ProfileId), fileName);
        File.Copy(sourcePath, destination, overwrite: true);
        return destination;
    }

    public static string? TryExtractIcon(KeyForgeProfile profile, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            using var icon = DrawingIcon.ExtractAssociatedIcon(executablePath);
            if (icon is null)
            {
                return null;
            }

            using var bitmap = icon.ToBitmap();
            var destination = Path.Combine(GetProfileAssetDirectory(profile.ProfileId), "icon.png");
            bitmap.Save(destination, ImageFormat.Png);
            return destination;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to extract icon from '{executablePath}'.", ex);
            return null;
        }
    }

    private static string GetProfileAssetDirectory(string profileId)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KeyForge",
            "assets",
            "profiles",
            SanitizeProfileId(profileId));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string GetProfileBackgroundDirectory(string profileId)
    {
        var directory = Path.Combine(
            BackgroundStorageDirectory,
            "profiles",
            SanitizeProfileId(profileId));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string SanitizeProfileId(string profileId)
    {
        var safeId = string.Concat(profileId.Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'));
        return string.IsNullOrWhiteSpace(safeId) ? "profile" : safeId;
    }
}
