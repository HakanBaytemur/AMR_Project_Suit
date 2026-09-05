using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Xml;
using Svg;

namespace DwgTrueView.App;

/// <summary>
/// Loads ribbon icons and branding from Assets, the published output copy,
/// or embedded resources so the EXE never depends on Downloads.
/// </summary>
internal static class AppIcons
{
    private const string IconPrefix = "IntraLayout.Icons.";
    private const string BrandingPrefix = "IntraLayout.Branding.";
    private const int RibbonGlyphSize = 48;

    public static Image? Load(string fileName)
    {
        Image? image = LoadExact(fileName, IconPrefix, "Icons");
        if (image is not null)
        {
            return image;
        }

        string extension = Path.GetExtension(fileName);
        if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return LoadExact(Path.ChangeExtension(fileName, ".png"), IconPrefix, "Icons");
        }
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            return LoadExact(Path.ChangeExtension(fileName, ".svg"), IconPrefix, "Icons");
        }

        return null;
    }

    public static Image? LoadInterfaceLogo()
    {
        Image? raw = LoadExact("only_logo.png", BrandingPrefix, "Branding");
        if (raw is not Bitmap bitmap)
        {
            return raw;
        }

        try
        {
            return CropBackground(bitmap);
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    private static Image? LoadExact(string fileName, string resourcePrefix, string folder)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        foreach (string path in CandidatePaths(folder, fileName))
        {
            if (!File.Exists(path))
            {
                continue;
            }
            try
            {
                return FromBytes(File.ReadAllBytes(path), fileName);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (XmlException)
            {
            }
            catch (SvgException)
            {
            }
        }

        using Stream? stream = typeof(AppIcons).Assembly.GetManifestResourceStream(
            resourcePrefix + fileName);
        if (stream is null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        try
        {
            return FromBytes(memory.ToArray(), fileName);
        }
        catch (XmlException)
        {
            return null;
        }
        catch (SvgException)
        {
            return null;
        }
    }

    private static Image FromBytes(byte[] bytes, string fileName)
    {
        if (fileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return FromSvg(bytes);
        }

        using var stream = new MemoryStream(bytes);
        using var loaded = Image.FromStream(stream);
        return new Bitmap(loaded);
    }

    private static Image FromSvg(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        SvgDocument document = SvgDocument.Open<SvgDocument>(stream);
        document.Width = RibbonGlyphSize;
        document.Height = RibbonGlyphSize;
        document.AspectRatio = new SvgAspectRatio(SvgPreserveAspectRatio.xMidYMid);
        return document.Draw(RibbonGlyphSize, RibbonGlyphSize)
            ?? throw new InvalidOperationException("SVG rasterization produced no image.");
    }

    private static Bitmap CropBackground(Bitmap source)
    {
        using var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        Color corner = bitmap.GetPixel(0, 0);
        int minX = bitmap.Width;
        int minY = bitmap.Height;
        int maxX = 0;
        int maxY = 0;
        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            byte[] buffer = new byte[Math.Abs(stride) * bitmap.Height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            const int limit = 28 * 28;
            for (int y = 0; y < bitmap.Height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int index = row + x * 4;
                    int db = buffer[index] - corner.B;
                    int dg = buffer[index + 1] - corner.G;
                    int dr = buffer[index + 2] - corner.R;
                    if (dr * dr + dg * dg + db * db < limit)
                    {
                        continue;
                    }
                    if (x < minX)
                    {
                        minX = x;
                    }
                    if (y < minY)
                    {
                        minY = y;
                    }
                    if (x > maxX)
                    {
                        maxX = x;
                    }
                    if (y > maxY)
                    {
                        maxY = y;
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        if (maxX < minX)
        {
            return new Bitmap(bitmap);
        }

        int pad = Math.Max(4, Math.Max(maxX - minX, maxY - minY) / 24);
        minX = Math.Max(0, minX - pad);
        minY = Math.Max(0, minY - pad);
        maxX = Math.Min(bitmap.Width - 1, maxX + pad);
        maxY = Math.Min(bitmap.Height - 1, maxY + pad);
        var bounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        var cropped = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using Graphics copy = Graphics.FromImage(cropped);
        copy.InterpolationMode = InterpolationMode.HighQualityBicubic;
        copy.DrawImage(bitmap, new Rectangle(0, 0, bounds.Width, bounds.Height), bounds, GraphicsUnit.Pixel);
        return cropped;
    }

    private static IEnumerable<string> CandidatePaths(string folder, string fileName)
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Assets", folder, fileName);

        string? directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && directory is not null; depth++)
        {
            yield return Path.Combine(directory, "Assets", folder, fileName);
            directory = Directory.GetParent(directory)?.FullName;
        }
    }
}
