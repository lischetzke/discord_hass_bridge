using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace DiscordHass.Ui;

/// <summary>
/// One section of the Settings form. Renders as a clickable title row (with an optional status
/// chip on the right) and a content area underneath that can be expanded or collapsed. Used to
/// fit the General / Home Assistant / Discord / States sections into one scrollable surface
/// without a TabControl (which doesn't honour theme colors).
///
/// Usage:
///   var sec = new CollapsiblePanel("Home Assistant") { Dock = DockStyle.Top };
///   sec.ContentArea.Controls.Add(...);
///   sec.ContentHeight = 220;            // height of the body when expanded
///   parent.Controls.Add(sec);
///
/// The control resizes itself: total height is HeaderHeight when collapsed, or
/// HeaderHeight + ContentHeight when expanded. Click anywhere on the header to toggle.
/// </summary>
internal sealed class CollapsiblePanel : Panel
{
    private const int HeaderHeight = 36;

    private readonly Panel _header;
    private readonly Label _chevron;
    private readonly Label _titleLabel;
    private readonly Label _statusChip;
    private readonly Panel _content;
    private readonly Panel _contentInner;
    private bool _expanded;
    private int _contentHeight;

    /// <summary>
    /// Where callers should add their controls. This is a Dock=Fill inner panel that sits
    /// inside <c>_content</c>'s padding, so the small top/bottom margins of the section
    /// are respected even though child controls use explicit Location values (Panel.Padding
    /// alone does not shift absolutely-positioned children).
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Panel ContentArea => _contentInner;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => _titleLabel.Text;
        set => _titleLabel.Text = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Expanded
    {
        get => _expanded;
        set { _expanded = value; ApplyLayout(); }
    }

    /// <summary>Height of the content body when expanded. Set this once after adding child controls.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ContentHeight
    {
        get => _contentHeight;
        set { _contentHeight = value; ApplyLayout(); }
    }

    public event EventHandler? ExpansionChanged;

    public CollapsiblePanel(string title)
    {
        BackColor = ThemeColors.Background;
        Margin = new Padding(0, 0, 0, 8);

        _header = new Panel
        {
            Dock = DockStyle.Top,
            Height = HeaderHeight,
            BackColor = ThemeColors.Surface,
            Cursor = Cursors.Hand,
        };
        _chevron = new Label
        {
            Text = "▶",                // ▶ collapsed, swapped to ▼ when expanded
            AutoSize = false,
            Size = new Size(24, HeaderHeight),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = ThemeColors.OnSurfaceDim,
            Cursor = Cursors.Hand,
            Location = new Point(8, 0),
        };
        _titleLabel = new Label
        {
            Text = title,
            AutoSize = false,
            Location = new Point(36, 0),
            Size = new Size(400, HeaderHeight),
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = ThemeColors.OnSurface,
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand,
        };
        _statusChip = new Label
        {
            AutoSize = false,
            Size = new Size(190, 22),
            TextAlign = ContentAlignment.MiddleCenter,
            Visible = false,
            ForeColor = ThemeColors.StatusForeground,
            BackColor = ThemeColors.StatusWarn,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };

        // Click anywhere in the header toggles the section.
        EventHandler toggle = (_, _) => { Expanded = !Expanded; ExpansionChanged?.Invoke(this, EventArgs.Empty); };
        _header.Click += toggle;
        _chevron.Click += toggle;
        _titleLabel.Click += toggle;

        _header.Controls.Add(_chevron);
        _header.Controls.Add(_titleLabel);
        _header.Controls.Add(_statusChip);

        // Outer content host. Padding here defines the small top + slightly larger bottom
        // margin around each section's body. Left/right padding stays at 0 because the
        // scrollable settings host already insets its sections by 20 px — adding more
        // here would double-indent every label and textbox.
        _content = new Panel
        {
            Dock = DockStyle.Top,
            BackColor = ThemeColors.Background,
            Padding = new Padding(0, 12, 0, 18),
            Height = 0,
            Visible = false,
        };
        // Fill-docked wrapper that respects _content.Padding. Callers receive this via
        // ContentArea; controls inside use Location values relative to this wrapper, so
        // the padding shifts everything down by 12 px without changing any existing
        // BuildXxxContent code.
        _contentInner = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeColors.Background,
        };
        _content.Controls.Add(_contentInner);

        Controls.Add(_content);
        Controls.Add(_header);

        _header.Resize += (_, _) =>
        {
            _statusChip.Location = new Point(_header.Width - _statusChip.Width - 12, (HeaderHeight - _statusChip.Height) / 2);
        };

        ApplyLayout();
    }

    /// <summary>
    /// Show or hide a coloured chip on the right of the header, e.g. "Setup required" /
    /// "Re-authorize required". Set <paramref name="text"/> to <c>null</c> to hide.
    /// </summary>
    public void SetStatusChip(string? text, Color backColor)
    {
        if (string.IsNullOrEmpty(text))
        {
            _statusChip.Visible = false;
            return;
        }
        _statusChip.Text = text;
        _statusChip.BackColor = backColor;
        _statusChip.Visible = true;
        _statusChip.Location = new Point(_header.Width - _statusChip.Width - 12, (HeaderHeight - _statusChip.Height) / 2);
    }

    private void ApplyLayout()
    {
        _chevron.Text = _expanded ? "▼" : "▶";
        _content.Visible = _expanded;
        _content.Height = _expanded ? _contentHeight : 0;
        Height = HeaderHeight + (_expanded ? _contentHeight : 0);
    }
}
