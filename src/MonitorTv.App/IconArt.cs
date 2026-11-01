using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace MonitorTv.App;

/// <summary>
/// The app icon drawn in code (no binary asset): a monitor with a speaker inside, and swap arrows, on a blue-to-violet tile.
/// Several variants exist while the final one is being chosen; all share one 256-unit coordinate system.
/// </summary>
internal static class IconArt
{
    public enum Skin { Blue, Light, Dark }

    private static readonly Color BlueStart = Color.FromRgb(0xF2, 0x3B, 0x4E);
    private static readonly Color VioletEnd = Color.FromRgb(0x9B, 0x0F, 0x3A);

    /// <summary>Draws variant <paramref name="variant"/> into a square of <paramref name="size"/> pixels whose top-left is <paramref name="origin"/>.</summary>
    public static void Draw(DrawingContext dc, Point origin, double size, int variant, Skin skin = Skin.Blue, Color? from = null, Color? to = null, Brush? inkOverride = null)
    {
        var scale = size / 256.0;
        dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
        dc.PushTransform(new ScaleTransform(scale, scale));

        Brush tile = skin switch
        {
            Skin.Light => Brushes.White,
            Skin.Dark => new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x16)),
            _ => new LinearGradientBrush(from ?? BlueStart, to ?? VioletEnd, new Point(0, 0), new Point(1, 1)),
        };
        Brush ink = inkOverride ?? (skin == Skin.Light ? new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x16)) : Brushes.White);
        // an outline tile for the light skin so it does not vanish on a white background
        dc.DrawRoundedRectangle(tile, skin == Skin.Light ? new Pen(new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xDE)), 2) : null,
            new Rect(0, 0, 256, 256), 58, 58);

        switch (variant)
        {
            case 1: BaseRefined(dc, ink); break;
            case 2: Ring(dc, ink); break;
            case 3: BadgeVariant(dc, ink, skin); break;
            case 4: Minimal(dc, ink); break;
            case 5: Toggle(dc, ink); break;
        }

        dc.Pop();
        dc.Pop();
    }

    // ---- variants -------------------------------------------------------------------------------------------------

    /// <summary>The sketch's idea, cleaned up: one stroke weight, arrows that hug the monitor, speaker optically centred in the screen.</summary>
    private static void BaseRefined(DrawingContext dc, Brush ink)
    {
        Monitor(dc, ink, new Rect(70, 98, 116, 66), 12, 11);
        SpeakerWithWaves(dc, ink, new Point(128, 131), 1.0);
        ArcWithHead(dc, ink, new Point(128, 130), 94, 215, 325, 11);   // top, left to right
        ArcWithHead(dc, ink, new Point(128, 130), 94, 35, 145, 11);    // bottom, right to left
    }

    /// <summary>A full ring of two arrows around a smaller monitor: reads as "switch" at a glance.</summary>
    private static void Ring(DrawingContext dc, Brush ink)
    {
        Monitor(dc, ink, new Rect(84, 104, 88, 50), 10, 10);
        SpeakerWithWaves(dc, ink, new Point(128, 129), 0.72);
        ArcWithHead(dc, ink, new Point(128, 128), 92, 205, 335, 12);
        ArcWithHead(dc, ink, new Point(128, 128), 92, 25, 155, 12);
    }

    /// <summary>The monitor and speaker are the icon; the swap is a small badge that can be dropped at small sizes.</summary>
    private static void BadgeVariant(DrawingContext dc, Brush ink, Skin skin)
    {
        Monitor(dc, ink, new Rect(36, 62, 150, 98), 14, 13);
        SpeakerWithWaves(dc, ink, new Point(111, 111), 1.15);

        var badgeFill = skin == Skin.Blue ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x2B, 0x7C, 0xFF));
        var badgeInk = skin == Skin.Blue ? new SolidColorBrush(Color.FromRgb(0x4A, 0x5C, 0xFF)) : Brushes.White;
        dc.DrawEllipse(badgeFill, null, new Point(184, 184), 44, 44);
        ArcWithHead(dc, badgeInk, new Point(184, 184), 22, 200, 340, 8, head: 15);
        ArcWithHead(dc, badgeInk, new Point(184, 184), 22, 20, 160, 8, head: 15);
    }

    /// <summary>Only what survives at 16 px: a heavy monitor and a speaker.</summary>
    private static void Minimal(DrawingContext dc, Brush ink)
    {
        Monitor(dc, ink, new Rect(30, 52, 196, 136), 20, 22);
        SpeakerWithWaves(dc, ink, new Point(128, 120), 1.7, stroke: 15);
    }

    /// <summary>A toggle where the stand would be: "flip between" without arrows.</summary>
    private static void Toggle(DrawingContext dc, Brush ink)
    {
        Monitor(dc, ink, new Rect(52, 52, 152, 102), 14, 13, stand: false);
        SpeakerWithWaves(dc, ink, new Point(122, 103), 1.1);
        var pill = new Rect(88, 180, 80, 36);
        dc.DrawRoundedRectangle(ink, null, pill, 18, 18);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x5A, 0x6A, 0xFF)), null, new Point(150, 198), 13, 13);
    }

    // ---- parts -----------------------------------------------------------------------------------------------------

    private static Pen RoundPen(Brush brush, double width) =>
        new(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

    private static void Monitor(DrawingContext dc, Brush ink, Rect body, double radius, double stroke, bool stand = true)
    {
        dc.DrawRoundedRectangle(null, RoundPen(ink, stroke), body, radius, radius);
        if (!stand) return;
        var x = body.Left + body.Width / 2;
        dc.DrawLine(RoundPen(ink, stroke * 0.8), new Point(x, body.Bottom + stroke / 2), new Point(x, body.Bottom + stroke / 2 + 8));
        dc.DrawLine(RoundPen(ink, stroke * 0.8), new Point(x - 16, body.Bottom + stroke / 2 + 9), new Point(x + 16, body.Bottom + stroke / 2 + 9));
    }

    /// <summary>A speaker cone with two sound waves, centred on <paramref name="centre"/> including the waves.</summary>
    private static void SpeakerWithWaves(DrawingContext dc, Brush ink, Point centre, double scale, double stroke = 8)
    {
        // The cone sits left of the centre and the waves to its right, so the whole glyph balances on the point.
        var cone = new StreamGeometry();
        using (var ctx = cone.Open())
        {
            ctx.BeginFigure(P(-24, -9), true, true);
            ctx.LineTo(P(-12, -9), true, false);
            ctx.LineTo(P(4, -23), true, false);
            ctx.LineTo(P(4, 23), true, false);
            ctx.LineTo(P(-12, 9), true, false);
            ctx.LineTo(P(-24, 9), true, false);
        }
        cone.Freeze();
        dc.DrawGeometry(ink, RoundPen(ink, 3), cone);

        var pen = RoundPen(ink, stroke);
        dc.DrawGeometry(null, pen, ArcGeometry(P(4, 0), 14 * scale, -48, 48));
        dc.DrawGeometry(null, pen, ArcGeometry(P(4, 0), 27 * scale, -48, 48));

        Point P(double x, double y) => new(centre.X + x * scale - 3.5 * scale, centre.Y + y * scale);
    }

    private static void ArcWithHead(DrawingContext dc, Brush ink, Point centre, double radius, double from, double to, double stroke, double head = 24)
    {
        dc.DrawGeometry(null, RoundPen(ink, stroke), ArcGeometry(centre, radius, from, to));

        // arrowhead at the end of the sweep, pointing along the (clockwise) direction of travel
        var theta = to * Math.PI / 180;
        var end = new Point(centre.X + radius * Math.Cos(theta), centre.Y + radius * Math.Sin(theta));
        var direction = new Vector(-Math.Sin(theta), Math.Cos(theta));
        var normal = new Vector(-direction.Y, direction.X);
        var tip = end + direction * (head * 0.55);
        var left = end - direction * (head * 0.35) + normal * (head * 0.5);
        var right = end - direction * (head * 0.35) - normal * (head * 0.5);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(tip, true, true);
            ctx.LineTo(left, true, false);
            ctx.LineTo(right, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(ink, RoundPen(ink, stroke * 0.35), geometry);
    }

    /// <summary>A clockwise arc (y points down, so increasing angle runs clockwise on screen).</summary>
    private static Geometry ArcGeometry(Point centre, double radius, double from, double to)
    {
        Point At(double degrees) => new(centre.X + radius * Math.Cos(degrees * Math.PI / 180), centre.Y + radius * Math.Sin(degrees * Math.PI / 180));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(At(from), false, false);
            ctx.ArcTo(At(to), new Size(radius, radius), 0, Math.Abs(to - from) > 180, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>The app icon at <paramref name="size"/> px; the smallest sizes use the heavy minimal variant so they stay legible.</summary>
    public static System.Windows.Media.Imaging.BitmapSource Render(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) Draw(dc, new Point(0, 0), size, size <= 24 ? 4 : 1);
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>The icon for a title bar: large, centred in a fixed-height box (the bar's own icon slot clips what overflows).</summary>
    public static Wpf.Ui.Controls.IconElement TitleIcon() => new Wpf.Ui.Controls.ImageIcon
    {
        Source = Render(96), Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0),
    };

    /// <summary>The heavy variant at any size: for the tray, where the icon is 16-24 px and sits on a busy bar.</summary>
    public static System.Windows.Media.Imaging.BitmapSource RenderSimple(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) Draw(dc, new Point(0, 0), size, 4);
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Writes a multi-size .ico (PNG-compressed entries).</summary>
    public static void WriteIco(string path)
    {
        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        var images = sizes.Select(s =>
        {
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(Render(s)));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }).ToList();

        using var w = new BinaryWriter(File.Create(path));
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
            w.Write(images[i].Length); w.Write(offset);
            offset += images[i].Length;
        }
        foreach (var img in images) w.Write(img);
    }

    /// <summary>Development aid: variant 1 in several palettes.</summary>
    public static DrawingVisual Palettes()
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x28)), null, new Rect(0, 0, 1000, 640));
        Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        var neon = new LinearGradientBrush(C("#3EE6C3"), C("#FF7AC6"), new Point(0, 0), new Point(1, 1));
        (string Name, Color? A, Color? B, Brush? Ink, Skin Skin)[] sets =
        [
            ("1  Rojo vivo", C("#FF4A3D"), C("#B3123C"), null, Skin.Blue),
            ("2  Rojo carmesí", C("#F23B4E"), C("#9B0F3A"), null, Skin.Blue),
            ("3  Rojo cereza", C("#E8283C"), C("#7A0C2E"), null, Skin.Blue),
            ("4  Rojo fuego", C("#FF3B30"), C("#C4152B"), null, Skin.Blue),
            ("5  Rojo rubí", C("#D91E4A"), C("#6E0B47"), null, Skin.Blue),
            ("6  Rojo y granate", C("#FF5A4D"), C("#8F0D2E"), null, Skin.Blue),
        ];
        for (var i = 0; i < sets.Length; i++)
        {
            var x = 30 + (i % 3) * 320;
            var y = 20 + (i / 3) * 310;
            var set = sets[i];
            Draw(dc, new Point(x, y), 240, 1, set.Skin, set.A, set.B, set.Ink);
            dc.DrawText(new FormattedText(set.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 16, Brushes.White, 1.0), new Point(x, y + 248));
            double[] sizes = [48, 32, 16];
            var off = 160.0;
            foreach (var sz in sizes) { Draw(dc, new Point(x + off, y + 270 - sz), sz, 1, set.Skin, set.A, set.B, set.Ink); off += sz + 8; }
        }
        return visual;
    }

    // ---- contact sheet for choosing -----------------------------------------------------------------------------

    /// <summary>Development aid: all variants large, then the best candidates at the sizes the OS really shows.</summary>
    public static DrawingVisual ContactSheet()
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x28)), null, new Rect(0, 0, 1000, 760));

        void Label(string text, double x, double y)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 16, Brushes.White, 1.0);
            dc.DrawText(ft, new Point(x, y));
        }

        string[] names = ["1  Base refinada", "2  Anillo", "3  Insignia", "4  Mínimo", "5  Interruptor", "1  sobre claro / oscuro"];
        for (var i = 0; i < 5; i++)
        {
            var x = 30 + (i % 3) * 320;
            var y = 24 + (i / 3) * 290;
            Draw(dc, new Point(x, y), 240, i + 1);
            Label(names[i], x, y + 248);
        }

        // variant 1 on the other two skins
        Draw(dc, new Point(670, 314), 110, 1, Skin.Light);
        Draw(dc, new Point(800, 314), 110, 1, Skin.Dark);
        Label(names[5], 670, 432);

        // size ladder: how each reads at 64, 32 and 16 px
        Label("Tamaños reales (64 / 32 / 24 / 16)", 30, 610);
        for (var v = 1; v <= 5; v++)
        {
            var x = 30 + (v - 1) * 190;
            double[] sizes = [64, 32, 24, 16];
            var offset = 0.0;
            foreach (var s in sizes)
            {
                Draw(dc, new Point(x + offset, 640), s, v);
                offset += s + 6;
            }
            Label(v.ToString(), x, 716);
        }
        return visual;
    }
}
