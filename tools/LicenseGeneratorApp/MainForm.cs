using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ElpisEdgeConnect.Core.Licensing;

namespace ElpisEdgeConnect.LicenseGeneratorApp;

/// <summary>
/// Admin form that generates a signed <c>license.json</c> for gateway activation.
/// </summary>
public sealed class MainForm : Form
{
    // Operator-available SOURCE protocols in EdgeConnect (display name -> the
    // license module key the runtime's DI gate checks). Keys mirror each
    // adapter's *SourceConfiguration.LicenseModuleKey exactly (note OPC UA Client
    // is "source-opcua-client"; Modbus TCP/RTU share "source-modbus-tcp").
    private static readonly (string Display, string Key)[] Sources =
    {
        ("Modbus TCP",                  "source-modbus-tcp"),
        ("Modbus RTU",                  "source-modbus-tcp"),
        ("FANUC FOCAS2",                "source-focas2"),
        ("Brother HTTP",                "source-brother-http"),
        ("OPC UA Client",               "source-opcua-client"),
        ("Siemens S7",                  "source-s7"),
        ("MTConnect",                   "source-mtconnect"),
        ("EtherNet/IP",                 "source-ethernet-ip"),
        ("Mitsubishi MELSEC",           "source-melsec"),
    };

    // Operator-available DESTINATION protocols (display -> sink module key).
    private static readonly (string Display, string Key)[] Destinations =
    {
        ("MQTT",                        "sink-mqtt"),
        ("OPC UA Server",               "sink-opc-ua-server"),
    };

    private readonly TextBox _client = new() { Width = 320 };
    private readonly TextBox _company = new() { Width = 320 };
    private readonly TextBox _email = new() { Width = 320 };
    private readonly TextBox _phone = new() { Width = 320 };
    private readonly ComboBox _product = new() { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };

