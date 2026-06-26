using KeyForge.Core.Models;

namespace KeyForge.Process;

public interface IForegroundAppDetector
{
    ActiveWindowInfo GetActiveWindow();

    IReadOnlyList<ActiveWindowInfo> GetOpenWindows();
}
