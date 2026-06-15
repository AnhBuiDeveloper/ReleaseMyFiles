using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace ReleaseMyFiles;

/// <summary>
/// Application context that manages the system tray icon, its context menu,
/// and listens for IPC commands from Explorer context menu invocations.
/// Uses a hidden Form to reliably marshal pipe messages to the UI thread.
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly CancellationTokenSource _cts = new();
    private readonly Form _hiddenForm;
    private OptionsForm? _optionsForm;

    public TrayApplicationContext()
    {
        // Hidden form used for Invoke/BeginInvoke from background thread
        _hiddenForm = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized };
        _hiddenForm.Load += (_, _) => _hiddenForm.Visible = false;
        _hiddenForm.Show();
        _hiddenForm.Hide();

        // Load embedded icon
        Icon? appIcon = null;
        try
        {
            using var stream = typeof(TrayApplicationContext).Assembly
                .GetManifestResourceStream("ReleaseMyFiles.Resources.app.ico");
            if (stream != null)
                appIcon = new Icon(stream);
        }
        catch { }

        // Build context menu
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Options", null, OnOptions);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, OnExit);

        // Create tray icon
        _notifyIcon = new NotifyIcon
        {
            Icon = appIcon ?? SystemIcons.Application,
            Text = "ReleaseMyFiles",
            Visible = true,
            ContextMenuStrip = contextMenu
        };

        _notifyIcon.MouseClick += OnTrayIconMouseClick;

        // Auto-register the modern context menu package if not already present.
        // Keyed off the package (not IsRegistered) so stale legacy entries from
        // an older version don't suppress installing the modern menu.
        if (!ContextMenuRegistrar.IsPackageRegistered())
        {
            try
            {
                ContextMenuRegistrar.Register();
            }
            catch (UnauthorizedAccessException)
            {
                // Will be handled via Options form
            }
        }

        // Start listening for IPC commands
        StartPipeServer();
    }

    /// <summary>
    /// Starts a background named pipe server that continuously listens for
    /// commands from other instances (Explorer context menu invocations).
    /// </summary>
    private void StartPipeServer()
    {
        Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    using var server = CreateSecuredPipeServer();

                    await server.WaitForConnectionAsync(_cts.Token);

                    using var reader = new StreamReader(server);
                    var message = await reader.ReadLineAsync();

                    if (!string.IsNullOrEmpty(message))
                    {
                        var parts = message.Split('|', 2);
                        if (parts.Length == 2)
                        {
                            string command = parts[0];
                            string path = parts[1];

                            // Marshal to UI thread via the hidden form
                            _hiddenForm.BeginInvoke(() => HandleCommand(command, path));
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    try { await Task.Delay(200, _cts.Token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }, _cts.Token);
    }

    // Security descriptor (SDDL):
    //   D:(A;;0x12019f;;;AU)  -> grant Authenticated Users generic read/write
    //   S:(ML;;NW;;;LW)       -> Low mandatory-integrity label, No-Write-Up
    // The Low label lets medium-integrity clients (Explorer context-menu
    // invocations) connect even when this tray instance runs elevated; the
    // label must be set at creation time, so we create the pipe via the Win32
    // CreateNamedPipe directly. Without it the elevated server rejects medium
    // clients with "Access to the path is denied".
    private const string PipeSddl = "D:(A;;0x12019f;;;AU)S:(ML;;NW;;;LW)";

    /// <summary>
    /// Creates a one-shot named pipe server instance secured with
    /// <see cref="PipeSddl"/> so it is reachable across integrity levels.
    /// </summary>
    private static NamedPipeServerStream CreateSecuredPipeServer()
    {
        if (!NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptor(
                PipeSddl, 1, out IntPtr pSecurityDescriptor, out _))
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                "Failed to build pipe security descriptor.");
        }

        try
        {
            var sa = new NativeMethods.SECURITY_ATTRIBUTES
            {
                nLength = Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(),
                lpSecurityDescriptor = pSecurityDescriptor,
                bInheritHandle = 0
            };

            var handle = NativeMethods.CreateNamedPipe(
                @"\\.\pipe\" + Program.PipeName,
                NativeMethods.PIPE_ACCESS_INBOUND | NativeMethods.FILE_FLAG_OVERLAPPED,
                NativeMethods.PIPE_TYPE_BYTE,
                NativeMethods.PIPE_UNLIMITED_INSTANCES,
                0, 0, 0, ref sa);

            if (handle.IsInvalid)
            {
                throw new System.ComponentModel.Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Failed to create named pipe.");
            }

            // isAsync: true (FILE_FLAG_OVERLAPPED), isConnected: false
            return new NamedPipeServerStream(PipeDirection.In, true, false, handle);
        }
        finally
        {
            NativeMethods.LocalFree(pSecurityDescriptor);
        }
    }

    private void HandleCommand(string command, string path)
    {
        switch (command)
        {
            case "--release":
                HandleRelease(path);
                break;

            case "--whohold":
                var form = new WhoHoldingForm(path);
                form.Show();
                break;
        }
    }

    private void HandleRelease(string path)
    {
        var processes = FileLockDetector.GetLockingProcesses(path);

        if (processes.Count == 0)
        {
            MessageBox.Show($"No processes are locking:\n{path}",
                "ReleaseMyFiles", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string processList = string.Join("\n",
            processes.Select(p => $"  • {p.ProcessName} (PID: {p.ProcessId})"));

        var result = MessageBox.Show(
            $"The following {processes.Count} process(es) are locking:\n{path}\n\n{processList}\n\nKill them all?",
            "ReleaseMyFiles – Release me",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result == DialogResult.Yes)
        {
            int killed = FileLockDetector.KillLockingProcesses(path);
            MessageBox.Show($"Killed {killed} of {processes.Count} process(es).",
                "ReleaseMyFiles", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void OnOptions(object? sender, EventArgs e)
    {
        ShowOptions();
    }

    private void OnTrayIconMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            ShowOptions();
    }

    private void ShowOptions()
    {
        if (_optionsForm is { IsDisposed: false })
        {
            if (_optionsForm.WindowState == FormWindowState.Minimized)
                _optionsForm.WindowState = FormWindowState.Normal;

            _optionsForm.Show();
            _optionsForm.Activate();
            _optionsForm.BringToFront();
            return;
        }

        _optionsForm = new OptionsForm();
        _optionsForm.FormClosed += (_, _) => _optionsForm = null;
        _optionsForm.Show();
        _optionsForm.Activate();
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _cts.Cancel();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _optionsForm?.Close();
        _hiddenForm.Close();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _optionsForm?.Dispose();
            _hiddenForm.Dispose();
        }
        base.Dispose(disposing);
    }
}
