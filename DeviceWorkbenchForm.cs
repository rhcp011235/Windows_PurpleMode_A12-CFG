using CfgUtility.Services;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;

namespace CfgUtility.Gui;

public sealed class DeviceWorkbenchForm : Form
{
    private static readonly Color Shell = Color.FromArgb(28, 32, 36);
    private static readonly Color Field = Color.FromArgb(44, 50, 55);
    private static readonly Color FieldTarget = Color.FromArgb(41, 154, 102);
    private static readonly Color TextPrimary = Color.FromArgb(235, 238, 241);
    private static readonly Color TextMuted = Color.FromArgb(151, 160, 168);
    private static readonly Color Accent = Color.FromArgb(45, 132, 190);
    private static readonly Color Danger = Color.FromArgb(205, 74, 82);
    private static readonly Color Notice = Color.FromArgb(207, 142, 46);
    private static readonly Color Success = Color.FromArgb(54, 166, 102);

    private readonly List<Control> _busyControls = [];

    private AppPaths _paths = null!;
    private ProcessRunner _runner = null!;
    private IrecoveryClient _irecovery = null!;
    private IrecoveryClient _quietIrecovery = null!;
    private DriverManager _drivers = null!;
    private DiagSerialService _diag = null!;
    private BootWorkflow _boot = null!;

    private Label _deviceLabel = null!;
    private Label _modeLabel = null!;
    private Label _authLabel = null!;
    private Label _statusLabel = null!;
    private TextBox _cpidBox = null!;
    private TextBox _bdidBox = null!;
    private TextBox _productBox = null!;
    private TextBox _ecidBox = null!;
    private ComboBox _portCombo = null!;
    private TextBox _readbackImeiBox = null!;
    private TextBox _readbackMeidBox = null!;
    private TextBox _readbackSerialBox = null!;
    private TextBox _readbackWifiBox = null!;
    private TextBox _readbackBluetoothBox = null!;
    private TextBox _readbackEthernetBox = null!;
    private TextBox _imeiBox = null!;
    private TextBox _meidBox = null!;
    private TextBox _serialBox = null!;
    private TextBox _wifiBox = null!;
    private TextBox _bluetoothBox = null!;
    private TextBox _ethernetBox = null!;
    private TextBox _logBox = null!;

    public DeviceWorkbenchForm()
    {
        Text = "Device CFG Utility";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1120, 720);
        Size = new Size(1280, 760);
        BackColor = Shell;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        BuildLayout();

        _paths = new AppPaths();
        _runner = new ProcessRunner();
        _irecovery = new IrecoveryClient(_paths, _runner, Log);
        _quietIrecovery = new IrecoveryClient(_paths, _runner);
        _drivers = new DriverManager(_paths, _runner, Log);
        _diag = new DiagSerialService(_runner);
        _boot = new BootWorkflow(_paths, _runner, _irecovery, _drivers, Log);

