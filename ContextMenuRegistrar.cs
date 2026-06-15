using System.Diagnostics;
using Microsoft.Win32;

namespace ReleaseMyFiles;

/// <summary>
/// Registers and unregisters Windows Explorer context menu entries for files and folders.
/// </summary>
public static class ContextMenuRegistrar
{
    private const string ReleaseMenuName = "ReleaseMyFiles_Release";
    private const string WhoHoldingMenuName = "ReleaseMyFiles_WhoHolding";
    private const string ReleaseCommandClsid = "{287107F8-274A-48AD-82BB-9B41D97A5F81}";
    private const string WhoHoldingCommandClsid = "{BC852822-647D-46D4-BF6D-A7B1BBE2259E}";
    private const string ExplorerCommandHandlerValue = "ExplorerCommandHandler";
    private const string ShellExtensionFileName = "ReleaseMyFiles.ShellExtension.comhost.dll";
    private const string PackageName = "AnhBuiDeveloper.ReleaseMyFiles";

    private static readonly string[] RegistryRoots =
    [
        @"*\shell",           // Files
        @"Directory\shell"    // Folders
    ];

    /// <summary>
    /// Registers the context menu entries for the Windows 11 modern menu.
    /// </summary>
    /// <remarks>
    /// Only the per-user sparse MSIX package is registered. The legacy
    /// machine-wide <c>*\shell</c> commands are intentionally not registered:
    /// Windows already mirrors the package's modern verbs into the classic
    /// ("Show more options") menu, so registering both produced duplicate
    /// entries there. Use <see cref="UnregisterMachineShellCommands"/> to clean
    /// up legacy entries left by earlier versions.
    /// </remarks>
    public static void Register()
    {
        RegisterWindows11Package();
    }

    /// <summary>
    /// Registers the machine-wide COM classes and legacy shell commands.
    /// This operation requires administrator permission.
    /// </summary>
    public static void RegisterMachineShellCommands()
    {
        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to locate the application executable.");
        string extensionPath = Path.Combine(
            Path.GetDirectoryName(executablePath)
                ?? throw new InvalidOperationException("Unable to locate the application directory."),
            ShellExtensionFileName);

        if (!File.Exists(extensionPath))
            throw new FileNotFoundException("Windows 11 shell extension was not found.", extensionPath);

        RegisterComClass(ReleaseCommandClsid, extensionPath);
        RegisterComClass(WhoHoldingCommandClsid, extensionPath);

        foreach (var root in RegistryRoots)
        {
            RegisterExplorerCommand(root, ReleaseMenuName, "Release me",
                ReleaseCommandClsid, executablePath);
            RegisterExplorerCommand(root, WhoHoldingMenuName, "Who is holding this?",
                WhoHoldingCommandClsid, executablePath);
        }
    }

    /// <summary>
    /// Removes the context menu entries.
    /// </summary>
    /// <remarks>
    /// The per-user package is removed first (no elevation needed), so that
    /// even if removing the legacy machine-wide entries throws for lack of
    /// administrator permission, the modern menu is already gone. Callers that
    /// catch the resulting <see cref="UnauthorizedAccessException"/> /
    /// <see cref="System.Security.SecurityException"/> should re-run elevated
    /// to finish the legacy cleanup.
    /// </remarks>
    public static void Unregister()
    {
        UnregisterWindows11Package();
        UnregisterMachineShellCommands();
    }

    /// <summary>
    /// Removes the machine-wide COM classes and legacy shell commands.
    /// This operation requires administrator permission.
    /// </summary>
    public static void UnregisterMachineShellCommands()
    {
        foreach (var root in RegistryRoots)
        {
            Registry.ClassesRoot.DeleteSubKeyTree($@"{root}\{ReleaseMenuName}", false);
            Registry.ClassesRoot.DeleteSubKeyTree($@"{root}\{WhoHoldingMenuName}", false);
        }

        Registry.ClassesRoot.DeleteSubKeyTree($@"CLSID\{ReleaseCommandClsid}", false);
        Registry.ClassesRoot.DeleteSubKeyTree($@"CLSID\{WhoHoldingCommandClsid}", false);
    }

