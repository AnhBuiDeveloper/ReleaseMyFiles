namespace ReleaseMyFiles;

/// <summary>
/// Form that displays all processes locking a given file or folder.
/// </summary>
public class WhoHoldingForm : Form
{
    private readonly string _targetPath;
    private readonly DataGridView _grid;
    private readonly Button _btnKillSelected;
    private readonly Button _btnKillAll;
    private readonly Button _btnRefresh;
    private readonly Label _lblPath;
    private readonly Label _lblStatus;

    public WhoHoldingForm(string targetPath)
    {
        _targetPath = targetPath;

        Text = "Who holding me? – ReleaseMyFiles";
        Size = new Size(750, 450);
        MinimumSize = new Size(600, 350);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        try
        {
            using var stream = typeof(WhoHoldingForm).Assembly
                .GetManifestResourceStream("ReleaseMyFiles.Resources.app.ico");
            if (stream != null) Icon = new Icon(stream);
        }
        catch { }

        // Path label
        _lblPath = new Label
        {
            Text = $"Path: {targetPath}",
            Dock = DockStyle.Top,
            Padding = new Padding(8, 8, 8, 4),
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };

        // Status label
        _lblStatus = new Label
        {
            Text = "Scanning...",
            Dock = DockStyle.Top,
            Padding = new Padding(8, 0, 8, 4),
            AutoSize = true
        };

        // Data grid
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false
        };

        _grid.Columns.Add("PID", "PID");
        _grid.Columns.Add("ProcessName", "Process Name");
        _grid.Columns.Add("Type", "Type");
        _grid.Columns.Add("StartTime", "Start Time");

        _grid.Columns["PID"]!.Width = 70;
        _grid.Columns["PID"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _grid.Columns["Type"]!.Width = 100;
        _grid.Columns["Type"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _grid.Columns["StartTime"]!.Width = 150;
        _grid.Columns["StartTime"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;

        // Button panel
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 45,
            Padding = new Padding(8, 6, 8, 6),
            FlowDirection = FlowDirection.RightToLeft
        };

        _btnRefresh = new Button { Text = "Refresh", Width = 90, Height = 30 };
        _btnKillAll = new Button { Text = "Kill All", Width = 90, Height = 30 };
        _btnKillSelected = new Button { Text = "Kill Selected", Width = 110, Height = 30 };

        _btnRefresh.Click += (_, _) => RefreshList();
        _btnKillAll.Click += BtnKillAll_Click;
        _btnKillSelected.Click += BtnKillSelected_Click;

        buttonPanel.Controls.AddRange([_btnRefresh, _btnKillAll, _btnKillSelected]);

        Controls.Add(_grid);
        Controls.Add(_lblStatus);
        Controls.Add(_lblPath);
        Controls.Add(buttonPanel);

        Load += (_, _) => RefreshList();
    }

    private void RefreshList()
    {
        _grid.Rows.Clear();
        _lblStatus.Text = "Scanning...";
        Application.DoEvents();

        var processes = FileLockDetector.GetLockingProcesses(_targetPath);

        foreach (var p in processes)
        {
            _grid.Rows.Add(
                p.ProcessId.ToString(),
                p.ProcessName,
                p.ApplicationType,
                p.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        _lblStatus.Text = processes.Count == 0
            ? "No processes are locking this path."
            : $"{processes.Count} process(es) found locking this path.";
    }

    private void BtnKillSelected_Click(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0)
        {
            MessageBox.Show("Please select one or more processes to kill.",
                "ReleaseMyFiles", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show(
            $"Kill {_grid.SelectedRows.Count} selected process(es)?",
            "ReleaseMyFiles", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (result != DialogResult.Yes) return;

        int killed = 0;
        foreach (DataGridViewRow row in _grid.SelectedRows)
        {
            if (int.TryParse(row.Cells["PID"].Value?.ToString(), out int pid))
            {
                try
                {
                    var proc = System.Diagnostics.Process.GetProcessById(pid);
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                    killed++;
                }
                catch { }
            }
        }

        MessageBox.Show($"Killed {killed} process(es).",
            "ReleaseMyFiles", MessageBoxButtons.OK, MessageBoxIcon.Information);
        RefreshList();
    }

    private void BtnKillAll_Click(object? sender, EventArgs e)
    {
        if (_grid.Rows.Count == 0)
        {
            MessageBox.Show("No processes to kill.",
                "ReleaseMyFiles", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show(
            $"Kill ALL {_grid.Rows.Count} locking process(es)?",
            "ReleaseMyFiles", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (result != DialogResult.Yes) return;

        int killed = FileLockDetector.KillLockingProcesses(_targetPath);

        MessageBox.Show($"Killed {killed} process(es).",
            "ReleaseMyFiles", MessageBoxButtons.OK, MessageBoxIcon.Information);
        RefreshList();
    }
}
