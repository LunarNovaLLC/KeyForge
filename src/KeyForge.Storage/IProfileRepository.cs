using KeyForge.Core.Models;

namespace KeyForge.Storage;

public interface IProfileRepository
{
    Task<IReadOnlyList<KeyForgeProfile>> LoadAllAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(KeyForgeProfile profile, CancellationToken cancellationToken = default);

    Task DeleteAsync(string profileId, CancellationToken cancellationToken = default);

    Task<KeyForgeProfile> ImportAsync(string sourcePath, CancellationToken cancellationToken = default);

    Task ExportAsync(KeyForgeProfile profile, string destinationPath, CancellationToken cancellationToken = default);
}
