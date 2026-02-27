using System.IO.Pipes;

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

        _notifyIcon.DoubleClick += OnOptions;

        // Auto-register context menus if not already done
        if (!ContextMenuRegistrar.IsRegistered())
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
                    using var server = new NamedPipeServerStream(
                        Program.PipeName, PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

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
        using var form = new OptionsForm();
        form.ShowDialog();
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _cts.Cancel();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
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
            _hiddenForm.Dispose();
        }
        base.Dispose(disposing);
    }
}
