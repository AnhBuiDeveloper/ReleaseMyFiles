using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace ReleaseMyFiles;

/// <summary>
/// Options dialog for ReleaseMyFiles settings.
/// </summary>
public class OptionsForm : Form
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ReleaseMyFiles";

    private readonly CheckBox _chkStartWithWindows;
    private readonly Button _btnRegisterContextMenu;
    private readonly Button _btnUnregisterContextMenu;
    private readonly Label _lblContextMenuStatus;

    public OptionsForm()
    {
        Text = "Options – ReleaseMyFiles";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(440, 320);
        MinimumSize = SizeFromClientSize(new Size(440, 320));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        try
        {
            using var stream = typeof(OptionsForm).Assembly
                .GetManifestResourceStream("ReleaseMyFiles.Resources.app.ico");
            if (stream != null) Icon = new Icon(stream);
        }
        catch { }

        // --- Startup group ---
        var grpStartup = new GroupBox
        {
            Text = "Startup",
            Location = new Point(12, 12),
            Size = new Size(416, 68),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _chkStartWithWindows = new CheckBox
        {
            Text = "Start with Windows",
            Location = new Point(15, 25),
            AutoSize = true,
            Checked = IsStartWithWindowsEnabled()
        };
        _chkStartWithWindows.CheckedChanged += ChkStartWithWindows_Changed;
        grpStartup.Controls.Add(_chkStartWithWindows);

        // --- Context Menu group ---
        var grpContext = new GroupBox
        {
            Text = "Explorer Context Menu",
            Location = new Point(12, 92),
            Size = new Size(416, 112),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _lblContextMenuStatus = new Label
        {
            Location = new Point(15, 25),
            AutoSize = true,
            Text = ComputeContextMenuStatusText()
        };

        _btnRegisterContextMenu = new Button
        {
            Text = "Register",
            Location = new Point(15, 55),
            Size = new Size(100, 30)
        };
        _btnRegisterContextMenu.Click += BtnRegisterContextMenu_Click;

        _btnUnregisterContextMenu = new Button
        {
            Text = "Unregister",
            Location = new Point(125, 55),
            Size = new Size(100, 30)
        };
        _btnUnregisterContextMenu.Click += BtnUnregisterContextMenu_Click;

        grpContext.Controls.AddRange([_lblContextMenuStatus, _btnRegisterContextMenu, _btnUnregisterContextMenu]);

        // --- About label ---
        var lblAbout = new Label
        {
            Text = "ReleaseMyFiles v1.0\nDetect and release file locks from Windows Explorer.",
            Location = new Point(12, 232),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom
        };

        // --- Close button ---
        var btnClose = new Button
        {
            Text = "Close",
            Location = new Point(343, 278),
            Size = new Size(85, 30),
            DialogResult = DialogResult.OK,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom
        };

        Controls.AddRange([grpStartup, grpContext, lblAbout, btnClose]);
        AcceptButton = btnClose;
    }

    private async void BtnRegisterContextMenu_Click(object? sender, EventArgs e)
    {
        try
        {
            ContextMenuRegistrar.Register();
            UpdateContextMenuStatus();
            MessageBox.Show("Context menu entries registered successfully.",
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (UnauthorizedAccessException)
        {
            await PromptAndRunAsAdministratorAsync(register: true);
        }
        catch (System.Security.SecurityException)
        {
            await PromptAndRunAsAdministratorAsync(register: true);
        }
        catch (Exception ex)
        {
            ShowContextMenuError(ex);
        }
    }

    private async void BtnUnregisterContextMenu_Click(object? sender, EventArgs e)
    {
        try
        {
            ContextMenuRegistrar.Unregister();
            UpdateContextMenuStatus();
            ShowUnregisterResult();
        }
        catch (UnauthorizedAccessException)
        {
            await PromptAndRunAsAdministratorAsync(register: false);
        }
        catch (System.Security.SecurityException)
        {
            await PromptAndRunAsAdministratorAsync(register: false);
        }
        catch (Exception ex)
        {
            ShowContextMenuError(ex);
        }
    }

    /// <summary>
    /// Reports the outcome of an unregister attempt based on the actual state,
    /// so leftover entries are never reported as a clean removal.
    /// </summary>
    private void ShowUnregisterResult()
    {
        if (ContextMenuRegistrar.HasLegacyMachineEntries())
            MessageBox.Show(
                "Old machine-wide entries could not be removed. Run the application as " +
                "Administrator and try Unregister again.",
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        else
            MessageBox.Show("Context menu entries removed.",
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task PromptAndRunAsAdministratorAsync(bool register)
    {
        var result = MessageBox.Show(
            $"Administrator permission is required to {(register ? "register" : "remove")} " +
            "Explorer context menu entries.\n\nContinue as Administrator?",
            AppName,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
            return;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath
                    ?? throw new InvalidOperationException("Unable to locate the application executable."),
                Arguments = register
                    ? Program.RegisterContextMenuArgument
                    : Program.UnregisterContextMenuArgument,
                UseShellExecute = true,
                Verb = "runas"
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return;

            _btnRegisterContextMenu.Enabled = false;
            _btnUnregisterContextMenu.Enabled = false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    "The Administrator task did not finish within 30 seconds.");
            }

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "The Administrator task failed. Please try again.");

            if (register)
                ContextMenuRegistrar.RegisterWindows11Package();
            else
                ContextMenuRegistrar.UnregisterWindows11Package();

            UpdateContextMenuStatus();
            if (register)
                MessageBox.Show(
                    "Context menu entries registered successfully.",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                ShowUnregisterResult();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // The user cancelled the UAC prompt.
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to update context menu entries:\n{ex.Message}",
                AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _btnRegisterContextMenu.Enabled = true;
            _btnUnregisterContextMenu.Enabled = true;
        }
    }

    private static void ShowContextMenuError(Exception ex)
    {
        MessageBox.Show(
            $"Unable to update context menu entries:\n{ex.Message}",
            AppName,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void UpdateContextMenuStatus()
    {
        _lblContextMenuStatus.Text = ComputeContextMenuStatusText();
    }

    private static string ComputeContextMenuStatusText()
    {
        bool package = ContextMenuRegistrar.IsPackageRegistered();
        bool legacy = ContextMenuRegistrar.HasLegacyMachineEntries();

        if (package && legacy)
            return "⚠ Registered, but old duplicate entries remain. Click Unregister, then Register.";
        if (package)
            return "✅ Context menu entries are registered.";
        if (legacy)
            return "⚠ Old context menu entries remain. Click Unregister to remove them.";
        return "❌ Context menu entries are NOT registered.";
    }

    private void ChkStartWithWindows_Changed(object? sender, EventArgs e)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        if (key == null) return;

        if (_chkStartWithWindows.Checked)
        {
            key.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
    }

    private static bool IsStartWithWindowsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(AppName) != null;
    }
}
