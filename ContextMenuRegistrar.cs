using Microsoft.Win32;

namespace ReleaseMyFiles;

/// <summary>
/// Registers and unregisters Windows Explorer context menu entries for files and folders.
/// </summary>
public static class ContextMenuRegistrar
{
    private const string ReleaseMenuName = "ReleaseMyFiles_Release";
    private const string WhoHoldingMenuName = "ReleaseMyFiles_WhoHolding";

    private static readonly string[] RegistryRoots =
    [
        @"*\shell",           // Files
        @"Directory\shell"    // Folders
    ];

    /// <summary>
    /// Registers the context menu entries in the Windows registry.
    /// </summary>
    public static void Register()
    {
        string exePath = $"\"{Environment.ProcessPath}\"";

        foreach (var root in RegistryRoots)
        {
            // "Release me" entry
            using (var key = Registry.ClassesRoot.CreateSubKey($@"{root}\{ReleaseMenuName}"))
            {
                key.SetValue("", "Release me");
                key.SetValue("Icon", Environment.ProcessPath ?? "");
                using var cmdKey = key.CreateSubKey("command");
                cmdKey.SetValue("", $"{exePath} --release \"%1\"");
            }

            // "Who holding me" entry
            using (var key = Registry.ClassesRoot.CreateSubKey($@"{root}\{WhoHoldingMenuName}"))
            {
                key.SetValue("", "Who holding me");
                key.SetValue("Icon", Environment.ProcessPath ?? "");
                using var cmdKey = key.CreateSubKey("command");
                cmdKey.SetValue("", $"{exePath} --whohold \"%1\"");
            }
        }
    }

    /// <summary>
    /// Removes the context menu entries from the Windows registry.
    /// </summary>
    public static void Unregister()
    {
        foreach (var root in RegistryRoots)
        {
            try { Registry.ClassesRoot.DeleteSubKeyTree($@"{root}\{ReleaseMenuName}", false); }
            catch (Exception) { }

            try { Registry.ClassesRoot.DeleteSubKeyTree($@"{root}\{WhoHoldingMenuName}", false); }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Checks if the context menus are currently registered.
    /// </summary>
    public static bool IsRegistered()
    {
        using var key = Registry.ClassesRoot.OpenSubKey($@"*\shell\{ReleaseMenuName}");
        return key != null;
    }
}
