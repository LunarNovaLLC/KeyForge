using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Velopack.Locators;

namespace KeyForge.App;

public static class DesktopShortcutService
{
    private const string ShortcutName = "KeyForge.lnk";

    public static bool ShouldAutoApply()
    {
        try
        {
            return VelopackLocator.Current.CurrentlyInstalledVersion is not null &&
                   !VelopackLocator.Current.IsPortable;
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(bool createShortcut)
    {
        try
        {
            if (createShortcut)
            {
                Create();
            }
            else
            {
                Remove();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to apply desktop shortcut setting.", ex);
        }
    }

    private static void Create()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return;
        }

        var shortcutPath = GetShortcutPath();
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return;
            }

            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);
            if (shortcut is null)
            {
                return;
            }

            var shortcutType = shortcut.GetType();
            SetShortcutProperty(shortcutType, shortcut, "TargetPath", executablePath);
            SetShortcutProperty(shortcutType, shortcut, "WorkingDirectory", Path.GetDirectoryName(executablePath) ?? string.Empty);
            SetShortcutProperty(shortcutType, shortcut, "Description", "KeyForge");
            SetShortcutProperty(shortcutType, shortcut, "IconLocation", $"{executablePath},0");
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, binder: null, target: shortcut, args: []);
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static void Remove()
    {
        var shortcutPath = GetShortcutPath();
        if (File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
        }
    }

    private static string GetShortcutPath()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return Path.Combine(desktop, ShortcutName);
    }

    private static void SetShortcutProperty(Type shortcutType, object shortcut, string propertyName, string value)
    {
        shortcutType.InvokeMember(
            propertyName,
            BindingFlags.SetProperty,
            binder: null,
            target: shortcut,
            args: [value]);
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
