using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DiscordHass.App;
using DiscordHass.Config;
using DiscordHass.Discord;

namespace DiscordHass.Ui;

/// <summary>
/// v0.2.0 front-door window. Header strip with app identity + two state pills (Discord, HA),
/// an identity row with the connected Discord user and an HA URL link, a 2×N grid of state-flag
/// tiles, and an action bar at the bottom. Subscribes to <see cref="BridgeService"/> events and
/// refreshes synchronously from its public properties.
///
/// Layout is built with docking so it survives WinForms DPI scaling and avoids "the form
/// hasn't been laid out yet" gotchas with <c>header.Width</c> at construction time.
/// </summary>
internal sealed class OverviewForm : Form
{
    private readonly AppConfig _config;
    private readonly BridgeService _bridge;

    private Label _discordPill = null!;
    private Label _haPill = null!;
    private Label _identityLabel = null!;
    private LinkLabel _haUrlLink = null!;
    private FlowLayoutPanel _tilesPanel = null!;
    private readonly List<FlagTile> _tiles = new();

    public event EventHandler? SettingsRequested;
    public event EventHandler? ReconnectRequested;
    public event EventHandler? OpenHaRequested;
    public event EventHandler? OpenHelpRequested;
    public event EventHandler? CopyDiagnosticsRequested;

    public OverviewForm(AppConfig config, BridgeService bridge)
    {
        _config = config;
        _bridge = bridge;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = $"{AppConstants.DisplayName} — Overview";
        ClientSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = true;
        BackColor = ThemeColors.Background;
        ForeColor = ThemeColors.OnSurface;

        // Docking order matters and the rule is non-obvious: WinForms applies
        // Controls[Count-1] (the most recently added control) FIRST when computing the dock
        // layout. So for sibling Dock=Top controls, the *last* one added takes the topmost
        // slot. The Fill panel must be added FIRST so its z-order is at the back AND so it
        // claims whatever vertical space is left only AFTER every other Dock has been
        // resolved — otherwise the Fill bounding box doesn't subtract the Top areas added
        // after it, which is exactly the bug that left rows 1–2 of the state tiles tucked
        // behind the Header band.
        //
        // Result with this order: Header (last Top added) at the very top, IdentityRow
        // beneath it, the state tiles in the middle, ActionBar at the bottom.
        BuildTilesPanel();
        BuildActionBar();
        BuildIdentityRow();
        BuildHeader();

        _bridge.StatusChanged    += OnAnyChange;
        _bridge.VoiceStateChanged += OnAnyChange;
        FormClosed += (_, _) =>
        {
            _bridge.StatusChanged    -= OnAnyChange;
            _bridge.VoiceStateChanged -= OnAnyChange;
        };

        ResumeLayout(performLayout: true);
        RefreshAll();
    }

    private void BuildHeader()
    {
        Panel header = new()
        {
            Dock = DockStyle.Top,
            Height = 80,
            BackColor = ThemeColors.Surface,
        };

        Label appName = new()
        {
            Text = AppConstants.DisplayName,
            Font = new Font(Font.FontFamily, Font.Size + 6F, FontStyle.Bold),
            ForeColor = ThemeColors.OnSurface,
            Location = new Point(20, 14),
            AutoSize = true,
        };
        Label versionLabel = new()
        {
            Text = $"v{AppConstants.GetVersionString()}",
            ForeColor = ThemeColors.OnSurfaceDim,
            Location = new Point(22, 46),
            AutoSize = true,
        };

        _discordPill = MakePill("Discord: idle");
        _haPill = MakePill("HA: idle");

        // Right-docked FlowLayoutPanel so the pills always sit a constant distance from the
        // right edge, regardless of when layout settles. RightToLeft adds children right-first.
        FlowLayoutPanel pillRow = new()
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Right,
            Width = 360,
            BackColor = ThemeColors.Surface,
            Padding = new Padding(0, 22, 20, 0),
            WrapContents = false,
        };
        _haPill.Margin = new Padding(8, 0, 0, 0);
        _discordPill.Margin = new Padding(8, 0, 0, 0);
        pillRow.Controls.Add(_haPill);
        pillRow.Controls.Add(_discordPill);

