using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using CodexMonitor.Core;

namespace CodexMonitor;

/// <summary>Lightweight native WinForms details window. All visuals are drawn locally; no images or web view.</summary>
public sealed class DetailsForm : Form
{
    private readonly Panel scroll;
    private readonly DetailsSurface surface;
    private readonly Panel actionArea;
    private readonly MintButton refresh;
    private string? lastDescription;
    private bool fittingContent;
    public event Action? RefreshRequested;

    public DetailsForm()
    {
        Text = "Quotas Codex";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 670);
        MinimumSize = new Size(440, 540);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        TopMost = true;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = DetailsTheme.Background;
        Font = new Font("Segoe UI", 10);
        actionArea = new Panel { Dock = DockStyle.Bottom, Height = 88, BackColor = DetailsTheme.Background, Padding = new Padding(24, 8, 24, 24) };
        refresh = new MintButton { Text = "Actualiser", AccessibleName = "Actualiser les quotas", Dock = DockStyle.Fill, TabIndex = 0 };
        refresh.Click += (_, _) => RefreshRequested?.Invoke();
        actionArea.Controls.Add(refresh);
        surface = new DetailsSurface { Dock = DockStyle.Top, TabStop = false, AccessibleRole = AccessibleRole.Grouping, AccessibleName = "Détails du quota Codex" };
        scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BackColor };
        scroll.Controls.Add(surface);
        Controls.Add(scroll);
        Controls.Add(actionArea);
        scroll.Resize += (_, _) => ResizeSurface();
        Shown += (_, _) =>
        {
            ResizeSurface(); refresh.Focus();
        };
        DpiChanged += (_, _) => BeginInvoke(ResizeSurface);
    }

    public void UpdateUsage(DetailsViewModel model)
    {
        if (lastDescription != model.Description)
        {
            lastDescription = model.Description;
            surface.Model = model;
            surface.AccessibleDescription = model.Description;
            ResizeSurface();
            surface.Invalidate();
        }
        refresh.Enabled = !model.Refreshing;
        refresh.Text = model.Refreshing ? "Actualisation…" : "Actualiser";
    }

    private void ResizeSurface()
        => FitContent(Screen.FromControl(this).WorkingArea);

    internal void FitContent(Rectangle workingArea)
    {
        if (fittingContent || scroll.Width <= 0 || IsDisposed) return;
        fittingContent = true;
        try
        {
            // Measure the full width first: an existing scrollbar must not make its own
            // narrower, more heavily wrapped layout persist after the window grows.
            var scale = surface.DeviceDpi / 96f;
            using var g = surface.CreateGraphics();
            var naturalHeight = (int)Math.Ceiling(surface.MeasureHeight(g, scroll.Width / scale, scale) * scale);
            var frameHeight = Height - ClientSize.Height;
            var maximumHeight = Math.Max(actionArea.Height + frameHeight + 1, workingArea.Height - 32);
            MinimumSize = new Size(MinimumSize.Width, Math.Min((int)Math.Ceiling(540 * scale), maximumHeight));
            var desiredClientHeight = Math.Max((int)Math.Ceiling(670 * scale), naturalHeight + actionArea.Height + (int)Math.Ceiling(2 * scale));
            var clientHeight = Math.Min(desiredClientHeight, maximumHeight - frameHeight);
            if (ClientSize.Height != clientHeight) ClientSize = new Size(ClientSize.Width, clientHeight);
            surface.Height = naturalHeight;
            PerformLayout();
            scroll.PerformLayout();
            // On a genuinely smaller screen, retain scrolling and measure the actual
            // narrower viewport so every line remains reachable, including the last note.
            if (naturalHeight > scroll.ClientSize.Height)
            {
                var width = Math.Max(1, scroll.ClientSize.Width / scale);
                surface.Height = (int)Math.Ceiling(surface.MeasureHeight(g, width, scale) * scale);
                scroll.PerformLayout();
            }
            if (Visible) Top = Math.Clamp(Top, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - Height));
        }
        finally { fittingContent = false; }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Windows 11's supported native corner preference; no custom chrome, hooks or shell changes.
        var rounded = 2;
        DwmSetWindowAttribute(Handle, 33, ref rounded, sizeof(int));
        var light = 0;
        DwmSetWindowAttribute(Handle, 20, ref light, sizeof(int));
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

