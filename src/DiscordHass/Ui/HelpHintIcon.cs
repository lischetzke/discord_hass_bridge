using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace DiscordHass.Ui;

/// <summary>
/// Owner-drawn 18×18 chip showing a centred bold "?". Clicking it opens
/// <see cref="HelpDialog"/> for the topic the chip was constructed with.
///
/// Implemented as a custom <see cref="Control"/> rather than a <see cref="Button"/> or
/// <see cref="Label"/> because both built-in widgets add their own internal text padding
/// — at this size that padding clips the descender of "?" so it renders without its dot.
/// Owner-drawing with <see cref="TextRenderer.DrawText"/> + <c>NoPadding</c> places the
/// glyph exactly where the bounds dictate, so the full character is always visible.
/// </summary>
internal sealed class HelpHintIcon : Control
{
    private const int LogicalSize = 18;

    private readonly string _topicId;
    private bool _hovered;

    public HelpHintIcon(string topicId)
    {
        if (string.IsNullOrEmpty(topicId)) throw new ArgumentException("topicId required", nameof(topicId));
        // Validate at construction so a typo throws immediately, not at click time.
        HelpContent.Get(topicId);
        _topicId = topicId;

        Size = new Size(LogicalSize, LogicalSize);
        TabStop = false;
        Cursor = Cursors.Hand;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        BackColor = ThemeColors.SurfaceMuted;
        ForeColor = ThemeColors.Accent;
        Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        DoubleBuffered = true;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = "Help";

        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor,
            true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        Color fill = _hovered ? ThemeColors.Accent : BackColor;
        Color glyph = _hovered ? ThemeColors.StatusForeground : ForeColor;

        using (SolidBrush bg = new(fill))
        {
            g.FillRectangle(bg, ClientRectangle);
        }
        using (Pen border = new(ThemeColors.Divider, 1))
        {
            // DrawRectangle with the full bounds would draw outside the client area on the
            // bottom/right edges. Inset by 1.
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        // True geometric center. The previous -1 nudge made the glyph sit too high; the
        // earlier "dot clipped" problem was the Button's hidden internal padding, which an
        // owner-drawn control doesn't have, so TextRenderer.VerticalCenter alone now sits
        // the bold "?" cleanly in the middle of the 18×18 chip.
        TextRenderer.DrawText(
            g, "?", Font, ClientRectangle, glyph,
            TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPadding);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        HelpDialog.ShowTopic(FindForm(), _topicId);
    }

    /// <summary>
    /// Place the chip so its vertical center aligns with <paramref name="text"/>'s vertical
    /// center, with a small horizontal gap after it. Use after both controls are added to
    /// the same parent.
    /// </summary>
    public void AlignWithLabel(Label text, int gap = 6)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        int preferredTextHeight = text.PreferredHeight > 0 ? text.PreferredHeight : text.Height;
        int yOffset = (preferredTextHeight - Height) / 2;
        Location = new Point(text.Right + gap, text.Top + yOffset);
    }
}
