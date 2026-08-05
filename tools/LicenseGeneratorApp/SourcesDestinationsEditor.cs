using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ElpisEdgeConnect.LicenseGeneratorApp;

/// <summary>
/// Sources &amp; Destinations picker. Each section (Sources, Destinations) shows its
/// header with a "Select all / Unselect all" toggle beside it, followed by the offered
/// protocols laid out in a grid that grows to at most three rows (columns are chosen
/// from the item count). The ticked boxes ARE the selection — read live, no apply/clear step.
///
/// The offered protocols and the default selection are pushed in by the host form
/// via <see cref="SetOptions"/> / <see cref="SetSelection"/>; changing the license
/// type restores the edition defaults by calling those again.
/// </summary>
public sealed class SourcesDestinationsEditor : UserControl
{
    private static readonly Color Accent = Color.FromArgb(46, 84, 150);
    private static readonly Color Muted = Color.FromArgb(96, 96, 96);
    private static readonly Color CardFill = Color.FromArgb(249, 250, 252);
    // Sources fill up to 2 rows (wide); Destinations sit in up to 2 columns / 1 row.
    private const int SourceMaxRows = 2;
    private const int SourceMaxColumns = 5;
    private const int DestMaxRows = 1;
    private const int DestMaxColumns = 2;

    // Shared fixed column width so Sources and Destinations columns line up.
    private const int ColumnWidth = 145;

    // Offered options (display -> module key), in offer order.
    private readonly List<(string Display, string Key)> _sourceOptions = new();
    private readonly List<(string Display, string Key)> _destOptions = new();

    private readonly TableLayoutPanel _sourceGrid = NewGrid();
    private readonly TableLayoutPanel _destGrid = NewGrid();
    private readonly CheckBox _sourceAll = NewSelectAll();
    private readonly CheckBox _destAll = NewSelectAll();

    private bool _syncing;

    public SourcesDestinationsEditor()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        // Styled card = a single-column TableLayoutPanel, the only (non-docked) child
        // of the UserControl, so AutoSize propagates cleanly.
        var card = new TableLayoutPanel
        {
            Location = new Point(0, 0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = CardFill,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(14, 12, 14, 12),
            ColumnCount = 1,
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        card.Controls.Add(HeaderRow(Header("SOURCES"), _sourceAll));
        card.Controls.Add(_sourceGrid);
        card.Controls.Add(new Label { Text = "", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        card.Controls.Add(HeaderRow(Header("DESTINATIONS"), _destAll));
        card.Controls.Add(_destGrid);

        Controls.Add(card);

        _sourceAll.Click += (_, _) => ToggleAll(_sourceGrid, _sourceAll);
        _destAll.Click += (_, _) => ToggleAll(_destGrid, _destAll);
    }

    // ---------------------------------------------------------------- public API

    /// <summary>Replace the offered protocol lists and rebuild the checkbox grids.</summary>
    public void SetOptions(
        IEnumerable<(string Display, string Key)> sources,
        IEnumerable<(string Display, string Key)> destinations)
    {
        _sourceOptions.Clear();
        _sourceOptions.AddRange(sources);
        _destOptions.Clear();
        _destOptions.AddRange(destinations);

        Rebuild(_sourceGrid, _sourceOptions, SourceMaxRows, SourceMaxColumns);
        Rebuild(_destGrid, _destOptions, DestMaxRows, DestMaxColumns);

        // "Select all / Unselect all" only makes sense with 2+ options — hide it for
        // single-option sections (e.g. Starter: Modbus TCP source, MQTT destination).
        _sourceAll.Visible = _sourceOptions.Count > 1;
        _destAll.Visible = _destOptions.Count > 1;
    }

    /// <summary>Tick exactly these display names; anything else is unticked.</summary>
    public void SetSelection(IEnumerable<string> sourceDisplays, IEnumerable<string> destinationDisplays)
    {
        ApplyChecked(_sourceGrid, new HashSet<string>(sourceDisplays, StringComparer.Ordinal));
        ApplyChecked(_destGrid, new HashSet<string>(destinationDisplays, StringComparer.Ordinal));
        RefreshSelectAll(_sourceGrid, _sourceAll);
        RefreshSelectAll(_destGrid, _destAll);
    }

    /// <summary>Display names of the ticked source protocols.</summary>
    public IReadOnlyCollection<string> SourceDisplays => CheckedDisplays(_sourceGrid);

    /// <summary>Display names of the ticked destination protocols.</summary>
    public IReadOnlyCollection<string> DestinationDisplays => CheckedDisplays(_destGrid);

    /// <summary>License module keys of the ticked source protocols.</summary>
    public IEnumerable<string> SourceKeys => CheckedKeys(_sourceGrid, _sourceOptions);

    /// <summary>License module keys of the ticked destination protocols.</summary>
    public IEnumerable<string> DestinationKeys => CheckedKeys(_destGrid, _destOptions);

    // ------------------------------------------------------------------ internals

    private void Rebuild(TableLayoutPanel grid, List<(string Display, string Key)> options, int maxRows, int maxColumns)
    {
        grid.SuspendLayout();
        grid.Controls.Clear();
        grid.RowStyles.Clear();
        grid.ColumnStyles.Clear();

        if (options.Count == 0)
        {
            grid.ColumnCount = 1;
            grid.Controls.Add(new Label { Text = "(none offered)", AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 3, 0, 3) });
            grid.ResumeLayout();
            return;
        }

        // Choose the column count so the grid fills at most maxRows rows, capped at
        // maxColumns so long protocol names still fit.
        var columns = Math.Min(maxColumns, Math.Max(1, (int)Math.Ceiling(options.Count / (double)maxRows)));
        grid.ColumnCount = columns;
        for (var c = 0; c < columns; c++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ColumnWidth));
        }

        foreach (var (display, _) in options)
        {
            var cb = new CheckBox
            {
                Text = display,
                Tag = display,
                AutoSize = true,
                Margin = new Padding(0, 3, 4, 3),
            };
            cb.CheckedChanged += (_, _) => OnItemChanged(grid);
            grid.Controls.Add(cb);
        }

        grid.ResumeLayout();
    }