internal static class DetailsTheme
{
    public static readonly Color Background = Color.FromArgb(244, 248, 247);
    public static readonly Color Ink = Color.FromArgb(29, 52, 46);
    public static readonly Color Muted = Color.FromArgb(101, 121, 114);
    public static readonly Color Accent = Color.FromArgb(40, 177, 144);
    public static readonly Color Mint = Color.FromArgb(180, 238, 219);
    public static readonly Color Border = Color.FromArgb(226, 235, 230);
    public static Color Tone(BadgeTone tone) => tone switch
    {
        BadgeTone.Green => Accent, BadgeTone.Orange => Color.FromArgb(192, 120, 35),
        BadgeTone.Red => Color.FromArgb(204, 84, 87), _ => Color.FromArgb(133, 148, 142)
    };
    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class DetailsSurface : Control
{
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public DetailsViewModel? Model { get; set; }
    public DetailsSurface()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = DetailsTheme.Background;
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new DetailsAccessibility(this);
    private sealed class DetailsAccessibility(DetailsSurface owner) : ControlAccessibleObject(owner)
    {
        // Keep custom-drawn cards readable to native UI Automation and screen readers.
        public override string? Help => owner.Model?.Description;
    }

    public float MeasureHeight(Graphics graphics, float width, float scale)
    {
        var saved = graphics.Save();
        graphics.ScaleTransform(scale, scale);
        var height = Draw(graphics, width, false);
        graphics.Restore(saved);
        return height;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var scale = DeviceDpi / 96f;
        e.Graphics.ScaleTransform(scale, scale);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        Draw(e.Graphics, ClientSize.Width / scale, true);
    }

    internal float Draw(Graphics g, float width, bool paint)
    {
        var m = Model;
        if (m is null) return 500;
        const float margin = 24;
        var cardWidth = width - margin * 2;
        if (paint)
        {
            DrawText(g, "Quotas Codex", 25, FontStyle.Bold, DetailsTheme.Ink, new(margin, 15, cardWidth, 38));
            DrawText(g, "Vos quotas, en un coup d’œil.", 12, FontStyle.Regular, DetailsTheme.Muted, new(margin, 56, cardWidth, 24));
            Pill(g, "CodexMonitor 1.0.0", new(margin, 89, 149, 25), Color.FromArgb(226, 243, 236), DetailsTheme.Muted);
        }
        float y = 132;
        if (paint)
        {
            Card(g, new(margin, y, cardWidth, 176));
            DrawRing(g, new(margin + 22, y + 22, 132, 132), m);
            var x = margin + 174;
            var rightWidth = cardWidth - 192;
            DrawText(g, "QUOTA SÉLECTIONNÉ", 9, FontStyle.Bold, DetailsTheme.Muted, new(x, y + 25, rightWidth, 19));
            DrawText(g, m.QuotaLabel, 22, FontStyle.Bold, DetailsTheme.Ink, new(x, y + 49, rightWidth, 40));
            DrawText(g, m.Bucket, 12, FontStyle.Regular, DetailsTheme.Muted, new(x, y + 91, rightWidth, 26));
            Pill(g, m.Status, new(x, y + 126, rightWidth, 28), m.Percentage is null ? Color.FromArgb(237, 241, 239) : Color.FromArgb(232, 246, 239), m.Tone == BadgeTone.Green ? Color.FromArgb(32, 115, 88) : DetailsTheme.Tone(m.Tone));
        }
        y += 192;
        var half = (cardWidth - 12) / 2;
        if (paint)
        {
            TimingCard(g, new(margin, y, half, 108), "Réinitialisation", m.ResetDate, m.ResetTime, true);
            TimingCard(g, new(margin + half + 12, y, half, 108), "Dernière lecture", m.ReadDate, m.ReadTime, false);
        }
        y += 124;
        if (m.Issue is not null)
        {
            var issueHeight = Measure(g, m.Issue, 11, cardWidth - 30) + 26;
            if (paint)
            {
                Card(g, new(margin, y, cardWidth, issueHeight), Color.FromArgb(239, 243, 241));
                DrawText(g, m.Issue, 11, FontStyle.Regular, DetailsTheme.Muted, new(margin + 15, y + 12, cardWidth - 30, issueHeight - 20), true);
            }
            y += issueHeight + 12;
        }
        var noteHeight = Measure(g, DetailsViewModel.Freshness, 10, cardWidth - 32) + 48;
        if (paint)
        {
            using var line = new Pen(DetailsTheme.Border);
            g.DrawLine(line, margin, y, width - margin, y);
            DrawText(g, DetailsViewModel.Cadence, 11, FontStyle.Bold, DetailsTheme.Muted, new(margin + 1, y + 15, cardWidth, 23));
            DrawText(g, DetailsViewModel.Freshness, 10, FontStyle.Regular, DetailsTheme.Muted, new(margin + 1, y + 40, cardWidth - 2, noteHeight - 35), true);
        }
        y += noteHeight + 8;
        if (m.OtherQuotas.Count > 0)
        {
            if (paint) DrawText(g, "Autres quotas", 12, FontStyle.Bold, DetailsTheme.Ink, new(margin, y, cardWidth, 27));
            y += 32;
            foreach (var row in m.OtherQuotas)
            {
                var titleHeight = Measure(g, row.Label, 11, cardWidth - 32, FontStyle.Bold);
                var rowHeight = titleHeight + 61;
                if (paint)
                {
                    Card(g, new(margin, y, cardWidth, rowHeight));
                    DrawText(g, row.Label, 11, FontStyle.Bold, DetailsTheme.Ink, new(margin + 16, y + 12, cardWidth - 32, titleHeight), true);
                    DrawText(g, $"{row.Value} · remise à zéro : {row.Reset}", 10, FontStyle.Regular, DetailsTheme.Muted, new(margin + 16, y + titleHeight + 20, cardWidth - 32, 34), true);
                }
                y += rowHeight + 10;
            }
        }
        foreach (var note in m.Notes)
        {
            var h = Measure(g, note, 10, cardWidth);
            if (paint) DrawText(g, note, 10, FontStyle.Regular, DetailsTheme.Muted, new(margin, y + 6, cardWidth, h), true);
            y += h + 13;
        }
        return y + 10;
    }

    private static void DrawRing(Graphics g, RectangleF r, DetailsViewModel m)
    {
        using var background = new SolidBrush(Color.FromArgb(244, 251, 247));
        g.FillEllipse(background, r);
        var arc = RectangleF.Inflate(r, -6, -6);
        using var track = new Pen(Color.FromArgb(225, 237, 230), 9);
        using var progress = new Pen(DetailsTheme.Tone(m.Tone), 9) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawEllipse(track, arc);
        if (m.Remaining is > 0) g.DrawArc(progress, arc, -90, (float)(m.Remaining.Value * 3.6));
        var numberSize = m.Percentage == 100 ? 29f : 33f;
        while (numberSize > 20)
        {
            using var font = new Font("Segoe UI", numberSize * 96f / 72f, FontStyle.Bold, GraphicsUnit.Pixel);
            if (g.MeasureString(m.PercentageText, font).Width <= r.Width - 10) break;
            numberSize -= 1;
        }
        DrawText(g, m.PercentageText, numberSize, FontStyle.Bold, DetailsTheme.Ink, new(r.X + 4, r.Y + 39, r.Width - 8, 43), centered: true);
        DrawText(g, m.Percentage is null ? "indisponible" : "restants", 10, FontStyle.Regular, DetailsTheme.Muted, new(r.X + 3, r.Y + 84, r.Width - 6, 21), centered: true);
    }

    private static void TimingCard(Graphics g, RectangleF r, string title, string date, string time, bool calendar)
    {
        Card(g, r);
        using var iconBackground = new SolidBrush(Color.FromArgb(231, 246, 239));
        using var shape = DetailsTheme.Rounded(new(r.X + 14, r.Y + 13, 25, 25), 7);
        g.FillPath(iconBackground, shape);
        using var pen = new Pen(DetailsTheme.Accent, 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var x = r.X + 20; var y = r.Y + 19;
        if (calendar)
        {
            g.DrawRectangle(pen, x, y + 2, 13, 11);
            g.DrawLine(pen, x, y + 5, x + 13, y + 5);
            g.DrawLine(pen, x + 3, y, x + 3, y + 3);
            g.DrawLine(pen, x + 10, y, x + 10, y + 3);
        }
        else
        {
            g.DrawEllipse(pen, x, y, 13, 13);
            g.DrawLine(pen, x + 6.5f, y + 3, x + 6.5f, y + 6.5f);
            g.DrawLine(pen, x + 6.5f, y + 6.5f, x + 9.5f, y + 8);
        }
        DrawText(g, title, 10, FontStyle.Bold, DetailsTheme.Muted, new(r.X + 47, r.Y + 17, r.Width - 59, 24));
        DrawText(g, date, 12, FontStyle.Bold, DetailsTheme.Ink, new(r.X + 14, r.Y + 48, r.Width - 28, 27));
        DrawText(g, time, 10, FontStyle.Regular, DetailsTheme.Muted, new(r.X + 14, r.Y + 78, r.Width - 28, 22));
    }

    private static void Card(Graphics g, RectangleF r, Color? fill = null)
    {
        using var shadow = DetailsTheme.Rounded(new(r.X, r.Y + 2, r.Width, r.Height), 17);
        using var shadowBrush = new SolidBrush(Color.FromArgb(12, 54, 92, 71));
        g.FillPath(shadowBrush, shadow);
        using var path = DetailsTheme.Rounded(r, 17);
        using var brush = new SolidBrush(fill ?? Color.White);
        using var border = new Pen(DetailsTheme.Border, .7f);
        g.FillPath(brush, path); g.DrawPath(border, path);
    }
    private static void Pill(Graphics g, string text, RectangleF r, Color background, Color ink)
    {
        using var path = DetailsTheme.Rounded(r, r.Height / 2);
        using var fill = new SolidBrush(background);
        g.FillPath(fill, path);
        DrawText(g, text, 9, FontStyle.Bold, ink, r, centered: true);
    }
    private static float Measure(Graphics g, string text, float size, float width, FontStyle style = FontStyle.Regular)
    {
        using var font = new Font("Segoe UI", size * 96f / 72f, style, GraphicsUnit.Pixel);
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter };
        return (float)Math.Ceiling(g.MeasureString(text, font, (int)width, format).Height);
    }
    private static void DrawText(Graphics g, string text, float size, FontStyle style, Color color, RectangleF r, bool wrap = false, bool centered = false)
    {
        using var font = new Font("Segoe UI", size * 96f / 72f, style, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = centered ? StringAlignment.Center : StringAlignment.Near,
            LineAlignment = centered ? StringAlignment.Center : StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = wrap ? 0 : StringFormatFlags.NoWrap
        };
        g.DrawString(text, font, ink, r, format);
    }
}

internal sealed class MintButton : Button
{
    private bool hovered;
    public MintButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = DetailsTheme.Background;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 12, FontStyle.Bold);
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(1, 1, Width - 3, Height - 3);
        using var path = DetailsTheme.Rounded(rect, 14 * DeviceDpi / 96f);
        var color = !Enabled ? Color.FromArgb(224, 235, 230) : hovered ? Color.FromArgb(157, 228, 205) : DetailsTheme.Mint;
        using var fill = new SolidBrush(color);
        e.Graphics.FillPath(fill, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Round(rect), Enabled ? DetailsTheme.Ink : DetailsTheme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        if (Focused && ShowFocusCues)
        {
            using var outline = new Pen(DetailsTheme.Accent, 1.5f) { DashStyle = DashStyle.Dot };
            e.Graphics.DrawPath(outline, path);
        }
    }
}
