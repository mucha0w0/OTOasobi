namespace Otoasobi;

// icon.bmp は 100×100 の 24bit BMP。元画像は比率を保って中央に置き、余白は白。
static class VoicebankImage
{
    public const int IconSize = 100;

    public static byte[] FitIcon(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
            throw new ArgumentException("画像の画素が足りません。");
        var dest = new byte[IconSize * IconSize * 4];
        for (var i = 0; i < dest.Length; i += 4)
        {
            dest[i] = 255;
            dest[i + 1] = 255;
            dest[i + 2] = 255;
            dest[i + 3] = 255;
        }

        var scale = Math.Min(IconSize / (double)width, IconSize / (double)height);
        var dw = Math.Clamp((int)Math.Round(width * scale), 1, IconSize);
        var dh = Math.Clamp((int)Math.Round(height * scale), 1, IconSize);
        var ox = (IconSize - dw) / 2;
        var oy = (IconSize - dh) / 2;
        for (var y = 0; y < dh; y++)
        {
            var sy = (y + 0.5) * height / dh - 0.5;
            for (var x = 0; x < dw; x++)
            {
                var sx = (x + 0.5) * width / dw - 0.5;
                var destIndex = ((oy + y) * IconSize + (ox + x)) * 4;
                Sample(bgra, width, height, sx, sy, dest, destIndex);
            }
        }

        return dest;
    }

    public static byte[] EncodeBmp24(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
            throw new ArgumentException("画像の画素が足りません。");
        var stride = (width * 3 + 3) & ~3;
        var pixels = stride * height;
        var file = new byte[54 + pixels];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        WriteInt(file, 2, file.Length);
        WriteInt(file, 10, 54);
        WriteInt(file, 14, 40);
        WriteInt(file, 18, width);
        WriteInt(file, 22, height);
        file[26] = 1;
        file[28] = 24;
        WriteInt(file, 34, pixels);
        for (var y = 0; y < height; y++)
        {
            var srcY = height - 1 - y;
            var dest = 54 + y * stride;
            for (var x = 0; x < width; x++)
            {
                var src = (srcY * width + x) * 4;
                file[dest++] = bgra[src];
                file[dest++] = bgra[src + 1];
                file[dest++] = bgra[src + 2];
            }
        }

        return file;
    }

    public static byte[] FlattenOnWhite(byte[] bgra)
    {
        var output = new byte[bgra.Length];
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            var alpha = bgra[i + 3] / 255.0;
            output[i] = (byte)Math.Round(bgra[i] * alpha + 255 * (1 - alpha));
            output[i + 1] = (byte)Math.Round(bgra[i + 1] * alpha + 255 * (1 - alpha));
            output[i + 2] = (byte)Math.Round(bgra[i + 2] * alpha + 255 * (1 - alpha));
            output[i + 3] = 255;
        }

        return output;
    }

    static void Sample(byte[] src, int width, int height, double x, double y, byte[] dest, int destIndex)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var x1 = Math.Min(x0 + 1, width - 1);
        var y1 = Math.Min(y0 + 1, height - 1);
        var tx = x - x0;
        var ty = y - y0;
        var c00 = Pixel(src, width, x0, y0);
        var c10 = Pixel(src, width, x1, y0);
        var c01 = Pixel(src, width, x0, y1);
        var c11 = Pixel(src, width, x1, y1);
        for (var channel = 0; channel < 4; channel++)
        {
            var top = c00[channel] * (1 - tx) + c10[channel] * tx;
            var bottom = c01[channel] * (1 - tx) + c11[channel] * tx;
            dest[destIndex + channel] = (byte)Math.Clamp(Math.Round(top * (1 - ty) + bottom * ty), 0, 255);
        }

        var alpha = dest[destIndex + 3] / 255.0;
        for (var channel = 0; channel < 3; channel++)
        {
            var blended = dest[destIndex + channel] * alpha + 255 * (1 - alpha);
            dest[destIndex + channel] = (byte)Math.Clamp(Math.Round(blended), 0, 255);
        }

        dest[destIndex + 3] = 255;
    }

    static double[] Pixel(byte[] src, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        return [src[i], src[i + 1], src[i + 2], src[i + 3]];
    }

    static void WriteInt(byte[] file, int offset, int value)
    {
        file[offset] = (byte)value;
        file[offset + 1] = (byte)(value >> 8);
        file[offset + 2] = (byte)(value >> 16);
        file[offset + 3] = (byte)(value >> 24);
    }
}