        _ = ExecuteUiActionAsync("Refresh", ScanDeviceAsync, showErrorDialog: false);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Shell,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 560));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(BuildWorkflowPanel(), 0, 0);

        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(21, 21, 25),
            ForeColor = Color.FromArgb(238, 238, 238),
            Font = new Font(FontFamily.GenericMonospace, 9F),
            Margin = new Padding(16, 8, 4, 8)
        };
        root.Controls.Add(_logBox, 1, 0);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Text = "Ready",
            ForeColor = TextMuted,
            Padding = new Padding(0, 6, 0, 0)
        };
        root.Controls.Add(_statusLabel, 0, 1);
        root.SetColumnSpan(_statusLabel, 2);
    }

    private Control BuildWorkflowPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Shell,
            ColumnCount = 1,
            RowCount = 10,
            Padding = new Padding(0),
            AutoScroll = true
        };

        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(BuildHeader(), 0, 0);
        panel.Controls.Add(BuildDeviceStatus(), 0, 1);
        panel.Controls.Add(BuildEcidRow(), 0, 2);
        panel.Controls.Add(BuildPrimaryActions(), 0, 3);
        panel.Controls.Add(BuildEraseActions(), 0, 4);
        panel.Controls.Add(BuildFlags(), 0, 5);
        panel.Controls.Add(BuildDiagSelector(), 0, 6);
        panel.Controls.Add(BuildSyscfgHeader(), 0, 7);
        panel.Controls.Add(BuildSyscfgGrid(), 0, 8);
        panel.Controls.Add(BuildFooterActions(), 0, 9);
        return panel;
    }

    private Control BuildHeader()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            BackColor = Shell,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 10)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var exit = StyledButton("X EXIT", Color.FromArgb(238, 238, 238), Color.FromArgb(45, 45, 48), 82);
        exit.ForeColor = Color.FromArgb(35, 35, 38);
        exit.Click += (_, _) => Close();
        layout.Controls.Add(exit, 0, 0);

        var title = new Label
        {
            Text = "Device CFG Utility",
            AutoSize = true,
            Font = new Font("Segoe UI", 22F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = TextPrimary,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(10, 0, 0, 0)
        };
        layout.Controls.Add(title, 1, 0);
        return layout;
    }

    private Control BuildDeviceStatus()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            BackColor = Shell,
            ColumnCount = 8,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _deviceLabel = MutedLabel("Extract lost syscfg  •  Device:");
        layout.Controls.Add(_deviceLabel, 0, 0);
        _cpidBox = StatusTextBox("CPID");
        layout.Controls.Add(_cpidBox, 1, 0);
        layout.Controls.Add(MutedLabel("BDID:"), 2, 0);
        _bdidBox = StatusTextBox("BDID");
        layout.Controls.Add(_bdidBox, 3, 0);
        layout.Controls.Add(MutedLabel("Mode:"), 4, 0);
        _modeLabel = new Label
        {
            Text = "-",
            AutoSize = true,
            ForeColor = Accent,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            Anchor = AnchorStyles.Left
        };
        layout.Controls.Add(_modeLabel, 5, 0);
        _authLabel = new Label
        {
            Text = "WAITING",
            AutoSize = true,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(94, 94, 98),
            Font = new Font("Segoe UI", 8F, FontStyle.Bold, GraphicsUnit.Point),
            Padding = new Padding(7, 3, 7, 3),
            Anchor = AnchorStyles.Left
        };
        layout.Controls.Add(_authLabel, 6, 0);
        _productBox = StatusTextBox("Product");
        _productBox.Visible = false;
        layout.Controls.Add(_productBox, 7, 0);
        return layout;
    }

    private Control BuildEcidRow()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            BackColor = Shell,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 14)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 202));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));

        layout.Controls.Add(MutedLabel("ECID:"), 0, 0);
        _ecidBox = StatusTextBox("ECID");
        _ecidBox.Width = 190;
        layout.Controls.Add(_ecidBox, 1, 0);
        var copy = StyledButton("COPY", Color.FromArgb(236, 236, 238), Color.FromArgb(60, 60, 65), 72);
        copy.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_ecidBox.Text))
            {
                Clipboard.SetText(_ecidBox.Text.Trim());
                Log("ECID copied.");
            }
        };
        layout.Controls.Add(copy, 2, 0);
        return layout;
    }

    private Control BuildPrimaryActions()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            BackColor = Shell,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 6)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddAction(layout, "READ SYSCFG", 0, 0, Color.FromArgb(242, 242, 244), Color.FromArgb(44, 44, 47), () => ExecuteUiActionAsync("Read syscfg", ReadConfigFromDeviceAsync));
        AddAction(layout, "REFRESH", 1, 0, Color.FromArgb(242, 242, 244), Color.FromArgb(44, 44, 47), () => ExecuteUiActionAsync("Refresh", ScanDeviceAsync));
        AddAction(layout, "FIX DRIVER", 2, 0, Notice, Color.White, () => ExecuteUiActionAsync("Fix driver", () => _drivers.FixAppleUsbDriverAsync()));
        AddAction(layout, "BOOT DIAG MODE", 3, 0, Accent, Color.White, () => ExecuteUiActionAsync("Boot DIAG mode", BootDetectedDeviceAsync));
        return layout;
    }

    private Control BuildEraseActions()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            BackColor = Shell,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(146, 0, 0, 10)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));

        AddAction(layout, "DIAG ERASE", 0, 0, Accent, Color.White, () => ExecuteUiActionAsync("DIAG erase", RunDiagEraseAsync));
        AddAction(layout, "ERASE DFU MODE", 1, 0, Danger, Color.White, () => ExecuteUiActionAsync("Erase DFU mode", RunDfuEraseFromConnectedDeviceAsync));
        return layout;
    }

    private Control BuildFlags()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            BackColor = Shell,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(170, 0, 0, 18)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));

        var seal = new RadioButton
        {
            Text = "Seal",
            ForeColor = TextMuted,
            BackColor = Shell,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        layout.Controls.Add(seal, 0, 0);

        var bbpv = new RadioButton
        {
            Text = "bbpv",
            ForeColor = TextMuted,
            BackColor = Shell,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        layout.Controls.Add(bbpv, 1, 0);

        var erased = new Label
        {
            Text = "ERASED",
            ForeColor = Color.White,
            BackColor = Success,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            Width = 96,
            Height = 34,
            Margin = new Padding(0, 0, 0, 0)
        };
        layout.Controls.Add(erased, 2, 0);
        return layout;
    }

    private Control BuildDiagSelector()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            BackColor = Shell,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(112, 0, 0, 18)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(MutedLabel("DIAG Port:"), 0, 0);
        _portCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDown,
            BackColor = Color.FromArgb(238, 238, 238),
            ForeColor = Color.FromArgb(35, 35, 38),
            FlatStyle = FlatStyle.Flat,
            Height = 30,
            Margin = new Padding(0, 0, 8, 0)
        };
        layout.Controls.Add(_portCombo, 1, 0);
        AddAction(layout, "Scan", 2, 0, Color.FromArgb(236, 236, 238), Color.FromArgb(40, 40, 44), () => ExecuteUiActionAsync("Scan ports", ScanPortsAsync), width: 54, height: 31);
        return layout;
    }

    private Control BuildSyscfgHeader()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            BackColor = Shell,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 312));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(HeaderLabel("Device readback"), 0, 0);
        layout.Controls.Add(HeaderLabel("syscfg on NAND"), 1, 0);
        return layout;
    }

    private Control BuildSyscfgGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            BackColor = Shell,
            ColumnCount = 4,
            RowCount = 6,
            Margin = new Padding(0, 0, 0, 12)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        for (var i = 0; i < 6; i++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        }

        _readbackImeiBox = AddSyscfgRow(grid, 0, "IMEI", readOnlyTargetText: "IMEI not editable", out _imeiBox);
        _readbackMeidBox = AddSyscfgRow(grid, 1, "MEID", readOnlyTargetText: "MEID not editable", out _meidBox);
        _readbackSerialBox = AddSyscfgRow(grid, 2, "SrNm", out _serialBox);
        _readbackWifiBox = AddSyscfgRow(grid, 3, "WMac", out _wifiBox);
        _readbackBluetoothBox = AddSyscfgRow(grid, 4, "BMac", out _bluetoothBox);
        _readbackEthernetBox = AddSyscfgRow(grid, 5, "EMac", out _ethernetBox);

        _ethernetBox.ReadOnly = true;
        _ethernetBox.BackColor = Field;
        _ethernetBox.ForeColor = TextMuted;
        _ethernetBox.Text = "auto-generated";

        _wifiBox.TextChanged += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_wifiBox.Text) && string.IsNullOrWhiteSpace(_bluetoothBox.Text))
            {
                _bluetoothBox.Text = SyscfgCodec.PairBluetoothMac(_wifiBox.Text);
            }
        };

        return grid;
    }

    private Control BuildFooterActions()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            BackColor = Shell,
            ColumnCount = 3,
            RowCount = 2,
            Margin = new Padding(170, 0, 0, 0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        AddAction(layout, "FLASH SYSCFG", 0, 0, Success, Color.White, () => ExecuteUiActionAsync("Flash syscfg", FlashSyscfgAsync), width: 158, height: 40);
        var help = StyledButton("?", Color.FromArgb(78, 78, 82), Color.White, 36, 36);
        help.Font = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Point);
        help.Click += (_, _) => MessageBox.Show(this, "Fill or read SrNm, WMac, and BMac, then flash syscfg while the device is in DIAG mode.", "Help", MessageBoxButtons.OK, MessageBoxIcon.Information);
        layout.Controls.Add(help, 1, 0);

        var note = new Label
        {
            Text = "(i) EMac is auto-generated",
            ForeColor = TextMuted,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 0, 0)
        };
        layout.Controls.Add(note, 0, 1);
        layout.SetColumnSpan(note, 3);
        return layout;
    }

    private TextBox AddSyscfgRow(TableLayoutPanel grid, int row, string label, out TextBox targetBox)
    {
        return AddSyscfgRow(grid, row, label, null, out targetBox);
    }

    private TextBox AddSyscfgRow(TableLayoutPanel grid, int row, string label, string? readOnlyTargetText, out TextBox targetBox)
    {
        grid.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            ForeColor = TextMuted,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 10, 0)
        }, 0, row);

        var readback = FieldBox(readOnly: true);
        grid.Controls.Add(readback, 1, row);

        grid.Controls.Add(new Label
        {
            Text = "→",
            AutoSize = true,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point),
            Anchor = AnchorStyles.None
        }, 2, row);

        targetBox = FieldBox(readOnly: readOnlyTargetText != null, target: readOnlyTargetText == null);
        if (readOnlyTargetText != null)
        {
            targetBox.Text = readOnlyTargetText;
        }
        grid.Controls.Add(targetBox, 3, row);
        return readback;
    }

    private async Task ScanDeviceAsync()
    {
        var values = await _quietIrecovery.QueryAsync();
        if (values.Count == 0)
        {
            _authLabel.Text = "WAITING";
            _authLabel.BackColor = Color.FromArgb(94, 94, 98);
            _modeLabel.Text = "-";
            Log("No device values returned.");
            return;
        }

        values.TryGetValue("CPID", out var cpid);
        values.TryGetValue("BDID", out var bdid);
        values.TryGetValue("ECID", out var ecid);
        values.TryGetValue("PRODUCT", out var product);
        values.TryGetValue("MODEL", out var model);
        values.TryGetValue("MODE", out var mode);

        _cpidBox.Text = FormatHex(cpid);
        _bdidBox.Text = NormalizeHex(bdid, 2);
        _ecidBox.Text = ecid ?? "";
        _productBox.Text = ResolveProduct(cpid, bdid, product, model);
        _modeLabel.Text = string.IsNullOrWhiteSpace(mode) ? "Recovery" : mode;
        _authLabel.Text = "AUTHORIZED";
        _authLabel.BackColor = Success;

        if (values.TryGetValue("SRNM", out var serial) && !string.Equals(serial, "N/A", StringComparison.OrdinalIgnoreCase))
        {
            _readbackSerialBox.Text = serial;
            CopyIfEmpty(_serialBox, serial);
        }

        foreach (var pair in values.OrderBy(p => p.Key))
        {
            Log($"{pair.Key}: {pair.Value}");
        }
    }

    private async Task ReadConfigFromDeviceAsync()
    {
        if (string.IsNullOrWhiteSpace(_portCombo.Text))
        {
            await ScanPortsAsync();
        }

        try
        {
            await ReadAtAsync();
        }
        catch (Exception ex)
        {
            Log("AT identity read skipped: " + ex.Message);
        }

        await ReadSyscfgAsync();
    }

    private async Task BootDetectedDeviceAsync()
    {
        var cpid = _cpidBox.Text;
        var bdid = _bdidBox.Text;
        if (string.IsNullOrWhiteSpace(cpid) || string.IsNullOrWhiteSpace(bdid))
        {
            await ScanDeviceAsync();
            cpid = _cpidBox.Text;
            bdid = _bdidBox.Text;
        }

        await _boot.BootDetectedDeviceAsync(Required(cpid, "CPID"), Required(bdid, "BDID"));
    }

    private async Task RunDfuEraseFromConnectedDeviceAsync()
    {
        ConfirmErase("DFU erase will upload iBEC, set obliteration flags, and reboot.");
        var values = await _quietIrecovery.QueryAsync();
        if (!values.TryGetValue("CPID", out var cpid) || string.IsNullOrWhiteSpace(cpid))
        {
            throw new InvalidOperationException("Could not read CPID. Make sure the device is in PWND DFU mode.");
        }

        if (!values.TryGetValue("BDID", out var bdid) || string.IsNullOrWhiteSpace(bdid))
        {
            throw new InvalidOperationException("Could not read BDID. Make sure the device is in PWND DFU mode.");
        }

        _cpidBox.Text = FormatHex(cpid);
        _bdidBox.Text = NormalizeHex(bdid, 2);
        await _boot.EraseDfuAsync(cpid, bdid);
    }

    private async Task ScanPortsAsync()
    {
        _portCombo.Items.Clear();
        foreach (var port in SerialPort.GetPortNames().OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            _portCombo.Items.Add(port);
        }

        var detected = await _diag.FindDiagPortAsync();
        if (!string.IsNullOrWhiteSpace(detected))
        {
            if (!_portCombo.Items.Contains(detected))
            {
                _portCombo.Items.Add(detected);
            }
            _portCombo.SelectedItem = detected;
            Log("Detected DIAG port: " + detected);
        }
        else
        {
            Log("No DIAG port detected.");
        }
    }

    private async Task ReadAtAsync()
    {
        var result = await _diag.ReadAtIdentityAsync(await ResolvePortAsync());
        ApplyAtIdentity(result);
        Log(result);
    }

    private async Task ReadSyscfgAsync()
    {
        var values = await _diag.ReadSyscfgAsync(await ResolvePortAsync());
        _readbackSerialBox.Text = values.Serial;
        _readbackWifiBox.Text = values.WifiMac;
        _readbackBluetoothBox.Text = values.BluetoothMac;

        CopyIfEmpty(_serialBox, values.Serial);
        CopyIfEmpty(_wifiBox, values.WifiMac);
        CopyIfEmpty(_bluetoothBox, values.BluetoothMac);

        Log($"Serial: {values.Serial}");
        Log($"WiFi: {values.WifiMac}");
        Log($"BT: {values.BluetoothMac}");
    }

    private async Task FlashSyscfgAsync()
    {
        var wifi = string.IsNullOrWhiteSpace(_wifiBox.Text) ? null : _wifiBox.Text.Trim();
        var bluetooth = string.IsNullOrWhiteSpace(_bluetoothBox.Text) ? null : _bluetoothBox.Text.Trim();
        if (bluetooth == null && wifi != null)
        {
            bluetooth = SyscfgCodec.PairBluetoothMac(wifi);
            _bluetoothBox.Text = bluetooth;
        }

        await _diag.FlashSyscfgAsync(await ResolvePortAsync(), Required(_serialBox.Text, "SrNm"), wifi, bluetooth);
        Log("syscfg write commands sent.");
    }

    private async Task RunDiagEraseAsync()
    {
        ConfirmErase("DIAG erase will set the DIAG obliteration flag and reset the device.");
        await _diag.RunDiagEraseAsync(await ResolvePortAsync());
        Log("DIAG erase commands sent.");
    }

    private async Task<string> ResolvePortAsync()
    {
        var port = _portCombo.Text.Trim();
        if (!string.IsNullOrWhiteSpace(port))
        {
            return port;
        }

        port = await _diag.FindDiagPortAsync();
        if (string.IsNullOrWhiteSpace(port))
        {
            throw new InvalidOperationException("No DIAG serial port found.");
        }

        _portCombo.Text = port;
        return port;
    }

    private async Task ExecuteUiActionAsync(string name, Func<Task> operation, bool showErrorDialog = true)
    {
        SetBusy(true);
        _statusLabel.Text = name;
        Log("=== " + name + " ===");
        try
        {
            await operation();
            _statusLabel.Text = "Ready";
            Log("Done.");
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Error";
            Log("ERROR: " + ex.Message);
            if (showErrorDialog)
            {
                MessageBox.Show(this, ex.Message, name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        foreach (var control in _busyControls)
        {
            control.Enabled = !busy;
        }
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void Log(string message)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(Log), message);
            return;
        }

        _logBox.AppendText(message + Environment.NewLine);
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private void ApplyAtIdentity(string result)
    {
        var imei = MatchValue(result, "IMEI");
        var serial = MatchValue(result, "SN");
        if (!string.IsNullOrWhiteSpace(imei))
        {
            _readbackImeiBox.Text = imei;
        }

        if (!string.IsNullOrWhiteSpace(serial))
        {
            _readbackSerialBox.Text = serial;
            CopyIfEmpty(_serialBox, serial);
        }
    }

    private static string? MatchValue(string text, string key)
    {
        var match = Regex.Match(text, key + @":([^|;\r\n]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static void CopyIfEmpty(TextBox target, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(target.Text))
        {
            target.Text = value.Trim();
        }
    }

    private static string ResolveProduct(string? cpid, string? bdid, string? product, string? model)
    {
        if (!string.IsNullOrWhiteSpace(product))
        {
            return string.IsNullOrWhiteSpace(model) ? product : $"{product} ({model})";
        }

        var key = (NormalizeHex(cpid, 4), NormalizeHex(bdid, 2));
        if (BootWorkflow.A12A13Devices.TryGetValue(key, out var a12))
        {
            return $"{a12.Name} ({a12.ProductType})";
        }

        if (BootWorkflow.A7A11Devices.TryGetValue(key, out var a7))
        {
            return $"{a7.Name} ({a7.ProductType})";
        }

        return "";
    }

    private static string FormatHex(string? value)
    {
        var normalized = NormalizeHex(value, 4);
        return string.IsNullOrWhiteSpace(normalized) ? "" : "0x" + normalized;
    }

    private static string NormalizeHex(string? value, int minWidth)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.Replace("0x", "", StringComparison.OrdinalIgnoreCase).Trim().ToLowerInvariant().PadLeft(minWidth, '0');
    }

    private static string Required(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(name + " is required.");
        }

        return value.Trim();
    }

    private void ConfirmErase(string message)
    {
        var result = MessageBox.Show(this, message + Environment.NewLine + Environment.NewLine + "All data may be erased. Continue?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes)
        {
            throw new OperationCanceledException("Cancelled.");
        }
    }

    private void AddAction(TableLayoutPanel layout, string text, int column, int row, Color backColor, Color foreColor, Func<Task> action, int width = 0, int height = 36)
    {
        var button = StyledButton(text, backColor, foreColor, width, height);
        button.Click += (_, _) => _ = action();
        layout.Controls.Add(button, column, row);
    }

    private Button StyledButton(string text, Color backColor, Color foreColor, int width = 0, int height = 36)
    {
        var button = new Button
        {
            Text = text,
            Width = width <= 0 ? 132 : width,
            Height = height,
            BackColor = backColor,
            ForeColor = foreColor,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 8, 0),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        _busyControls.Add(button);
        return button;
    }

    private static Label MutedLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = TextMuted,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 6, 0)
        };
    }

    private static Label HeaderLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = TextMuted,
            Anchor = AnchorStyles.Left,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 0, 0, 0),
            Padding = new Padding(0, 0, 0, 0)
        };
    }

    private static TextBox StatusTextBox(string accessibilityName)
    {
        return new TextBox
        {
            AccessibleName = accessibilityName,
            Height = 28,
            BorderStyle = BorderStyle.None,
            BackColor = Field,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 8, 0)
        };
    }

    private static TextBox FieldBox(bool readOnly, bool target = false)
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Height = 34,
            ReadOnly = readOnly,
            BorderStyle = BorderStyle.None,
            BackColor = target ? FieldTarget : Field,
            ForeColor = target ? Color.White : TextPrimary,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 0, 8)
        };
    }
}
