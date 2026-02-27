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
        Size = new Size(420, 300);
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
            Size = new Size(380, 60)
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
            Location = new Point(12, 85),
            Size = new Size(380, 100)
        };

        _lblContextMenuStatus = new Label
        {
            Location = new Point(15, 25),
            AutoSize = true,
            Text = ContextMenuRegistrar.IsRegistered()
                ? "✅ Context menu entries are registered."
                : "❌ Context menu entries are NOT registered."
        };

        _btnRegisterContextMenu = new Button
        {
            Text = "Register",
            Location = new Point(15, 55),
            Size = new Size(100, 30)
        };
        _btnRegisterContextMenu.Click += (_, _) =>
        {
            ContextMenuRegistrar.Register();
            UpdateContextMenuStatus();
            MessageBox.Show("Context menu entries registered successfully!",
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        _btnUnregisterContextMenu = new Button
        {
            Text = "Unregister",
            Location = new Point(125, 55),
            Size = new Size(100, 30)
        };
        _btnUnregisterContextMenu.Click += (_, _) =>
        {
            ContextMenuRegistrar.Unregister();
            UpdateContextMenuStatus();
            MessageBox.Show("Context menu entries removed.",
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        grpContext.Controls.AddRange([_lblContextMenuStatus, _btnRegisterContextMenu, _btnUnregisterContextMenu]);

        // --- About label ---
        var lblAbout = new Label
        {
            Text = "ReleaseMyFiles v1.0\nDetect and release file locks from Windows Explorer.",
            Location = new Point(12, 200),
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        };

        // --- Close button ---
        var btnClose = new Button
        {
            Text = "Close",
            Location = new Point(305, 230),
            Size = new Size(85, 30),
            DialogResult = DialogResult.OK
        };

        Controls.AddRange([grpStartup, grpContext, lblAbout, btnClose]);
        AcceptButton = btnClose;
    }

    private void UpdateContextMenuStatus()
    {
        _lblContextMenuStatus.Text = ContextMenuRegistrar.IsRegistered()
            ? "✅ Context menu entries are registered."
            : "❌ Context menu entries are NOT registered.";
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
