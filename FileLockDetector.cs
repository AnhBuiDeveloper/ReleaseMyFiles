using System.Diagnostics;

namespace ReleaseMyFiles;

/// <summary>
/// Information about a process that is locking a file.
/// </summary>
public record LockingProcessInfo(
    int ProcessId,
    string ProcessName,
    string ApplicationType,
    DateTime StartTime);

/// <summary>
/// Detects processes locking a given file or folder using the Windows Restart Manager API.
/// </summary>
public static class FileLockDetector
{
    /// <summary>
    /// Gets all processes that are locking the specified file or any file inside the specified folder.
    /// </summary>
    public static List<LockingProcessInfo> GetLockingProcesses(string path)
    {
        var results = new Dictionary<int, LockingProcessInfo>(); // keyed by PID to deduplicate
        var currentExeName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");

        if (Directory.Exists(path))
        {
            // For folders, check all files in the directory (top level)
            try
            {
                var files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    foreach (var info in GetLockingProcessesForFile(file))
                    {
                        if (!IsOwnProcess(info, currentExeName))
                            results.TryAdd(info.ProcessId, info);
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        else if (File.Exists(path))
        {
            foreach (var info in GetLockingProcessesForFile(path))
            {
                if (!IsOwnProcess(info, currentExeName))
                    results.TryAdd(info.ProcessId, info);
            }
        }

        return results.Values.ToList();
    }

    /// <summary>
    /// Checks if the process belongs to this application (ReleaseMyFiles).
    /// </summary>
    private static bool IsOwnProcess(LockingProcessInfo info, string currentExeName)
    {
        return string.Equals(info.ProcessName, currentExeName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(info.ProcessName, currentExeName + ".exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Queries the Restart Manager for a single file.
    /// </summary>
    private static List<LockingProcessInfo> GetLockingProcessesForFile(string filePath)
    {
        var processes = new List<LockingProcessInfo>();

        int result = NativeMethods.RmStartSession(out uint sessionHandle, 0,
            Guid.NewGuid().ToString());

        if (result != 0)
            return processes;

        try
        {
            string[] resources = [filePath];
            result = NativeMethods.RmRegisterResources(sessionHandle,
                (uint)resources.Length, resources, 0, null, 0, null);

            if (result != 0)
                return processes;

            uint pnProcInfo = 0;
            uint lpdwRebootReasons = NativeMethods.RmRebootReasonNone;

            // First call to get the count
            result = NativeMethods.RmGetList(sessionHandle, out uint pnProcInfoNeeded,
                ref pnProcInfo, null, ref lpdwRebootReasons);

            if (result == NativeMethods.ERROR_MORE_DATA && pnProcInfoNeeded > 0)
            {
                var processInfo = new NativeMethods.RM_PROCESS_INFO[pnProcInfoNeeded];
                pnProcInfo = pnProcInfoNeeded;

                result = NativeMethods.RmGetList(sessionHandle, out pnProcInfoNeeded,
                    ref pnProcInfo, processInfo, ref lpdwRebootReasons);

                if (result == 0)
                {
                    for (int i = 0; i < pnProcInfo; i++)
                    {
                        try
                        {
                            var proc = Process.GetProcessById(processInfo[i].Process.dwProcessId);
                            processes.Add(new LockingProcessInfo(
                                ProcessId: processInfo[i].Process.dwProcessId,
                                ProcessName: processInfo[i].strAppName,
                                ApplicationType: processInfo[i].ApplicationType.ToString(),
                                StartTime: proc.StartTime));
                        }
                        catch (ArgumentException)
                        {
                            // Process no longer exists
                        }
                        catch (InvalidOperationException)
                        {
                            // Process has exited
                        }
                    }
                }
            }
        }
        finally
        {
            NativeMethods.RmEndSession(sessionHandle);
        }

        return processes;
    }

    /// <summary>
    /// Kills all processes that are locking the specified path.
    /// Returns the count of processes killed.
    /// </summary>
    public static int KillLockingProcesses(string path)
    {
        var lockingProcesses = GetLockingProcesses(path);
        int killed = 0;

        foreach (var info in lockingProcesses)
        {
            try
            {
                var process = Process.GetProcessById(info.ProcessId);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
                killed++;
            }
            catch (ArgumentException) { }      // Already exited
            catch (InvalidOperationException) { } // Cannot kill
            catch (System.ComponentModel.Win32Exception) { } // Access denied
        }

        return killed;
    }
}