    // Add-a-product row. The dropdown stays a DropDownList — free-typing a
    // product name into the licence itself invites typos that only surface at
    // activation — so a new name is added deliberately here and persisted to the
    // catalogue, then selected in the dropdown above.
    private readonly TextBox _newProduct = new() { Width = 236 };
    private readonly Button _saveProduct = new()
    {
        Text = "Save",
        AutoSize = true,
        Padding = new Padding(8, 3, 8, 3),
        Margin = new Padding(6, 0, 0, 0),
    };
    private readonly ComboBox _edition = new() { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _gateway = new() { Width = 320 };
    private readonly DateTimePicker _expiry = new()
    {
        Width = 200,
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "dd MMM yyyy",
        MinDate = DateTime.Today,
    };
    private readonly Label _expiryHint = new() { AutoSize = true, ForeColor = Color.FromArgb(96, 96, 96), Margin = new Padding(10, 6, 0, 0) };
    // Trial-Period expiry input: a day count (shown only for "Trial Period"). The
    // three year-preset buttons live in _yearPresetsPanel so both groups can be
    // toggled as a unit when switching expiry modes.
    private readonly NumericUpDown _trialDays = new() { Width = 70, Minimum = 1, Maximum = 3650, Value = 30 };
    private readonly FlowLayoutPanel _trialDaysPanel = new() { AutoSize = true, WrapContents = false, Visible = false, Margin = new Padding(6, 2, 0, 0) };
    private readonly FlowLayoutPanel _yearPresetsPanel = new() { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
    private readonly TextBox _licenseId = new() { Width = 320, ReadOnly = true, BackColor = Color.FromArgb(240, 240, 240) };
    private readonly SourcesDestinationsEditor _protocols = new();
    private readonly NumericUpDown _maxSources = new() { Width = 70, Minimum = 0, Maximum = 100000, Value = 50 };
    private readonly NumericUpDown _maxSinks = new() { Width = 70, Minimum = 0, Maximum = 100000, Value = 5 };
    private readonly NumericUpDown _maxRoutes = new() { Width = 70, Minimum = 0, Maximum = 100000, Value = 100 };

    // ---- Mode selector + Load-license drill-down ----
    private const string NoneLabel = "(none)";
    private readonly RadioButton _modeNew = new() { Text = "New License", AutoSize = true, Checked = true, Margin = new Padding(0, 0, 20, 0) };
    private readonly RadioButton _modeLoad = new() { Text = "Update License", AutoSize = true };
    private readonly Panel _newPanel = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Panel _loadPanel = new() { Dock = DockStyle.Fill, AutoScroll = true, Visible = false };
    private readonly ComboBox _loadProduct = new() { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _loadType = new() { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _loadCompany = new() { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _loadCustomer = new() { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _loadDetails = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Width = 470,
        Height = 260,
        Font = new Font("Consolas", 9f),
        BackColor = Color.White,
    };
    private bool _suppressCascade;
    private string _loadedJson = "";
    private IssuedLicenseRecord? _loadedRecord;
    private readonly Label _updateNote = new()
    {
        AutoSize = true,
        ForeColor = Color.FromArgb(46, 84, 150),
        Font = new Font("Segoe UI", 9f, FontStyle.Italic),
        Margin = new Padding(0, 0, 0, 8),
        MaximumSize = new Size(520, 0),
        Visible = false,
    };

    public MainForm()
    {
        Text = "Elpis — License Generator";
        // Title-bar / taskbar icon = the Elpis app icon (ApplicationIcon in the
        // csproj). ExtractAssociatedIcon pulls it from our own exe so no separate
        // .ico file is shipped; falls back to the WinForms default if unavailable.
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* keep default */ }
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;
        // Resizable + scrollable so the whole form is always reachable regardless of
        // the display's DPI scaling. A fixed-size dialog can push its bottom off a
        // scaled screen (the "page not fully displaying" bug); the mode panels
        // scroll internally and the window is clamped to the working area below.
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        MinimumSize = new Size(560, 420);

        // Catalogue-driven now. EnsureCreated seeds the two names this line used
        // to hard-code, so nothing disappears for an existing installation.
        ReloadProductCatalog(selectName: null);
        _edition.Items.AddRange(new object[] { "Starter", "Professional", "CNC Pro", "Enterprise", "Trial Period" });

        // On edition change: apply per-edition default Limits (still editable) AND
        // restrict which source/destination protocols are even offered. A Starter
        // license offers exactly Modbus TCP + MQTT; Professional/Enterprise offer
        // the full catalogue.
        _edition.SelectedIndexChanged += (_, _) =>
        {
            ApplyEditionLimits();
            PopulateProtocolsForEdition();
            ApplyExpiryModeForEdition();
        };
        _edition.SelectedIndex = 1; // Professional -> triggers the handler above
        _expiry.Value = DateTime.Today.AddYears(1);
        _expiry.ValueChanged += (_, _) => UpdateExpiryHint();
        UpdateExpiryHint();
        _licenseId.Text = NewLicenseId();

        var layout = new TableLayoutPanel
        {
            // Dock TOP + AutoSize (not Fill): the panel takes its natural height so
            // the parent's AutoScroll can show a scrollbar if it exceeds the window.
            // Dock=Fill + AutoSize conflict and clip the content instead.
            Dock = DockStyle.Top,
            Padding = new Padding(16),
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        void Row(string label, Control control, bool required = false)
        {
            layout.Controls.Add(MakeFieldLabel(label, required));
            control.Margin = new Padding(0, 6, 0, 6);
            layout.Controls.Add(control);
        }

        // NOTE: the app title/subtitle used to sit here, inside the scrolling
        // panel, so it scrolled off the top on tall forms. It now lives in a
        // FIXED header bar docked above the content (see `header` below), which
        // stays locked in place while these fields scroll.

        // Shown only when the form was populated from Load License (update flow).
        layout.Controls.Add(_updateNote);
        layout.SetColumnSpan(_updateNote, 2);

        var addProduct = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 6),
        };
        _newProduct.PlaceholderText = "New product name";
        addProduct.Controls.Add(_newProduct);
        addProduct.Controls.Add(_saveProduct);
        _saveProduct.Click += OnSaveProductClick;
        // Enter in the box saves, rather than submitting the whole form.
        _newProduct.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                OnSaveProductClick(_saveProduct, EventArgs.Empty);
            }
        };
        // Add product leads the form: it is the step that makes a NEW product
        // selectable, so it has to come before the dropdown it feeds. Sitting
        // underneath, it read as an afterthought attached to a list the operator
        // had already been forced to choose from.
        Row("Add product", addProduct);
        Row("Product name", _product, required: true);

        Row("Client name", _client, required: true);
        Row("Company", _company, required: true);
        Row("Email", _email, required: true);
        Row("Phone", _phone);
        Row("License type", _edition);
        Row("Gateway ID", _gateway, required: true);

        var gwHint = new Label
        {
            Text = "Enter the gateway's ID (shown on the Studio License page) as a GUID, "
                 + "e.g. f129be9d-bc86-4fe1-bf9c-6d68aab7102f.  Use * for a license valid on any machine.",
            AutoSize = true,
            ForeColor = Color.Gray,
            MaximumSize = new Size(360, 0),
            Margin = new Padding(0, 0, 0, 6),
        };
        layout.Controls.Add(new Label());
        layout.Controls.Add(gwHint);

        // Expiry: date picker + quick-duration presets (or a Trial-days count for
        // the "Trial Period" type) + a "valid for N days" hint.
        _yearPresetsPanel.Controls.Add(MakePresetButton("1Y", 1));
        _yearPresetsPanel.Controls.Add(MakePresetButton("2Y", 2));
        _yearPresetsPanel.Controls.Add(MakePresetButton("3Y", 3));

        _trialDaysPanel.Controls.Add(new Label { Text = "Trial days:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _trialDaysPanel.Controls.Add(_trialDays);
        _trialDays.ValueChanged += (_, _) =>
        {
            if (_suppressCascade) { return; }
            var target = DateTime.Today.AddDays((int)_trialDays.Value);
            if (target < _expiry.MinDate) { target = _expiry.MinDate; }
            if (target > _expiry.MaxDate) { target = _expiry.MaxDate; }
            _expiry.Value = target;
            UpdateExpiryHint();
        };

        var expiryRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 4) };
        expiryRow.Controls.Add(_expiry);
        expiryRow.Controls.Add(_yearPresetsPanel);
        expiryRow.Controls.Add(_trialDaysPanel);
        expiryRow.Controls.Add(_expiryHint);
        Row("Expiry date", expiryRow);

        Row("License ID", _licenseId);

        var sdHeader = new Label
        {
            Text = "Sources & Destinations",
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Margin = new Padding(0, 12, 0, 4),
        };
        layout.Controls.Add(sdHeader);
        layout.SetColumnSpan(sdHeader, 2);

        var sdHint = new Label
        {
            Text = "Auto-filled from the license type. Tick individual protocols, or use Select all / Unselect all.",
            AutoSize = true,
            ForeColor = Color.Gray,
            MaximumSize = new Size(470, 0),
            Margin = new Padding(0, 0, 0, 6),
        };
        layout.Controls.Add(sdHint);
        layout.SetColumnSpan(sdHint, 2);

        _protocols.Margin = new Padding(0, 0, 0, 6);
        layout.Controls.Add(_protocols);
        layout.SetColumnSpan(_protocols, 2);

        var limits = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        limits.Controls.Add(new Label { Text = "Sources", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        limits.Controls.Add(_maxSources);
        limits.Controls.Add(new Label { Text = "Sinks", AutoSize = true, Margin = new Padding(12, 6, 4, 0) });
        limits.Controls.Add(_maxSinks);
        limits.Controls.Add(new Label { Text = "Routes", AutoSize = true, Margin = new Padding(12, 6, 4, 0) });
        limits.Controls.Add(_maxRoutes);
        Row("Limits (max)", limits);

        var generate = new Button
        {
            Text = "Submit — Create License",
            AutoSize = true,
            Padding = new Padding(10, 6, 10, 6),
            BackColor = Color.FromArgb(46, 84, 150),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 12, 0, 0),
        };
        generate.Click += OnGenerate;

        var clear = new Button
        {
            Text = "Clear",
            AutoSize = true,
            Padding = new Padding(10, 6, 10, 6),
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(12, 12, 0, 0),
        };
        clear.Click += (_, _) => ResetForm();

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        buttons.Controls.Add(generate);
        buttons.Controls.Add(clear);
        layout.Controls.Add(new Label());
        layout.Controls.Add(buttons);

        _newPanel.Controls.Add(layout);
        BuildLoadPanel();

        // Mode selector across the top: New License (create) / Load License (browse
        // previously issued licenses from the history database).
        var modeBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(16, 10, 16, 6),
        };
        modeBar.Controls.Add(new Label
        {
            Text = "Mode:",
            AutoSize = true,
            Margin = new Padding(0, 4, 12, 0),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        });
        modeBar.Controls.Add(_modeNew);
        modeBar.Controls.Add(_modeLoad);

        _modeNew.CheckedChanged += (_, _) =>
        {
            if (_modeNew.Checked) { _loadPanel.Visible = false; _newPanel.Visible = true; }
        };
        _modeLoad.CheckedChanged += (_, _) =>
        {
            if (_modeLoad.Checked) { _newPanel.Visible = false; _loadPanel.Visible = true; RefreshLoadProducts(); }
        };

        // Changing the License ID away from the loaded one means "make a new license",
        // so the "updating…" banner no longer applies.
        _licenseId.TextChanged += (_, _) =>
        {
            if (_loadedRecord is null
                || !string.Equals(_licenseId.Text.Trim(), _loadedRecord.LicenseId, StringComparison.Ordinal))
            {
                _updateNote.Visible = false;
            }
        };

        // Both mode panels live in the same content area; visibility selects one.
        var content = new Panel { Dock = DockStyle.Fill };
        content.Controls.Add(_loadPanel);
        content.Controls.Add(_newPanel);

        // Fixed header bar — LOCKED to the top of the window: the app title +
        // subtitle stay put while the form body (mode panels) scrolls beneath.
        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.FromArgb(245, 247, 250),
            Padding = new Padding(16, 10, 16, 10),
        };
        // Elpis logo (left). Embedded resource, so the standalone exe needs no
        // loose asset. Omitted gracefully if the resource can't be loaded.
        var logo = LoadEmbeddedLogo();
        if (logo is not null)
        {
            header.Controls.Add(new PictureBox
            {
                Image = logo,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(40, 40),
                Margin = new Padding(0, 2, 12, 0),
            });
        }
        var headerText = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.TopDown,
            Margin = Padding.Empty,
        };
        headerText.Controls.Add(new Label
        {
            Text = "Elpis — License Generator",
            AutoSize = true,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            Margin = Padding.Empty,
        });
        headerText.Controls.Add(new Label
        {
            Text = "Generate a signed license.json for gateway activation",
            AutoSize = true,
            ForeColor = Color.FromArgb(96, 96, 96),
            Margin = new Padding(0, 2, 0, 0),
        });
        header.Controls.Add(headerText);
        // Thin bottom rule so the locked header reads as chrome, not content.
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(214, 219, 226));
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        // Fixed footer bar — LOCKED to the bottom of the window: the copyright
        // line stays put while the form body scrolls above it.
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.FromArgb(245, 247, 250),
            Padding = new Padding(16, 6, 16, 6),
        };
        footer.Controls.Add(new Label
        {
            Text = "Copyright © 2026 Elpis IT Solutions",
            AutoSize = true,
            ForeColor = Color.FromArgb(96, 96, 96),
            Margin = Padding.Empty,
        });
        // Thin top rule so the locked footer reads as chrome, not content.
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(214, 219, 226));
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };

        // root: fixed header + fixed mode bar + scrolling content + fixed footer.
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // header (locked)
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // mode bar (locked)
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // content (scrolls)
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // footer (locked)
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(modeBar, 0, 1);
        root.Controls.Add(content, 0, 2);
        root.Controls.Add(footer, 0, 3);

        Controls.Add(root);

        // Size to fit the working area (with margin) so the bottom never lands
        // off-screen on a high-DPI display; the panels' AutoScroll covers the rest.
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        ClientSize = new Size(
            Math.Min(800, Math.Max(720, wa.Width - 80)),
            Math.Min(720, Math.Max(420, wa.Height - 80)));
    }

    /// <summary>Build the "Load License" panel — a Product → Type → Company → Customer drill-down.</summary>
    private void BuildLoadPanel()
    {
        var loadLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(16),
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        loadLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        loadLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        void LRow(string label, Control control)
        {
            loadLayout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 8) });
            control.Margin = new Padding(0, 6, 0, 6);
            loadLayout.Controls.Add(control);
        }

        var title = new Label
        {
            Text = "Update a previously issued license",
            AutoSize = true,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 12),
        };
        loadLayout.Controls.Add(title);
        loadLayout.SetColumnSpan(title, 2);

        _loadProduct.SelectedIndexChanged += (_, _) => { if (!_suppressCascade) OnLoadProductChanged(); };
        _loadType.SelectedIndexChanged += (_, _) => { if (!_suppressCascade) OnLoadTypeChanged(); };
        _loadCompany.SelectedIndexChanged += (_, _) => { if (!_suppressCascade) OnLoadCompanyChanged(); };
        _loadCustomer.SelectedIndexChanged += (_, _) => { if (!_suppressCascade) OnLoadCustomerChanged(); };

        LRow("Product name", _loadProduct);
        LRow("License type", _loadType);
        LRow("Company", _loadCompany);
        LRow("Customer name", _loadCustomer);

        loadLayout.Controls.Add(new Label { Text = "Details", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 8, 8, 8) });
        loadLayout.Controls.Add(_loadDetails);

        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };

        var editBtn = new Button
        {
            Text = "Edit && update this license",
            AutoSize = true,
            Padding = new Padding(10, 6, 10, 6),
            BackColor = Color.FromArgb(46, 84, 150),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 12, 0),
        };
        editBtn.Click += (_, _) =>
        {
            if (_loadedRecord is not null)
            {
                LoadIntoForm(_loadedRecord);
            }
        };
        actions.Controls.Add(editBtn);

        var copyBtn = new Button { Text = "Copy license JSON", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
        copyBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_loadedJson))
            {
                Clipboard.SetText(_loadedJson);
            }
        };
        actions.Controls.Add(copyBtn);

        loadLayout.Controls.Add(new Label());
        loadLayout.Controls.Add(actions);

        _loadPanel.Controls.Add(loadLayout);
    }

    /// <summary>(Re)load the distinct product list from the DB and reset the rest of the drill-down.</summary>
    /// <summary>
    /// Repopulates the Product dropdown from the catalogue, preserving the
    /// current selection (or selecting <paramref name="selectName"/> when a name
    /// was just added). Falls back to the first entry so the required field is
    /// never left blank.
    /// </summary>
    private void ReloadProductCatalog(string? selectName)
    {
        var keep = selectName ?? _product.SelectedItem as string;

        _product.Items.Clear();
        foreach (var name in LicenseDatabase.GetProductCatalog())
        {
            _product.Items.Add(name);
        }

        if (_product.Items.Count == 0)
        {
            return;
        }

        var index = keep is null ? -1 : _product.Items.IndexOf(keep);
        _product.SelectedIndex = index >= 0 ? index : 0;
    }

    /// <summary>
    /// Saves the typed product name to the catalogue and selects it. Reports the
    /// outcome either way — a Save button that answers with silence leaves the
    /// operator unsure whether the name was taken.
    /// </summary>
    private void OnSaveProductClick(object? sender, EventArgs e)
    {
        var name = _newProduct.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Type a product name first.", "Nothing to save",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            _newProduct.Focus();
            return;
        }

        bool added;
        try
        {
            added = LicenseDatabase.AddProduct(name);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Could not save the product name:" + Environment.NewLine + Environment.NewLine + ex.Message,
                "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!added)
        {
            MessageBox.Show(this,
                $"'{name}' is already in the product list.",
                "Already saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ReloadProductCatalog(selectName: name);
            _newProduct.Clear();
            return;
        }

        ReloadProductCatalog(selectName: name);
        _newProduct.Clear();
        MessageBox.Show(this,
            $"'{name}' was added and is now selected as the product.",
            "Product saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RefreshLoadProducts()
    {
        _suppressCascade = true;
        _loadProduct.Items.Clear();
        _loadType.Items.Clear();
        _loadCompany.Items.Clear();
        _loadCustomer.Items.Clear();
        _loadProduct.SelectedIndex = -1;
        _loadDetails.Clear();
        _loadedJson = "";
        _loadedRecord = null;

        try
        {
            foreach (var p in LicenseDatabase.GetProducts())
            {
                _loadProduct.Items.Add(p);
            }
        }
        catch (Exception ex)
        {
            _loadDetails.Text = "Could not read the license history database:\r\n\r\n" + ex.Message
                + "\r\n\r\n" + LicenseDatabase.DatabasePath;
        }
        finally
        {
            _suppressCascade = false;
        }

        if (_loadProduct.Items.Count == 0 && _loadDetails.TextLength == 0)
        {
            _loadDetails.Text = "No licenses have been issued yet.";
        }
    }

    private void OnLoadProductChanged()
    {
        _suppressCascade = true;
        _loadType.Items.Clear();
        _loadCompany.Items.Clear();
        _loadCustomer.Items.Clear();
        _loadDetails.Clear();
        _loadedJson = "";
        _loadedRecord = null;

        if (_loadProduct.SelectedItem is string p)
        {
            foreach (var t in LicenseDatabase.GetLicenseTypes(p))
            {
                _loadType.Items.Add(t);
            }
        }
        _suppressCascade = false;
    }

    private void OnLoadTypeChanged()
    {
        _suppressCascade = true;
        _loadCompany.Items.Clear();
        _loadCustomer.Items.Clear();
        _loadDetails.Clear();
        _loadedJson = "";
        _loadedRecord = null;

        if (_loadProduct.SelectedItem is string p && _loadType.SelectedItem is string t)
        {
            foreach (var c in LicenseDatabase.GetCompanies(p, t))
            {
                _loadCompany.Items.Add(c.Length == 0 ? NoneLabel : c);
            }
        }
        _suppressCascade = false;
    }

    private void OnLoadCompanyChanged()
    {
        _suppressCascade = true;
        _loadCustomer.Items.Clear();
        _loadDetails.Clear();
        _loadedJson = "";
        _loadedRecord = null;

        if (_loadProduct.SelectedItem is string p && _loadType.SelectedItem is string t
            && _loadCompany.SelectedItem is string c)
        {
            foreach (var cust in LicenseDatabase.GetCustomers(p, t, CompanyValue(c)))
            {
                _loadCustomer.Items.Add(cust);
            }
        }
        _suppressCascade = false;
    }

    private void OnLoadCustomerChanged()
    {
        if (_loadProduct.SelectedItem is not string p || _loadType.SelectedItem is not string t
            || _loadCompany.SelectedItem is not string c || _loadCustomer.SelectedItem is not string cust)
        {
            return;
        }

        IssuedLicenseRecord? rec;
        try
        {
            rec = LicenseDatabase.GetLatestLicense(p, t, CompanyValue(c), cust);
        }
        catch (Exception ex)
        {
            _loadDetails.Text = "Failed to load the license:\r\n\r\n" + ex.Message;
            return;
        }

        if (rec is null)
        {
            _loadDetails.Text = "License not found.";
            _loadedJson = "";
            _loadedRecord = null;
            return;
        }

        _loadedRecord = rec;
        _loadedJson = rec.LicenseJson;
        _loadDetails.Text = FormatDetails(rec);
    }

    /// <summary>Translate the Company combo display back to its stored value ("(none)" → "").</summary>
    private static string CompanyValue(string display) =>
        string.Equals(display, NoneLabel, StringComparison.Ordinal) ? string.Empty : display;

    /// <summary>Render every stored field of a license for the details pane.</summary>
    private static string FormatDetails(IssuedLicenseRecord r)
    {
        var sb = new StringBuilder();
        void L(string key, string? value) => sb.AppendLine($"{key,-15}{value}");

        L("License ID:", r.LicenseId);
        L("Product:", r.ProductName);
        L("License type:", r.LicenseType);
        L("Customer:", r.CustomerName);
        L("Company:", string.IsNullOrWhiteSpace(r.CompanyName) ? NoneLabel : r.CompanyName);
        L("Email:", r.ContactEmail);
        L("Phone:", r.ContactPhone);
        L("Gateway ID:", r.GatewayId);
        L("Issued:", r.IssuedAt);
        L("Expires:", r.ExpiresAt);
        L("Sources:", r.Sources);
        L("Destinations:", r.Destinations);
        L("Modules:", r.ModuleKeys);
        L("Limits:", $"sources {r.MaxSources}, sinks {r.MaxSinks}, routes {r.MaxRoutes}");
        L("Saved file:", r.SavedFilePath);
        L("Created:", $"{r.CreatedAtUtc} by {r.CreatedByUser}");
        sb.AppendLine();
        sb.AppendLine("--- Signed license.json ---");
        sb.AppendLine(r.LicenseJson);

        // Normalize to CRLF so the multiline TextBox renders line breaks correctly.
        return sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    /// <summary>
    /// Populate the (editable) New-License form from a stored license so the operator
    /// can UPDATE it. The License ID and Gateway ID are preserved, so re-submitting
    /// re-issues the SAME license (the history row keyed by License ID is replaced).
    /// Switches the form into New/edit view.
    /// </summary>
    private void LoadIntoForm(IssuedLicenseRecord r)
    {
        var pi = _product.Items.IndexOf(r.ProductName);
        if (pi >= 0)
        {
            _product.SelectedIndex = pi;
        }

        // Selecting the edition repopulates the offered protocol lists AND resets the
        // Limits to edition defaults — so do it FIRST, then apply the stored values.
        var ei = _edition.Items.IndexOf(r.LicenseType);
        if (ei >= 0)
        {
            _edition.SelectedIndex = ei;
        }

        _client.Text = r.CustomerName;
        _company.Text = r.CompanyName ?? string.Empty;
        _email.Text = r.ContactEmail ?? string.Empty;
        _phone.Text = r.ContactPhone ?? string.Empty;
        _gateway.Text = r.GatewayId;

        if (DateTime.TryParse(r.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exp))
        {
            if (exp < _expiry.MinDate) { exp = _expiry.MinDate; }
            if (exp > _expiry.MaxDate) { exp = _expiry.MaxDate; }
            _expiry.Value = exp;

            // For a loaded Trial Period license, back-fill the Trial-days control from
            // the loaded expiry WITHOUT letting its ValueChanged recompute (and thus
            // clobber) the loaded end-date, then toggle the expiry controls to trial mode.
            if (string.Equals(r.LicenseType, "Trial Period", StringComparison.Ordinal))
            {
                var days = (exp.Date - DateTime.Today).Days;
                if (days < (int)_trialDays.Minimum) { days = (int)_trialDays.Minimum; }
                if (days > (int)_trialDays.Maximum) { days = (int)_trialDays.Maximum; }

                _suppressCascade = true;
                try
                {
                    _trialDays.Value = days;
                    ApplyExpiryModeForEdition(); // suppressed: leaves the loaded expiry intact
                }
                finally { _suppressCascade = false; }
            }
        }

        // Keep the SAME License ID so Submit updates (INSERT OR REPLACE) the same row.
        _licenseId.Text = r.LicenseId;

        _maxSources.Value = ClampNum(r.MaxSources, _maxSources);
        _maxSinks.Value = ClampNum(r.MaxSinks, _maxSinks);
        _maxRoutes.Value = ClampNum(r.MaxRoutes, _maxRoutes);

        // Edition selection above already populated offered options + defaults;
        // now override with the stored selection for this license.
        _protocols.SetSelection(SplitCsv(r.Sources), SplitCsv(r.Destinations));

        _updateNote.Text =
            $"Updating existing license {r.LicenseId} (gateway {r.GatewayId}). Edit any field and click " +
            "Submit to re-issue — the history record with this License ID is replaced. Use the Clear button " +
            "to start a brand-new license instead.";
        _updateNote.Visible = true;

        _modeNew.Checked = true; // reveal the editable form
    }

    /// <summary>Split a stored comma-separated display list (e.g. Sources/Destinations).</summary>
    private static IEnumerable<string> SplitCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Clamp a stored integer into a NumericUpDown's valid range.</summary>
    private static decimal ClampNum(int value, NumericUpDown control)
    {
        if (value < control.Minimum) { return control.Minimum; }
        if (value > control.Maximum) { return control.Maximum; }
        return value;
    }

    /// <summary>Fill the Limits fields with per-edition defaults (operator may override).</summary>
    private void ApplyEditionLimits()
    {
        switch (_edition.SelectedItem as string)
        {
            case "Starter":
                _maxSources.Value = 1;
                _maxSinks.Value = 1;
                _maxRoutes.Value = 1;
                break;
            case "CNC Pro":
                _maxSources.Value = 25;
                _maxSinks.Value = 3;
                _maxRoutes.Value = 50;
                break;
            case "Enterprise":
                _maxSources.Value = 200;
                _maxSinks.Value = 20;
                _maxRoutes.Value = 500;
                break;
            case "Trial Period":
                // Trial lets them try everything.
                _maxSources.Value = 200;
                _maxSinks.Value = 20;
                _maxRoutes.Value = 500;
                break;
            default: // Professional
                _maxSources.Value = 50;
                _maxSinks.Value = 5;
                _maxRoutes.Value = 100;
                break;
        }
    }

    /// <summary>
    /// Rebuild the Sources/Destinations editor for the selected edition, mirroring
    /// <see cref="LicenseEditionCatalog"/> so the generator and the Studio pickers
    /// agree. Offering matrix and default selection:
    ///   Starter      → Modbus TCP source, MQTT destination
    ///   Professional → all sources, MQTT destination
    ///   Enterprise   → all sources, MQTT + OPC UA Server destinations
    /// The default selection is EVERY offered protocol, ticked. Changing the license
    /// type re-applies these defaults (restoring them if the operator had edited).
    /// </summary>
    private void PopulateProtocolsForEdition()
    {
        var (offeredSources, offeredDestinations) = OfferedProtocols();
        _protocols.SetOptions(offeredSources, offeredDestinations);
        _protocols.SetSelection(
            offeredSources.Select(o => o.Display),
            offeredDestinations.Select(o => o.Display));
    }

    /// <summary>
    /// Compute the source/destination protocols offered under the selected edition,
    /// applying the <see cref="LicenseEditionCatalog"/> policy (Starter surfaces a
    /// single Modbus variant — TCP, never RTU — even though both share a module).
    /// </summary>
    private (List<(string Display, string Key)> Sources, List<(string Display, string Key)> Destinations) OfferedProtocols()
    {
        var editionText = _edition.SelectedItem as string;

        // The two Sony/CNC license types are entitlement-by-explicit-module-key and
        // are NOT part of the Core LicenseEdition catalog, so special-case them by
        // string before the catalog path.
        switch (editionText)
        {
            case "CNC Pro":
            {
                // CNC-focused bundle: FOCAS2 + Brother HTTP + MTConnect sources, MQTT only.
                var cncDisplays = new[] { "FANUC FOCAS2", "Brother HTTP", "MTConnect" };
                var cncSources = cncDisplays
                    .Select(d => Sources.First(s => s.Display == d))
                    .Select(s => (s.Display, s.Key))
                    .ToList();
                var cncDestinations = Destinations
                    .Where(d => d.Display == "MQTT")
                    .Select(d => (d.Display, d.Key))
                    .ToList();
                return (cncSources, cncDestinations);
            }
            case "Trial Period":
            {
                // Trial lets them try everything — every source and destination.
                var trialSources = Sources.Select(s => (s.Display, s.Key)).ToList();
                var trialDestinations = Destinations.Select(d => (d.Display, d.Key)).ToList();
                return (trialSources, trialDestinations);
            }
        }

        var edition = ParseEdition(editionText);
        var restrictsSources = LicenseEditionCatalog.RestrictsSources(edition);

        var offeredSources = new List<(string Display, string Key)>();
        foreach (var (display, key) in Sources)
        {
            if (!LicenseEditionCatalog.IsSourceModuleOffered(edition, key))
            {
                continue;
            }
            if (restrictsSources && display != "Modbus TCP")
            {
                continue;
            }
            offeredSources.Add((display, key));
        }

        // Professional no longer offers OPC UA Client (Sony scoping); Starter and
        // Enterprise offerings are unchanged.
        if (edition == LicenseEdition.Professional)
        {
            offeredSources.RemoveAll(s => s.Key == "source-opcua-client");
        }

        var offeredDestinations = new List<(string, string)>();
        foreach (var (display, key) in Destinations)
        {
            if (LicenseEditionCatalog.IsSinkModuleOffered(edition, key))
            {
                offeredDestinations.Add((display, key));
            }
        }

        return (offeredSources, offeredDestinations);
    }

    /// <summary>
    /// Toggle the expiry controls for the selected license type. "Trial Period"
    /// uses a day-count (the year presets hide, the date picker becomes read-only
    /// but stays visible so the resulting end-date is shown); every other type uses
    /// the date picker + year presets.
    /// </summary>
    private void ApplyExpiryModeForEdition()
    {
        if (string.Equals(_edition.SelectedItem as string, "Trial Period", StringComparison.Ordinal))
        {
            _yearPresetsPanel.Visible = false;
            _trialDaysPanel.Visible = true;
            _expiry.Enabled = false;
            if (!_suppressCascade)
            {
                var target = DateTime.Today.AddDays((int)_trialDays.Value);
                if (target < _expiry.MinDate) { target = _expiry.MinDate; }
                if (target > _expiry.MaxDate) { target = _expiry.MaxDate; }
                _expiry.Value = target;
            }
        }
        else
        {
            _yearPresetsPanel.Visible = true;
            _trialDaysPanel.Visible = false;
            _expiry.Enabled = true;
        }
    }

    /// <summary>
    /// Reset the New-License form to a blank, fresh license (the "Clear" action) and
    /// show the New-License view. Discards any loaded-for-update state.
    /// </summary>
    private void ResetForm()
    {
        _client.Text = string.Empty;
        _company.Text = string.Empty;
        _email.Text = string.Empty;
        _phone.Text = string.Empty;
        _gateway.Text = string.Empty;

        _product.SelectedIndex = 0;
        _edition.SelectedIndex = 1; // Professional
        // Re-apply edition defaults (the editor selects every offered protocol).
        // Done explicitly in case the edition was already Professional (no event).
        ApplyEditionLimits();
        PopulateProtocolsForEdition();
        ApplyExpiryModeForEdition();

        _expiry.Value = DateTime.Today.AddYears(1);
        _licenseId.Text = NewLicenseId();

        _loadedRecord = null;
        _updateNote.Visible = false;

        _modeNew.Checked = true; // show the New-License window
    }

    // ------------------------------------------------------------------ UI helpers

    /// <summary>
    /// Build a field label, appending a red "*" when the field is mandatory.
    /// </summary>
    private static Control MakeFieldLabel(string text, bool required)
    {
        if (!required)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 8) };
        }

        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 8, 8), Padding = Padding.Empty };
        panel.Controls.Add(new Label { Text = text, AutoSize = true, Margin = Padding.Empty });
        panel.Controls.Add(new Label
        {
            Text = " *",
            AutoSize = true,
            ForeColor = Color.FromArgb(200, 40, 40),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Margin = Padding.Empty,
        });
        return panel;
    }

    /// <summary>A small flat button that sets the expiry to N years from today.</summary>
    private Button MakePresetButton(string text, int years)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            Padding = new Padding(8, 3, 8, 3),
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(6, 2, 0, 0),
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(214, 219, 226);
        btn.Click += (_, _) =>
        {
            var target = DateTime.Today.AddYears(years);
            if (target < _expiry.MinDate) { target = _expiry.MinDate; }
            if (target > _expiry.MaxDate) { target = _expiry.MaxDate; }
            _expiry.Value = target;
        };
        return btn;
    }

    /// <summary>Refresh the "valid for N days" hint beside the expiry picker.</summary>
    private void UpdateExpiryHint()
    {
        var days = (_expiry.Value.Date - DateTime.Today).Days;
        _expiryHint.Text = days <= 0 ? "(expires today)" : $"(valid for {days:N0} days)";
    }

    /// <summary>Load the embedded Elpis logo (ElpisLogo.png), or null if unavailable.</summary>
    private static Image? LoadEmbeddedLogo()
    {
        try
        {
            using var stream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("ElpisLogo.png");
            if (stream is null)
            {
                return null;
            }
            // Copy into a standalone Bitmap so the resource stream can be disposed.
            using var img = Image.FromStream(stream);
            return new Bitmap(img);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Generate a fresh, unique License ID.</summary>
    private static string NewLicenseId() =>
        "LIC-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    /// <summary>Warn + focus a control, returning false when the value is blank.</summary>
    private bool RequireField(string? value, string fieldName, Control control)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return true;
        }
        WarnField($"{fieldName} is required.", control);
        return false;
    }

    /// <summary>Show a warning dialog and move focus to the offending control.</summary>
    private void WarnField(string message, Control control)
    {
        MessageBox.Show(this, message, "Missing or invalid field", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        control.Focus();
    }

    /// <summary>Basic e-mail shape check (non-empty, single @, dotted domain).</summary>
    private static bool IsValidEmail(string value) =>
        Regex.IsMatch(value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    /// <summary>
    /// Gateway ID must be a hyphenated GUID (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx),
    /// or the single "*" wildcard for an any-machine license.
    /// </summary>
    private static bool IsValidGatewayId(string value) =>
        value == "*" || Guid.TryParseExact(value, "D", out _);

    /// <summary>Map the edition combo text to the Core <see cref="LicenseEdition"/> enum.</summary>
    private static LicenseEdition ParseEdition(string? edition) => edition switch
    {
        "Starter" => LicenseEdition.Starter,
        "Enterprise" => LicenseEdition.Enterprise,
        "Professional" => LicenseEdition.Professional,
        _ => LicenseEdition.Unknown,
    };

    private void OnGenerate(object? sender, EventArgs e)
    {
        var client = _client.Text.Trim();
        var gateway = _gateway.Text.Trim();
        var selectedKeys = _protocols.SourceKeys.Concat(_protocols.DestinationKeys).ToList();

        // Mandatory fields (marked * in the form): Product, Client, Company, Email, Gateway ID.
        if (!RequireField(_product.SelectedItem as string, "Product name", _product)) { return; }
        if (!RequireField(client, "Client name", _client)) { return; }
        if (!RequireField(_company.Text, "Company name", _company)) { return; }
        if (!RequireField(_email.Text, "Email", _email)) { return; }
        if (!IsValidEmail(_email.Text.Trim()))
        {
            WarnField("Enter a valid email address, e.g. name@company.com.", _email);
            return;
        }
        if (!RequireField(gateway, "Gateway ID", _gateway)) { return; }
        if (!IsValidGatewayId(gateway))
        {
            WarnField(
                "Gateway ID must be a valid GUID, e.g. f129be9d-bc86-4fe1-bf9c-6d68aab7102f.\n"
                + "(Use * for a license valid on any machine.)",
                _gateway);
            return;
        }
        if (selectedKeys.Count == 0)
        {
            MessageBox.Show(this, "Select at least one source or destination.", "Missing field", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Modules: core-runtime + the Studio UI are always included; then the
        // selected source/destination module keys (deduped by the dictionary).
        var modules = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["core-runtime"] = new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["enabled"] = true },
            ["connectivity-studio"] = new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["enabled"] = true },
        };
        foreach (var key in selectedKeys)
        {
            modules[key] = new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["enabled"] = true };
        }

        var product = (string)_product.SelectedItem!;
        var edition = (string)_edition.SelectedItem!;
        var expiresAt = _expiry.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var issuedAt = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var licenseId = string.IsNullOrWhiteSpace(_licenseId.Text)
            ? "LIC-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
            : _licenseId.Text.Trim();

        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["customer"] = client,
            ["product"] = product,
            ["edition"] = edition,
            ["expiresAt"] = expiresAt,
            ["gatewayId"] = gateway,
            ["issuedAt"] = issuedAt,
            ["licenseId"] = licenseId,
            ["limits"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["maxRoutes"] = (int)_maxRoutes.Value,
                ["maxSinkInstances"] = (int)_maxSinks.Value,
                ["maxSourceInstances"] = (int)_maxSources.Value,
            },
            ["modules"] = modules,
        };

        string signedJson;
        try
        {
            signedJson = SignLicense(payload);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Failed to sign the license:\n\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Policy: the license file is ALWAYS written to the folder the License
        // Generator runs from (never a user-chosen location). Named "edgelicense";
        // a numeric suffix is added if that name already exists so an earlier
        // file is never overwritten.
        var outputDir = AppContext.BaseDirectory;
        var outputPath = UniqueFilePath(outputDir, "edgelicense", ".json");

        try
        {
            File.WriteAllText(outputPath, signedJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Failed to write the file:\n\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Record the issued license in the local history database. A DB failure
        // must not lose the already-generated file, so warn but continue.
        var savedToDb = true;
        try
        {
            LicenseDatabase.Save(new IssuedLicenseRecord
            {
                LicenseId = licenseId,
                ProductName = product,
                CustomerName = client,
                CompanyName = _company.Text.Trim(),
                ContactEmail = _email.Text.Trim(),
                ContactPhone = _phone.Text.Trim(),
                LicenseType = edition,
                GatewayId = gateway,
                IssuedAt = issuedAt,
                ExpiresAt = expiresAt,
                Sources = string.Join(", ", _protocols.SourceDisplays),
                Destinations = string.Join(", ", _protocols.DestinationDisplays),
                ModuleKeys = string.Join(", ", modules.Keys),
                MaxSources = (int)_maxSources.Value,
                MaxSinks = (int)_maxSinks.Value,
                MaxRoutes = (int)_maxRoutes.Value,
                Signature = ExtractSignature(signedJson),
                LicenseJson = signedJson,
                SavedFilePath = outputPath,
            });
        }
        catch (Exception ex)
        {
            savedToDb = false;
            MessageBox.Show(
                this,
                "The license file was created, but saving it to the history database failed:\n\n"
                    + ex.Message + "\n\nDatabase: " + LicenseDatabase.DatabasePath,
                "Database warning",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        // Updating = the form was populated from Load License and the License ID is
        // still the loaded one, so the history row keyed by that License ID is
        // replaced rather than added.
        var isUpdate = _loadedRecord is not null
            && string.Equals(licenseId, _loadedRecord.LicenseId, StringComparison.Ordinal);
        var actionWord = isUpdate ? "updated" : "created";

        MessageBox.Show(
            this,
            "License " + actionWord + ":\n\n" + outputPath +
            "\n\nCustomer: " + client +
            "\nGateway: " + gateway +
            "\nEdition: " + edition +
            "\nExpires: " + expiresAt +
            (savedToDb
                ? "\n\n" + (isUpdate ? "Updated in" : "Saved to") + " history database: " + LicenseDatabase.DatabasePath
                : "") +
            "\n\nActivate it from the Studio License page (Activate License).",
            "License " + (isUpdate ? "updated" : "generated"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        // Update applied — drop the "updating…" banner.
        _updateNote.Visible = false;
    }

    /// <summary>
    /// Build "&lt;baseName&gt;&lt;ext&gt;" in <paramref name="dir"/>, appending
    /// " (n)" if a file with that name already exists, so nothing is overwritten.
    /// </summary>
    private static string UniqueFilePath(string dir, string baseName, string ext)
    {
        var path = Path.Combine(dir, baseName + ext);
        var n = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(dir, $"{baseName} ({n}){ext}");
            n++;
        }
        return path;
    }

    /// <summary>Pull the <c>signature</c> value out of the signed license JSON.</summary>
    private static string ExtractSignature(string signedJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(signedJson);
            return doc.RootElement.TryGetProperty("signature", out var sig)
                ? sig.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Serialize, canonicalize, RSA-PSS sign (via Core), and return the final
    /// indented JSON with the signature appended — matching LicenseGen exactly.
    /// </summary>
    private static string SignLicense(SortedDictionary<string, object?> payload)
    {
        var unsignedJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
        var canonical = CanonicalJson.Canonicalize(unsignedJson);
        var signature = LicenseSignatureValidator.SignWithPrivateKey(canonical, DevSigningKey.PrivatePem);

        var withSig = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(unsignedJson)!;
        var finalDoc = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kvp in withSig)
        {
            finalDoc[kvp.Key] = JsonSerializer.Deserialize<object?>(kvp.Value.GetRawText());
        }
        finalDoc["signature"] = signature;

        return JsonSerializer.Serialize(finalDoc, new JsonSerializerOptions { WriteIndented = true });
    }
}