        header.Controls.Add(pillRow);
        header.Controls.Add(appName);
        header.Controls.Add(versionLabel);
        Controls.Add(header);
    }

    private static Label MakePill(string initialText) => new()
    {
        Text = initialText,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleCenter,
        Size = new Size(160, 30),
        ForeColor = ThemeColors.StatusForeground,
        BackColor = ThemeColors.SurfaceMuted,
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
    };

    private void BuildIdentityRow()
    {
        Panel row = new()
        {
            Dock = DockStyle.Top,
            Height = 32,
            BackColor = ThemeColors.Background,
        };
        _identityLabel = new Label
        {
            Location = new Point(20, 8),
            Text = "",
            AutoSize = true,
            ForeColor = ThemeColors.OnSurfaceDim,
        };
        _haUrlLink = new LinkLabel
        {
            Text = "",
            AutoSize = true,
            LinkColor = ThemeColors.Accent,
            ActiveLinkColor = ThemeColors.Accent,
            VisitedLinkColor = ThemeColors.Accent,
            BackColor = ThemeColors.Background,
            Visible = false,
        };
        _haUrlLink.LinkClicked += (_, _) => OpenHaRequested?.Invoke(this, EventArgs.Empty);

        // Stick the URL link to the right edge using a Right-docked container.
        Panel urlSlot = new()
        {
            Dock = DockStyle.Right,
            Width = 360,
            BackColor = ThemeColors.Background,
            Padding = new Padding(0, 8, 20, 0),
        };
        urlSlot.Controls.Add(_haUrlLink);
        _haUrlLink.Dock = DockStyle.Right;
        _haUrlLink.TextAlign = ContentAlignment.MiddleRight;

        row.Controls.Add(urlSlot);
        row.Controls.Add(_identityLabel);
        Controls.Add(row);
    }

    private void BuildTilesPanel()
    {
        // FlowLayoutPanel with fixed-size tiles. Previous TableLayoutPanel attempt let the last
        // row (with an orphan Busy tile) stretch to fill leftover vertical space because the
        // RowStyle entries didn't perfectly match RowCount. Fixed-size children + FlowLayout
        // sidesteps that whole class of bug — each tile is exactly TileHeight tall, always.
        const int padding = 16;
        const int tileMargin = 4;
        const int tileHeight = 76;
        int tileWidth = (ClientSize.Width - padding * 2 - tileMargin * 4) / 2; // 2 columns

        _tilesPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = ThemeColors.Background,
            Padding = new Padding(padding, 8, padding, 8),
            AutoScroll = false,
        };

        foreach (StateFlagDefinition def in StateFlagDefinitions.All)
        {
            FlagTile tile = new(def, _config, _bridge)
            {
                Size = new Size(tileWidth, tileHeight),
                Margin = new Padding(tileMargin),
            };
            _tilesPanel.Controls.Add(tile);
            _tiles.Add(tile);
        }
        Controls.Add(_tilesPanel);
    }

    private void BuildActionBar()
    {
        Panel bar = new()
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            BackColor = ThemeColors.Surface,
        };

        Button btnSettings   = MakeAction("Settings");
        btnSettings.Click   += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        Button btnReconnect  = MakeAction("Reconnect");
        btnReconnect.Click  += (_, _) => ReconnectRequested?.Invoke(this, EventArgs.Empty);
        Button btnOpenHa     = MakeAction("Open HA");
        btnOpenHa.Click     += (_, _) => OpenHaRequested?.Invoke(this, EventArgs.Empty);
        Button btnDiag       = MakeAction("Diagnostics");
        btnDiag.Click       += (_, _) => CopyDiagnosticsRequested?.Invoke(this, EventArgs.Empty);
        Button btnHelp       = MakeAction("Help");
        btnHelp.Click       += (_, _) => OpenHelpRequested?.Invoke(this, EventArgs.Empty);

        // FlowLayoutPanel right-docked. RightToLeft flow → add buttons right-first so the
        // visual order left-to-right is Settings | Reconnect | Open HA | Diagnostics | Help.
        FlowLayoutPanel buttons = new()
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            BackColor = ThemeColors.Surface,
            Padding = new Padding(0, 14, 16, 0),
            WrapContents = false,
        };
        foreach (Button b in new[] { btnHelp, btnDiag, btnOpenHa, btnReconnect, btnSettings })
        {
            b.Margin = new Padding(8, 0, 0, 0);
            buttons.Controls.Add(b);
        }

        bar.Controls.Add(buttons);
        Controls.Add(bar);
    }

    private static Button MakeAction(string text)
    {
        Button b = new()
        {
            Text = text,
            Width = 110,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeColors.SurfaceMuted,
            ForeColor = ThemeColors.OnSurface,
            TabStop = true,
            UseCompatibleTextRendering = false,
        };
        b.FlatAppearance.BorderColor = ThemeColors.Divider;
        b.FlatAppearance.MouseOverBackColor = ThemeColors.Surface;
        b.FlatAppearance.MouseDownBackColor = ThemeColors.SurfaceMuted;
        return b;
    }

    private void OnAnyChange(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(new Action(RefreshAll));
        else RefreshAll();
    }

    private void RefreshAll()
    {
        SetPill(_discordPill, "Discord", _bridge.DiscordStatus.Phase);
        SetPill(_haPill,      "HA",      _bridge.HaStatus.Phase);

        string user = _bridge.DiscordUserName ?? "—";
        _identityLabel.Text = _bridge.DiscordStatus.Phase == ConnectionPhase.Connected
            ? $"Connected as {user}"
            : "Not connected to Discord";

        if (!string.IsNullOrWhiteSpace(_config.HaBaseUrl))
        {
            _haUrlLink.Text = _config.HaBaseUrl;
            _haUrlLink.Visible = true;
        }
        else
        {
            _haUrlLink.Visible = false;
        }

        foreach (FlagTile tile in _tiles) tile.RefreshFromBridge();
    }

    private static void SetPill(Label pill, string prefix, ConnectionPhase phase)
    {
        string label = phase switch
        {
            ConnectionPhase.Idle         => "idle",
            ConnectionPhase.Connecting   => "connecting…",
            ConnectionPhase.Connected    => "connected",
            ConnectionPhase.Reconnecting => "reconnecting…",
            ConnectionPhase.Faulted      => "faulted",
            _                            => phase.ToString().ToLowerInvariant(),
        };
        pill.Text = $"{prefix}: {label}";
        pill.BackColor = phase switch
        {
            ConnectionPhase.Connected    => ThemeColors.StatusOk,
            ConnectionPhase.Connecting   => ThemeColors.StatusWarn,
            ConnectionPhase.Reconnecting => ThemeColors.StatusWarn,
            ConnectionPhase.Faulted      => ThemeColors.StatusError,
            _                            => ThemeColors.SurfaceMuted,
        };
        pill.ForeColor = ThemeColors.StatusForeground;
    }

    /// <summary>
    /// One state-flag tile. Two rows of content: top row has the friendly name (left,
    /// truncated with ellipsis if it overflows) and the ON/off/disabled badge (right);
    /// bottom row has the resolved entity_id slug (truncated). Layout uses a
    /// TableLayoutPanel inside so the name auto-clips when the prefix is long instead of
    /// running into the badge — the exact bug captured in the v0.2.0 user feedback.
    /// </summary>
    private sealed class FlagTile : Panel
    {
        private readonly StateFlagDefinition _def;
        private readonly AppConfig _config;
        private readonly BridgeService _bridge;
        private readonly Label _name;
        private readonly Label _value;
        private readonly Label _slug;

        public FlagTile(StateFlagDefinition def, AppConfig config, BridgeService bridge)
        {
            _def = def;
            _config = config;
            _bridge = bridge;

            BackColor = ThemeColors.Surface;
            BorderStyle = BorderStyle.FixedSingle;

            TableLayoutPanel layout = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = ThemeColors.Surface,
                Padding = new Padding(10, 8, 10, 8),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _name = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, Font.Size, FontStyle.Bold),
                ForeColor = ThemeColors.OnSurface,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0),
                Margin = new Padding(0),
            };
            _value = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(Font.FontFamily, Font.Size, FontStyle.Bold),
                Margin = new Padding(0),
            };
            _slug = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                ForeColor = ThemeColors.OnSurfaceDim,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 4, 0, 0),
            };

            layout.Controls.Add(_name,  column: 0, row: 0);
            layout.Controls.Add(_value, column: 1, row: 0);
            layout.SetColumnSpan(_slug, 2);
            layout.Controls.Add(_slug,  column: 0, row: 1);

            Controls.Add(layout);
        }

        public void RefreshFromBridge()
        {
            EffectiveStateFlag eff = FlagResolver.Resolve(_def, _config);
            _name.Text = eff.FriendlyName;
            _slug.Text = $"input_boolean.{eff.EntityIdSlug}";

            if (!_config.EnabledFlags.Contains(_def.FlagId))
            {
                _value.Text = "disabled";
                _value.BackColor = ThemeColors.SurfaceMuted;
                _value.ForeColor = ThemeColors.OnSurfaceDim;
                return;
            }

            bool on = eff.ValueSelector(_bridge.CurrentVoiceState);
            _value.Text = on ? "ON" : "off";
            _value.BackColor = on ? ThemeColors.StatusOk : ThemeColors.SurfaceMuted;
            _value.ForeColor = on ? ThemeColors.StatusForeground : ThemeColors.OnSurfaceDim;
        }
    }
}
