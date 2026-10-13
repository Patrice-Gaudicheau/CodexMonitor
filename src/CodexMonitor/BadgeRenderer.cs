using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using CodexMonitor.Core;

namespace CodexMonitor;

public static class BadgeRenderer
{
    public static Bitmap Render(int? value, BadgeTone tone, int size)
    {
        size = Math.Clamp(size, 16, 128);
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var background = tone switch
        {
            BadgeTone.Green => Color.FromArgb(163, 238, 204),
            BadgeTone.Orange => Color.FromArgb(255, 199, 119),
            BadgeTone.Red => Color.FromArgb(255, 150, 153),
            _ => Color.FromArgb(205, 210, 216)
        };
        var inset = size / 32f;
        var rect = new RectangleF(inset, size * .07f, size - 2 * inset, size * .86f);
        var radius = size * .2f;
        using var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
        path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
        path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        using var fill = new SolidBrush(background);
        graphics.FillPath(fill, path);
        var text = value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "–";
        using var format = new StringFormat(StringFormat.GenericTypographic)
        { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        var fontSize = size * (text.Length == 3 ? .57f : .70f);
        Font font = new("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        while (graphics.MeasureString(text, font, size, format).Width > size - 2 && fontSize > size * .35f)
        { font.Dispose(); fontSize -= .5f; font = new("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel); }
        using (font)
        using (var ink = new SolidBrush(Color.FromArgb(19, 38, 32)))
            graphics.DrawString(text, font, ink, new RectangleF(0, -.3f, size, size), format);
        return bitmap;
    }

    public static Icon Create(int? value, BadgeTone tone, int size)
    {
        using var bitmap = Render(value, tone, size);
        var handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
}
