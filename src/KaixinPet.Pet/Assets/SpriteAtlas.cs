using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KaixinPet.Pet.Assets;

public sealed record SpriteFrame(int Width, int Height, byte[] Pixels);

internal static class BitmapLoader
{
    public static BitmapSource Load(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Pbgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    public static SpriteFrame Crop(BitmapSource source, Int32Rect rect, double scale)
    {
        BitmapSource cropped = new CroppedBitmap(source, rect);
        BitmapSource scaled = cropped;

        if (Math.Abs(scale - 1.0) > 0.001)
        {
            var transform = new ScaleTransform(scale, scale);
            transform.Freeze();
            var transformed = new TransformedBitmap(cropped, transform);
            transformed.Freeze();
            scaled = transformed;
        }

        var final = new FormatConvertedBitmap(scaled, PixelFormats.Pbgra32, null, 0);
        final.Freeze();

        var width = final.PixelWidth;
        var height = final.PixelHeight;
        var pixels = new byte[width * height * 4];
        final.CopyPixels(pixels, width * 4, 0);
        return new SpriteFrame(width, height, pixels);
    }
}

public sealed class SpriteAtlas
{
    private readonly BitmapSource _image;
    private readonly int _cellWidth;
    private readonly int _cellHeight;
    private readonly Dictionary<int, SpriteFrame> _cache = new();
    private double _cachedScale = double.NaN;

    public SpriteAtlas(string path, int cellWidth, int cellHeight)
    {
        _image = BitmapLoader.Load(path);
        _cellWidth = cellWidth;
        _cellHeight = cellHeight;

        if (_image.PixelWidth % _cellWidth != 0)
        {
            throw new InvalidDataException(
                $"图集宽度 {_image.PixelWidth} 不是帧宽 {_cellWidth} 的整数倍：{path}");
        }
    }

    public int FrameCount => _image.PixelWidth / _cellWidth;

    public SpriteFrame GetFrame(int index, double scale)
    {
        if (index < 0 || index >= FrameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"帧索引 {index} 超出范围（共 {FrameCount} 帧）");
        }

        if (Math.Abs(scale - _cachedScale) > 0.0001)
        {
            _cache.Clear();
            _cachedScale = scale;
        }

        if (_cache.TryGetValue(index, out var cached))
        {
            return cached;
        }

        var frame = BitmapLoader.Crop(
            _image,
            new Int32Rect(index * _cellWidth, 0, _cellWidth, _cellHeight),
            scale);
        _cache[index] = frame;
        return frame;
    }
}

public sealed class SpriteImage
{
    private readonly BitmapSource _image;
    private SpriteFrame? _cached;
    private double _cachedScale = double.NaN;

    public SpriteImage(string path)
    {
        _image = BitmapLoader.Load(path);
    }

    public SpriteFrame Get(double scale)
    {
        if (_cached is not null && Math.Abs(scale - _cachedScale) <= 0.0001)
        {
            return _cached;
        }

        _cached = BitmapLoader.Crop(
            _image,
            new Int32Rect(0, 0, _image.PixelWidth, _image.PixelHeight),
            scale);
        _cachedScale = scale;
        return _cached;
    }
}
