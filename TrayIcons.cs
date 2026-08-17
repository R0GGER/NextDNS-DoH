using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace NextDnsDoh;

internal static class TrayIcons
{
    private static readonly Bitmap Source = LoadSource();

    public static Icon Create(bool enabled, bool showBadge = true, bool minimalist = false, bool lightTaskbar = false)
    {
        int[] sizes = [32];
        var images = new List<byte[]>(sizes.Length);
        foreach (var size in sizes)
        {
            using var bitmap = Render(size, enabled, showBadge, minimalist, lightTaskbar);
            images.Add(EncodePng(bitmap));
        }

        using var stream = BuildIco(images);
        using var temp = new Icon(stream);
        return (Icon)temp.Clone();
    }

    private static Bitmap LoadSource()
    {
        var assembly = typeof(TrayIcons).Assembly;
        using var stream = assembly.GetManifestResourceStream("nextdns.png")
            ?? throw new InvalidOperationException("Missing NextDNS icon resource.");
        return new Bitmap(stream);
    }

    private static Bitmap Render(int size, bool enabled, bool showBadge, bool minimalist, bool lightTaskbar)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        if (minimalist)
        {
            DrawMonochrome(graphics, size, enabled, lightTaskbar);
        }
        else if (enabled)
        {
            graphics.DrawImage(Source, new Rectangle(0, 0, size, size));
        }
        else
        {
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(GrayscaleMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            graphics.DrawImage(
                Source,
                new Rectangle(0, 0, size, size),
                0, 0, Source.Width, Source.Height,
                GraphicsUnit.Pixel,
                attributes);
        }

        if (showBadge)
        {
            DrawStatusBadge(graphics, size, enabled);
        }

        return bitmap;
    }

    private static void DrawMonochrome(Graphics graphics, int size, bool enabled, bool lightTaskbar)
    {
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(
            MonochromeMatrix(lightTaskbar, enabled ? 1f : 0.62f),
            ColorMatrixFlag.Default,
            ColorAdjustType.Bitmap);
        graphics.DrawImage(
            Source,
            new Rectangle(0, 0, size, size),
            0, 0, Source.Width, Source.Height,
            GraphicsUnit.Pixel,
            attributes);
    }

    private static ColorMatrix MonochromeMatrix(bool lightTaskbar, float alpha)
    {
        var tone = lightTaskbar ? 0f : 1f;
        return new(
        [
            [0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0],
            [0, 0, 0, alpha, 0],
            [tone, tone, tone, 0, 1]
        ]);
    }

    private static void DrawStatusBadge(Graphics graphics, int size, bool enabled)
    {
        var s = size / 32f;
        var badge = 13.5f * s;
        var x = size - badge - (1.2f * s);
        var y = size - badge - (1.2f * s);
        var ring = 1.55f * s;

        using (var backing = new SolidBrush(Color.FromArgb(235, 16, 16, 16)))
        {
            graphics.FillEllipse(backing, x - ring, y - ring, badge + (ring * 2), badge + (ring * 2));
        }

        using (var fill = new SolidBrush(enabled
            ? Color.FromArgb(255, 22, 163, 74)
            : Color.FromArgb(255, 64, 64, 64)))
        {
            graphics.FillEllipse(fill, x, y, badge, badge);
        }

        if (enabled)
        {
            using var check = new Pen(Color.White, Math.Max(1.75f, 2.05f * s))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            graphics.DrawLines(check,
            [
                new PointF(x + 3.1f * s, y + 7.15f * s),
                new PointF(x + 5.7f * s, y + 9.7f * s),
                new PointF(x + 10.5f * s, y + 3.9f * s)
            ]);
            return;
        }

        var dot = 8.2f * s;
        var dx = x + ((badge - dot) / 2f);
        var dy = y + ((badge - dot) / 2f);
        using var red = new SolidBrush(Color.FromArgb(255, 255, 32, 32));
        graphics.FillEllipse(red, dx, dy, dot, dot);
    }

    private static readonly ColorMatrix GrayscaleMatrix = new(
    [
        [0.299f, 0.299f, 0.299f, 0, 0],
        [0.587f, 0.587f, 0.587f, 0, 0],
        [0.114f, 0.114f, 0.114f, 0, 0],
        [0, 0, 0, 1, 0],
        [0.12f, 0.12f, 0.12f, 0, 1]
    ]);

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static MemoryStream BuildIco(IReadOnlyList<byte[]> pngImages)
    {
        var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)pngImages.Count);

        var offset = 6 + (16 * pngImages.Count);
        foreach (var png in pngImages)
        {
            using var pngStream = new MemoryStream(png);
            using var image = Image.FromStream(pngStream);
            writer.Write((byte)(image.Width >= 256 ? 0 : image.Width));
            writer.Write((byte)(image.Height >= 256 ? 0 : image.Height));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var png in pngImages)
        {
            writer.Write(png);
        }

        writer.Flush();
        stream.Position = 0;
        return stream;
    }
}
