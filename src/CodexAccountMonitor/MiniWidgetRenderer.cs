using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;

namespace CodexAccountMonitor;

public static class MiniWidgetRenderer
{
    public const int ChipWidth = 92, Gap = 8, Padding = 4, OverflowWidth = 32;
    public static int Width(int count) => count <= 0 ? 118 : Padding * 2 + Math.Min(3, count) * ChipWidth + Math.Max(0, Math.Min(3, count) - 1) * Gap + (count > 3 ? Gap + OverflowWidth : 0);

    public static string Label(MiniAccount account)
    {
        var name = string.IsNullOrWhiteSpace(account.ShortName) ? account.Name : account.ShortName;
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(name.Trim());
        while (enumerator.MoveNext()) elements.Add(enumerator.GetTextElement());
        if (elements.Count <= 6) return string.Concat(elements);
        // Keep trailing account numbers: research02 becomes rese02, not an ambiguous prefix.
        var tail = new string(name.Reverse().TakeWhile(char.IsDigit).Take(2).Reverse().ToArray());
        return tail.Length > 0 ? string.Concat(elements.Take(6 - tail.Length)) + tail : string.Concat(elements.Take(5)) + "…";
    }

    public static Bitmap Render(IReadOnlyList<MiniAccount> accounts, Size size, double scale, bool lightTheme)
    {
        var image = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.CompositingMode = CompositingMode.SourceOver;
        var s = (float)scale;
        var height = Math.Min(26 * s, size.Height - 4 * s);
        var top = (size.Height - height) / 2;
        using var chip = new SolidBrush(lightTheme ? Color.FromArgb(9, 20, 24, 35) : Color.FromArgb(17, 255, 255, 255));
        using var labelInk = new SolidBrush(lightTheme ? Color.FromArgb(190, 59, 62, 73) : Color.FromArgb(205, 226, 226, 235));
        using var valueInk = new SolidBrush(lightTheme ? Color.FromArgb(245, 42, 45, 55) : Color.FromArgb(250, 245, 245, 250));
        using var warning = new SolidBrush(lightTheme ? Color.FromArgb(255, 174, 110, 45) : Color.FromArgb(255, 222, 177, 107));
        using var nameFont = new Font("Segoe UI", 10.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var valueFont = new Font("Segoe UI", 11.5f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        using var staleFont = new Font("Malgun Gothic", 9.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var labelFormat = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        using var valueFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        var count = Math.Min(3, accounts.Count);
        for (var i = 0; i < Math.Max(1, count); i++)
        {
            var left = (Padding + i * (ChipWidth + Gap)) * s;
            var width = accounts.Count == 0 ? size.Width - Padding * 2 * s : ChipWidth * s;
            using var shape = RoundRectangle(new RectangleF(left, top, width, height), 8 * s);
            graphics.FillPath(chip, shape);
            if (accounts.Count == 0)
            {
                graphics.DrawString("+ Codex 계정", nameFont, labelInk, new RectangleF(left + 8 * s, top, width - 16 * s, height), labelFormat);
                continue;
            }
            var account = accounts[i];
            var stale = !account.Fresh && account.Remaining.HasValue;
            var value = account.Blocked ? "제한" : account.Remaining is { } remaining ? $"{(stale ? "이전 " : "")}{remaining:0}%" : "—";
            graphics.DrawString(Label(account), nameFont, labelInk, new RectangleF(left + 8 * s, top, (stale ? 25 : 38) * s, height), labelFormat);
            graphics.DrawString(value, stale ? staleFont : valueFont, account.Blocked || account.Remaining <= 10 ? warning : valueInk,
                new RectangleF(left + (stale ? 36 : 49) * s, top, (stale ? 48 : 35) * s, height), valueFormat);
        }
        if (accounts.Count > 3)
        {
            var left = (Padding + 3 * (ChipWidth + Gap)) * s;
            using var shape = RoundRectangle(new RectangleF(left, top, OverflowWidth * s, height), 8 * s);
            graphics.FillPath(chip, shape);
            using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString($"+{accounts.Count - 3}", nameFont, labelInk, new RectangleF(left, top, OverflowWidth * s, height), center);
        }
        return image;
    }

    private static GraphicsPath RoundRectangle(RectangleF box, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(box.X, box.Y, diameter, diameter, 180, 90);
        path.AddArc(box.Right - diameter, box.Y, diameter, diameter, 270, 90);
        path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(box.X, box.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); return path;
    }
}
