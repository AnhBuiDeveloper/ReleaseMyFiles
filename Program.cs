using System.IO.Pipes;

namespace ReleaseMyFiles;

static class Program
{
    private const string MutexName = "Global\\ReleaseMyFiles_SingleInstance";
    internal const string PipeName = "ReleaseMyFiles_Pipe";
    internal const string RegisterContextMenuArgument = "--register-context-menu";
    internal const string UnregisterContextMenuArgument = "--unregister-context-menu";

    [STAThread]
    static void Main(string[] args)
    {
        // A stable, versioned shell identity prevents Windows from reusing an
        // older cached taskbar icon for this executable path.
        NativeMethods.SetCurrentProcessExplicitAppUserModelID(
            "AnhBuiDeveloper.ReleaseMyFiles.IconV2");

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        if (args.Length == 1 &&
            (args[0].Equals(RegisterContextMenuArgument, StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals(UnregisterContextMenuArgument, StringComparison.OrdinalIgnoreCase)))
        {
            ChangeContextMenuRegistration(args[0]);
            return;
        }

        // Handle command-line invocations from Explorer context menu
        if (args.Length >= 2)
        {
            string command = args[0].ToLowerInvariant();
            string path = args[1];

            if (command is "--release" or "--whohold")
            {
                // Send the command to the running tray instance via named pipe
                SendCommandToRunningInstance($"{command}|{path}");
                return;
            }
        }

        // Single-instance check for tray mode
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("ReleaseMyFiles is already running in the system tray.",
                "ReleaseMyFiles", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.Run(new TrayApplicationContext());
    }

    private static void ChangeContextMenuRegistration(string argument)
    {
        try
        {
            bool register = argument.Equals(
                RegisterContextMenuArgument, StringComparison.OrdinalIgnoreCase);
            // Registration is per-user (sparse package) and needs no elevation.
            // The elevated path exists only to clean up legacy machine-wide
            // shell commands left by earlier versions.
            if (!register)
                ContextMenuRegistrar.UnregisterMachineShellCommands();
        }
        catch
        {
            Environment.ExitCode = 1;
        }
    }

    /// <summary>
    /// Sends a command string to the running tray instance via named pipe.
    /// If no instance is running, shows an error and exits.
    /// </summary>
    private static void SendCommandToRunningInstance(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000); // wait up to 3 seconds
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(message);
        }
        catch (TimeoutException)
        {
            MessageBox.Show(
                "ReleaseMyFiles is not running.\nPlease start the application first.",
                "ReleaseMyFiles", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to communicate with ReleaseMyFiles:\n{ex.Message}",
                "ReleaseMyFiles", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