    public static void RegisterWindows11Package()
    {
        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to locate the application executable.");
        RegisterSparsePackage(Path.GetDirectoryName(executablePath)!);
    }

    public static void UnregisterWindows11Package()
    {
        RunPowerShell(
            $"Get-AppxPackage -Name '{PackageName}' | Remove-AppxPackage");
    }

    /// <summary>
    /// Checks if the context menus are present in any form, i.e. the Windows 11
    /// package is installed or stale legacy machine-wide entries remain. Used to
    /// drive the registered/not-registered status shown to the user.
    /// </summary>
    public static bool IsRegistered() => IsPackageRegistered() || HasLegacyMachineEntries();

    /// <summary>
    /// Checks whether the Windows 11 sparse package is installed (the modern
    /// context menu).
    /// </summary>
    public static bool IsPackageRegistered()
    {
        try
        {
            return RunPowerShellQuery(
                $"if (Get-AppxPackage -Name '{PackageName}') {{ 'yes' }}").Trim() == "yes";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether any stale legacy machine-wide shell command or COM class
    /// remains from earlier versions. Reading HKLM does not require elevation;
    /// removing the entries does.
    /// </summary>
    public static bool HasLegacyMachineEntries()
    {
        using var classes = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes");
        if (classes is null)
            return false;

        foreach (var root in RegistryRoots)
        {
            if (classes.OpenSubKey($@"{root}\{ReleaseMenuName}") is { } a) { a.Dispose(); return true; }
            if (classes.OpenSubKey($@"{root}\{WhoHoldingMenuName}") is { } b) { b.Dispose(); return true; }
        }

        if (classes.OpenSubKey($@"CLSID\{ReleaseCommandClsid}") is { } c) { c.Dispose(); return true; }
        if (classes.OpenSubKey($@"CLSID\{WhoHoldingCommandClsid}") is { } d) { d.Dispose(); return true; }

        return false;
    }

    private static void RegisterComClass(string clsid, string extensionPath)
    {
        using var key = Registry.ClassesRoot.CreateSubKey($@"CLSID\{clsid}\InprocServer32")
            ?? throw new InvalidOperationException($"Unable to register COM class {clsid}.");
        key.SetValue("", extensionPath);
        key.SetValue("ThreadingModel", "Both");
    }

    private static void RegisterExplorerCommand(
        string root, string menuName, string title, string clsid, string iconPath)
    {
        using var key = Registry.ClassesRoot.CreateSubKey($@"{root}\{menuName}")
            ?? throw new InvalidOperationException($"Unable to register shell command {menuName}.");
        key.SetValue("", title);
        key.SetValue("Icon", iconPath);
        key.SetValue(ExplorerCommandHandlerValue, clsid);
        key.DeleteSubKeyTree("command", false);
    }

    private static void RegisterSparsePackage(string applicationDirectory)
    {
        string manifestPath = Path.Combine(applicationDirectory, "Packaging", "AppxManifest.xml");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Windows 11 sparse package manifest was not found.", manifestPath);

        RunPowerShell(
            $"$package = Get-AppxPackage -Name '{PackageName}'; " +
            $"if (-not $package -or $package.Status -ne 'Ok' -or " +
            $"$package.InstallLocation -ne '{EscapePowerShell(Path.GetDirectoryName(manifestPath)!)}') {{ " +
            $"Add-AppxPackage -Register '{EscapePowerShell(manifestPath)}' " +
            $"-ExternalLocation '{EscapePowerShell(applicationDirectory)}' -ForceUpdateFromAnyVersion }}");
    }

    private static void RunPowerShell(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add($"$ErrorActionPreference = 'Stop'; {command}");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start PowerShell.");
        string error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30_000))
        {
            try { process.Kill(entireProcessTree: true); }
            catch { }
            throw new TimeoutException("Windows package registration timed out.");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException(error.Trim());
    }

    private static string RunPowerShellQuery(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add($"$ErrorActionPreference = 'Stop'; {command}");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start PowerShell.");
        string output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(30_000))
        {
            try { process.Kill(entireProcessTree: true); }
            catch { }
            throw new TimeoutException("Windows package query timed out.");
        }

        return output;
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''");
}
