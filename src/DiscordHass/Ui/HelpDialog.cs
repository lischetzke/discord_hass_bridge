using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DiscordHass.Ui;

/// <summary>
/// Modal popup that renders one <see cref="HelpTopic"/> — a title, a body in a read-only
/// multiline TextBox (selectable, DPI-correct), and any associated links as clickable rows.
/// Use <see cref="ShowTopic"/> rather than constructing directly.
/// </summary>
internal sealed class HelpDialog : Form
{
    public static void ShowTopic(IWin32Window? owner, string topicId)
    {
        HelpTopic topic = HelpContent.Get(topicId);
        using HelpDialog dlg = new(topic);
        if (owner is Form parent && !parent.IsDisposed)
        {
            dlg.ShowDialog(parent);
        }
        else
        {
            dlg.ShowDialog();
        }
    }

    private HelpDialog(HelpTopic topic)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = topic.Title;
        ClientSize = new Size(520, 360);
        BackColor = ThemeColors.Background;
        ForeColor = ThemeColors.OnSurface;

        Label title = new()
        {
            Text = topic.Title,
            AutoSize = false,
            Location = new Point(16, 16),
            Size = new Size(ClientSize.Width - 32, 24),
            Font = new Font(Font.FontFamily, Font.Size + 2F, FontStyle.Bold),
            ForeColor = ThemeColors.OnSurface,
        };
        Controls.Add(title);

        TextBox body = new()
        {
            Text = topic.Body,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = ThemeColors.Surface,
            ForeColor = ThemeColors.OnSurface,
            Location = new Point(16, 48),
            Size = new Size(ClientSize.Width - 32, ClientSize.Height - 48 - 60 - Math.Max(0, topic.Links.Count) * 22),
            WordWrap = true,
            TabStop = false,
        };
        Controls.Add(body);

        int y = body.Bottom + 12;
        foreach (HelpLink link in topic.Links)
        {
            LinkLabel ll = new()
            {
                Text = link.Caption + " →",
                AutoSize = true,
                Location = new Point(16, y),
                LinkColor = ThemeColors.Accent,
                ActiveLinkColor = ThemeColors.Accent,
                VisitedLinkColor = ThemeColors.Accent,
                BackColor = ThemeColors.Background,
            };
            string url = link.Url;
            ll.LinkClicked += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
                catch { /* best effort */ }
            };
            Controls.Add(ll);
            y += 22;
        }

        Button close = new()
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            Size = new Size(88, 28),
            Location = new Point(ClientSize.Width - 88 - 16, ClientSize.Height - 28 - 12),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };
        close.Click += (_, _) => Close();
        Controls.Add(close);
        AcceptButton = close;
        CancelButton = close;

        ResumeLayout(performLayout: true);
    }
}
