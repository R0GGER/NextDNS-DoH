using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace NextDnsDoh;

internal static class TrayIcons
{
    private static readonly Bitmap Source = LoadSource();

    public static Icon Create(bool enabled)
    {
        int[] sizes = [32];
        var images = new List<byte[]>(sizes.Length);
        foreach (var size in sizes)
        {
            using var bitmap = Render(size, enabled);
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

    private static Bitmap Render(int size, bool enabled)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        if (enabled)
        {
            graphics.DrawImage(Source, new Rectangle(0, 0, size, size));
            return bitmap;
        }

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(GrayscaleMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        graphics.DrawImage(
            Source,
            new Rectangle(0, 0, size, size),
            0, 0, Source.Width, Source.Height,
            GraphicsUnit.Pixel,
            attributes);
        return bitmap;
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
