using System.Reflection;

namespace KeyForge.App;

internal static class DistributionInfo
{
    public const string PackageId = "KeyForge";
    public const string PackageTitle = "KeyForge";
    public const string ReleaseChannel = "win";
    public static readonly TimeSpan AutoUpdateCheckInterval = TimeSpan.FromDays(1);

    public static string ReleaseRepositoryUrl
    {
        get
        {
            var environmentUrl = Environment.GetEnvironmentVariable("KEYFORGE_UPDATE_REPOSITORY_URL");
            if (IsUsableRepositoryUrl(environmentUrl))
            {
                return environmentUrl!;
            }

            var metadataUrl = typeof(DistributionInfo).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => string.Equals(
                    attribute.Key,
                    "KeyForgeReleaseRepositoryUrl",
                    StringComparison.OrdinalIgnoreCase))
                ?.Value;

            return metadataUrl ?? string.Empty;
        }
    }

    public static bool IsUsableRepositoryUrl(string? repositoryUrl) =>
        Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Length >= 2;
}