    private static void ApplyChecked(TableLayoutPanel grid, HashSet<string> wanted)
    {
        foreach (var cb in grid.Controls.OfType<CheckBox>())
        {
            cb.Checked = wanted.Contains((string)cb.Tag!);
        }
    }

    private void SetAllChecked(TableLayoutPanel grid, bool value)
    {
        foreach (var cb in grid.Controls.OfType<CheckBox>())
        {
            cb.Checked = value;
        }
    }

    /// <summary>
    /// Handle the toggle click: if everything is already ticked, untick all;
    /// otherwise tick all. The label then flips to match.
    /// </summary>
    private void ToggleAll(TableLayoutPanel grid, CheckBox toggle)
    {
        var boxes = grid.Controls.OfType<CheckBox>().ToList();
        var allChecked = boxes.Count > 0 && boxes.All(c => c.Checked);
        SetAllChecked(grid, !allChecked);
        RefreshSelectAll(grid, toggle);
    }

    private void OnItemChanged(TableLayoutPanel grid)
    {
        if (_syncing)
        {
            return;
        }
        RefreshSelectAll(grid, grid == _sourceGrid ? _sourceAll : _destAll);
    }

    /// <summary>Reflect the grid's state in its Select-all / Unselect-all toggle.</summary>
    private void RefreshSelectAll(TableLayoutPanel grid, CheckBox toggle)
    {
        _syncing = true;
        var boxes = grid.Controls.OfType<CheckBox>().ToList();
        var allChecked = boxes.Count > 0 && boxes.All(c => c.Checked);
        toggle.Checked = allChecked;
        toggle.Text = allChecked ? "Unselect all" : "Select all";
        toggle.Enabled = boxes.Count > 0;
        _syncing = false;
    }

    private static IReadOnlyCollection<string> CheckedDisplays(TableLayoutPanel grid) =>
        grid.Controls.OfType<CheckBox>().Where(c => c.Checked).Select(c => (string)c.Tag!).ToList();

    private static IEnumerable<string> CheckedKeys(TableLayoutPanel grid, List<(string Display, string Key)> options)
    {
        var keyByDisplay = options.ToDictionary(o => o.Display, o => o.Key, StringComparer.Ordinal);
        return grid.Controls.OfType<CheckBox>()
            .Where(c => c.Checked)
            .Select(c => keyByDisplay.TryGetValue((string)c.Tag!, out var k) ? k : null)
            .Where(k => k is not null)
            .Select(k => k!);
    }

    // ------------------------------------------------------------ factory helpers

    private static TableLayoutPanel NewGrid() => new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        Margin = new Padding(0, 2, 0, 0),
    };

    private static CheckBox NewSelectAll() => new()
    {
        Text = "Select all",
        AutoSize = true,
        AutoCheck = false, // we drive Checked/label ourselves to mirror the grid
        ForeColor = Accent,
        Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
        Margin = new Padding(0, 0, 0, 5),
    };

    private static Label Header(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Accent,
        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
        Margin = new Padding(0, 0, 0, 2),
    };

    /// <summary>
    /// Lay a section header and its Select-all / Unselect-all toggle on a single
    /// row, the toggle sitting immediately beside the header (SOURCES / DESTINATIONS).
    /// </summary>
    private static FlowLayoutPanel HeaderRow(Label header, CheckBox selectAll)
    {
        // Nudge the header down slightly so its baseline lines up with the
        // checkbox glyph that sits to its right.
        header.Margin = new Padding(0, 2, 12, 2);
        selectAll.Margin = new Padding(0, 1, 0, 2);

        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 2),
            Padding = new Padding(0),
        };
        row.Controls.Add(header);
        row.Controls.Add(selectAll);
        return row;
    }
}
