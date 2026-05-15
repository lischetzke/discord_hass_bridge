using System;
using System.Drawing;
using System.Windows.Forms;
using DiscordHass.App;
using DiscordHass.Config;
using DiscordHass.Discord;

namespace DiscordHass.Ui;

internal sealed class StatusForm : Form
{
    private readonly AppConfig _config;
    private readonly BridgeService _bridge;
    private readonly Label _discordStatusLabel;
    private readonly Label _haStatusLabel;
    private readonly System.Collections.Generic.Dictionary<string, Label> _flagNameLabels = new();
    private readonly System.Collections.Generic.Dictionary<string, Label> _flagValueLabels = new();

    public StatusForm(AppConfig config, BridgeService bridge)
    {
        _config = config;
        _bridge = bridge;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = $"{AppConstants.DisplayName} — Status";
        ClientSize = new Size(520, 420);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        Label discordHeader = new() { Text = "Discord", Font = new Font(Font, FontStyle.Bold), Location = new Point(16, 16), AutoSize = true };
        _discordStatusLabel = new Label { Location = new Point(16, 38), Width = 490, Height = 20, AutoSize = false, Text = "" };

        Label haHeader = new() { Text = "Home Assistant", Font = new Font(Font, FontStyle.Bold), Location = new Point(16, 72), AutoSize = true };
        _haStatusLabel = new Label { Location = new Point(16, 94), Width = 490, Height = 20, AutoSize = false, Text = "" };

        Label statesHeader = new() { Text = "Current state flags", Font = new Font(Font, FontStyle.Bold), Location = new Point(16, 130), AutoSize = true };
        Controls.AddRange(new Control[] { discordHeader, _discordStatusLabel, haHeader, _haStatusLabel, statesHeader });

        int y = 154;
        foreach (StateFlagDefinition def in StateFlagDefinitions.All)
        {
            Label name = new() { Text = "", Location = new Point(28, y), Width = 270, Height = 20, AutoSize = false };
            Label value = new() { Text = "—", Location = new Point(304, y), Width = 200, Height = 20, AutoSize = false };
            _flagNameLabels[def.FlagId] = name;
            _flagValueLabels[def.FlagId] = value;
            Controls.Add(name);
            Controls.Add(value);
            y += 22;
        }

        _bridge.StatusChanged += OnAnyChange;
        _bridge.VoiceStateChanged += OnAnyChange;
        FormClosed += (_, _) =>
        {
            _bridge.StatusChanged -= OnAnyChange;
            _bridge.VoiceStateChanged -= OnAnyChange;
        };

        ResumeLayout(performLayout: true);
        Refresh();
    }

    private void OnAnyChange(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(Refresh));
        }
        else
        {
            Refresh();
        }
    }

    public new void Refresh()
    {
        _discordStatusLabel.Text = FormatPhase(_bridge.DiscordStatus, _bridge.DiscordUserName);
        _haStatusLabel.Text = FormatPhase(_bridge.HaStatus, null);

        VoiceState s = _bridge.CurrentVoiceState;
        foreach (StateFlagDefinition def in StateFlagDefinitions.All)
        {
            EffectiveStateFlag eff = FlagResolver.Resolve(def, _config);
            if (_flagNameLabels.TryGetValue(def.FlagId, out Label? nameLabel))
            {
                nameLabel.Text = eff.FriendlyName;
            }
            if (!_flagValueLabels.TryGetValue(def.FlagId, out Label? valueLabel)) continue;

            if (!_config.EnabledFlags.Contains(def.FlagId))
            {
                valueLabel.Text = "disabled";
                valueLabel.ForeColor = Color.DimGray;
                continue;
            }
            bool on = eff.ValueSelector(s);
            valueLabel.Text = on ? "ON" : "off";
            valueLabel.ForeColor = on ? Color.SeaGreen : Color.DimGray;
        }

        base.Refresh();
    }

    private static string FormatPhase(ConnectionStatus status, string? extra)
    {
        string baseText = status.Phase switch
        {
            ConnectionPhase.Idle => "Idle",
            ConnectionPhase.Connecting => "Connecting…",
            ConnectionPhase.Connected => extra is null ? "Connected" : $"Connected as {extra}",
            ConnectionPhase.Reconnecting => "Reconnecting…",
            ConnectionPhase.Faulted => "Faulted",
            _ => status.Phase.ToString(),
        };
        return status.LastError is null ? baseText : $"{baseText} — {status.LastError}";
    }
}
