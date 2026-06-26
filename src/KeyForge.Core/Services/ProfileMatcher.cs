using KeyForge.Core.Models;

namespace KeyForge.Core.Services;

public static class ProfileMatcher
{
    public static KeyForgeProfile? SelectActiveProfile(IEnumerable<KeyForgeProfile> profiles, ActiveWindowInfo activeWindow)
    {
        return SelectActiveProfiles(profiles, activeWindow).FirstOrDefault();
    }

    public static IReadOnlyList<KeyForgeProfile> SelectActiveProfiles(IEnumerable<KeyForgeProfile> profiles, ActiveWindowInfo activeWindow)
    {
        var candidates = profiles.Where(profile => profile.Enabled && profile.Mode != ProfileMode.Off).ToList();

        var activeProfiles = new List<KeyForgeProfile>();
        if (!string.IsNullOrWhiteSpace(activeWindow.ExecutableName) ||
            !string.IsNullOrWhiteSpace(activeWindow.ProcessName))
        {
            activeProfiles.AddRange(candidates
                .Where(profile => profile.Mode == ProfileMode.Auto && Matches(profile, activeWindow))
                .OrderByDescending(profile => !string.IsNullOrWhiteSpace(profile.Target.WindowTitleContains))
                .ThenBy(profile => profile.ProfileName, StringComparer.OrdinalIgnoreCase));
        }

        activeProfiles.AddRange(candidates
            .Where(profile => profile.Mode == ProfileMode.AlwaysOn)
            .OrderBy(profile => profile.ProfileName, StringComparer.OrdinalIgnoreCase));

        return activeProfiles;
    }

    public static bool Matches(KeyForgeProfile profile, ActiveWindowInfo activeWindow)
    {
        if (profile.Mode != ProfileMode.Auto || string.IsNullOrWhiteSpace(profile.Target.Exe))
        {
            return false;
        }

        var targetExe = Path.GetFileName(profile.Target.Exe);
        var activeExe = Path.GetFileName(activeWindow.ExecutableName);
        var processExe = activeWindow.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? activeWindow.ProcessName
            : $"{activeWindow.ProcessName}.exe";

        var exeMatches =
            string.Equals(targetExe, activeExe, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(targetExe, processExe, StringComparison.OrdinalIgnoreCase);

        if (!exeMatches)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(profile.Target.WindowTitleContains) ||
               activeWindow.WindowTitle.Contains(profile.Target.WindowTitleContains, StringComparison.OrdinalIgnoreCase);
    }
}
