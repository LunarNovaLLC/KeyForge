using System.Text.Json;
using KeyForge.Core.Models;

namespace KeyForge.Storage;

public sealed class JsonProfileRepository : IProfileRepository
{
    private readonly string _profilesDirectory;

    public JsonProfileRepository(string? rootDirectory = null)
    {
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KeyForge");
        _profilesDirectory = Path.Combine(root, "profiles");
    }

    public async Task<IReadOnlyList<KeyForgeProfile>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_profilesDirectory);

        var profiles = new List<KeyForgeProfile>();
        foreach (var file in Directory.EnumerateFiles(_profilesDirectory, "*.json"))
        {
            await using var stream = File.OpenRead(file);
            var profile = await JsonSerializer.DeserializeAsync<KeyForgeProfile>(
                stream,
                JsonStorageOptions.Default,
                cancellationToken);
            if (profile is not null)
            {
                profiles.Add(profile);
            }
        }

        return profiles
            .OrderBy(profile => profile.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task SaveAsync(KeyForgeProfile profile, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_profilesDirectory);
        profile.ModifiedAt = DateTimeOffset.UtcNow;
        var destination = GetProfilePath(profile.ProfileId);
        await WriteJsonAtomicallyAsync(destination, profile, cancellationToken);
    }

    public Task DeleteAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var path = GetProfilePath(profileId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public async Task<KeyForgeProfile> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(sourcePath);
        var profile = await JsonSerializer.DeserializeAsync<KeyForgeProfile>(
            stream,
            JsonStorageOptions.Default,
            cancellationToken);

        if (profile is null)
        {
            throw new InvalidDataException("The selected file is not a KeyForge profile.");
        }

        if (string.IsNullOrWhiteSpace(profile.ProfileId))
        {
            profile.ProfileId = Guid.NewGuid().ToString("n");
        }

        await SaveAsync(profile, cancellationToken);
        return profile;
    }

    public Task ExportAsync(KeyForgeProfile profile, string destinationPath, CancellationToken cancellationToken = default)
    {
        return WriteJsonAtomicallyAsync(destinationPath, profile, cancellationToken);
    }

    private string GetProfilePath(string profileId)
    {
        var safeId = string.Concat(profileId.Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'));
        return Path.Combine(_profilesDirectory, $"{safeId}.json");
    }

    private static async Task WriteJsonAtomicallyAsync<T>(string destinationPath, T value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{destinationPath}.{Guid.NewGuid():n}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonStorageOptions.Default, cancellationToken);
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        File.Move(tempPath, destinationPath);
    }
}
