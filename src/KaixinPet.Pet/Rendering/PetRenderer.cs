using System.IO;
using System.Runtime.InteropServices;
using KaixinPet.Pet.Animation;
using KaixinPet.Pet.Assets;
using KaixinPet.Pet.Native;
using KaixinPet.Pet.Scene;

namespace KaixinPet.Pet.Rendering;

public sealed class PetRenderer : IDisposable
{
    private readonly AssetCatalog _assets;
    private readonly PetScene _scene;
    private readonly Dictionary<string, SpriteFrame[]> _clipFrames =
        new(StringComparer.OrdinalIgnoreCase);

    private double _dpiScale = 1.0;
    private double _spriteScale = 1.0;
    private double _propScale = 0.5;
    private int _widthPx;
    private int _heightPx;
    private byte[] _pixels = Array.Empty<byte>();

    private IntPtr _screenDc;
    private IntPtr _memDc;
    private IntPtr _dib;
    private IntPtr _dibBits;
    private IntPtr _oldBitmap;

    private SpriteFrame[] _yarnFrames = Array.Empty<SpriteFrame>();
    private SpriteFrame? _hourglassFrame;
    private SpriteFrame? _sandTopFrame;
    private SpriteFrame? _sandBottomFrame;
    private SpriteFrame? _sandTopBreakFrame;
    private SpriteFrame? _sandBottomBreakFrame;
    private SpriteFrame? _shadowCatFrame;
    private SpriteFrame? _shadowSleepFrame;
    private SpriteFrame? _shadowHourglassFrame;

    public PetRenderer(AssetCatalog assets, PetScene scene)
    {
        _assets = assets;
        _scene = scene;
        _screenDc = NativeMethods.GetDC(IntPtr.Zero);
        _memDc = NativeMethods.CreateCompatibleDC(_screenDc);
    }

    public int WidthPx => _widthPx;

    public int HeightPx => _heightPx;

    public double DpiScale => _dpiScale;

    public void SetDpi(double dpiScale)
    {
        if (_dib != IntPtr.Zero && Math.Abs(dpiScale - _dpiScale) < 0.0001)
        {
            return;
        }

        _dpiScale = dpiScale;
        _spriteScale = dpiScale / Math.Max(1, _assets.Character.MasterScale);
        _propScale = dpiScale / Math.Max(1, _assets.Props.MasterScale);
        _widthPx = Math.Max(1, (int)Math.Round(SceneLayout.WindowWidthDip * dpiScale));
        _heightPx = Math.Max(1, (int)Math.Round(SceneLayout.WindowHeightDip * dpiScale));
        _pixels = new byte[_widthPx * _heightPx * 4];

        BuildClipFrames();

        _yarnFrames = Enumerable.Range(0, _assets.Yarn.FrameCount)
            .Select(index => _assets.Yarn.GetFrame(index, _propScale))
            .ToArray();
        _hourglassFrame = _assets.Hourglass.Get(_propScale);
        _sandTopFrame = _assets.SandTop.Get(_propScale);
        _sandBottomFrame = _assets.SandBottom.Get(_propScale);
        _sandTopBreakFrame = Tint(_sandTopFrame, 155, 182, 140);
        _sandBottomBreakFrame = Tint(_sandBottomFrame, 155, 182, 140);
        _shadowCatFrame = _assets.ShadowCat.Get(_propScale);
        _shadowSleepFrame = _assets.ShadowSleep.Get(_propScale);
        _shadowHourglassFrame = _assets.ShadowHourglass.Get(_propScale);

        CreateDib();
    }

    private void BuildClipFrames()
    {
        _clipFrames.Clear();
        foreach (var (name, clip) in _assets.Character.Clips)
        {
            if (!_assets.Atlases.TryGetValue(clip.Atlas, out var atlas))
            {
                throw new InvalidDataException($"动画片段 {name} 引用了不存在的图集 {clip.Atlas}");
            }

            var frames = new SpriteFrame[clip.Frames.Length];
            for (var i = 0; i < clip.Frames.Length; i++)
            {
                frames[i] = atlas.GetFrame(clip.Frames[i], _spriteScale);
            }

            _clipFrames[name] = frames;
        }
    }

    public void Render(AnimationFrameSet state)
    {
        Array.Clear(_pixels);

        if (!_scene.IsDragging)
        {
            if (_scene.Sleeping)
            {
                if (_shadowSleepFrame is not null)
                {
                    Draw(
                        _shadowSleepFrame,
                        Dip(SceneLayout.ShadowSleepLeftDip),
                        Dip(SceneLayout.ShadowSleepTopDip));
                }
            }
            else if (_shadowCatFrame is not null)
            {
                Draw(
                    _shadowCatFrame,
                    Dip(SceneLayout.ShadowCatLeftDip),
                    Dip(SceneLayout.ShadowCatTopDip));
            }
        }

        if (_scene.TimerVisible && !_scene.IsDragging)
        {
            DrawHourglass();
        }

        var catLeft = Dip(SceneLayout.CatFrameLeftDip + _scene.CatOffsetXDip);
        var catTop = Dip(SceneLayout.CatFrameTopDip);
        var baseFrames = GetFrames(state.BaseClip, "idle.breath");
        if (baseFrames is { Length: > 0 })
        {
            var index = Math.Clamp(state.BaseFrame, 0, baseFrames.Length - 1);
            Draw(baseFrames[index], catLeft, catTop);
        }

        foreach (var overlay in state.Overlays)
        {
            var frames = GetFrames(overlay.Clip, null);
            if (frames is not { Length: > 0 })
            {
                continue;
            }

            var index = Math.Clamp(overlay.Frame, 0, frames.Length - 1);
            Draw(frames[index], catLeft, catTop);
        }

        if (_scene.BallVisible && _yarnFrames.Length > 0)
        {
            var frame = Math.Clamp(_scene.BallFrame, 0, _yarnFrames.Length - 1);
            var half = SceneLayout.YarnFrameSizeDip / 2;
            Draw(
                _yarnFrames[frame],
                Dip(_scene.BallXDip - half),
                Dip(_scene.BallYDip - half));
        }
    }

    private SpriteFrame[]? GetFrames(string clip, string? fallbackClip)
    {
        if (_clipFrames.TryGetValue(clip, out var frames))
        {
            return frames;
        }

        if (fallbackClip is not null && _clipFrames.TryGetValue(fallbackClip, out var fallback))
        {
            return fallback;
        }

        return null;
    }

    private void DrawHourglass()
    {
        if (_hourglassFrame is null || _sandTopFrame is null || _sandBottomFrame is null)
        {
            return;
        }

        var alpha = _scene.HourglassAlpha;
        if (alpha <= 0.01)
        {
            return;
        }

        var left = Dip(SceneLayout.HourglassLeftDip);
        var top = Dip(SceneLayout.HourglassTopDip);
        var flip = _scene.HourglassFlipScale;

        if (flip < 0.99)
        {
            var centerY = (SceneLayout.HourglassTopDip + SceneLayout.HourglassHeightDip / 2) * _dpiScale;
            var destHeight = Math.Max(1, (int)Math.Round(_hourglassFrame.Height * flip));
            var destTop = (int)Math.Round(centerY - destHeight / 2.0);
            BlitScaledY(
                _hourglassFrame, 0, 0, _hourglassFrame.Width, _hourglassFrame.Height,
                left, destTop, _hourglassFrame.Width, destHeight, alpha);
            return;
        }

        if (_shadowHourglassFrame is not null)
        {
            Draw(
                _shadowHourglassFrame,
                Dip(SceneLayout.ShadowHourglassLeftDip),
                Dip(SceneLayout.ShadowHourglassTopDip),
                alpha);
        }

        Draw(_hourglassFrame, left, top, alpha);

        var progress = _scene.Progress;
        var topSand = _scene.SandBreak
            ? _sandTopBreakFrame ?? _sandTopFrame
            : _sandTopFrame;
        var bottomSand = _scene.SandBreak
            ? _sandBottomBreakFrame ?? _sandBottomFrame
            : _sandBottomFrame;

        DrawSand(topSand, 1.0 - progress, left, top, alpha);
        DrawSand(bottomSand, progress, left, top, alpha);
    }

    private void DrawSand(SpriteFrame sand, double visibleFraction, int left, int top, double alpha = 1.0)
    {
        if (visibleFraction <= 0.001)
        {
            return;
        }

        var visibleHeight = (int)Math.Round(sand.Height * Math.Clamp(visibleFraction, 0, 1));
        if (visibleHeight <= 0)
        {
            return;
        }

        var srcY = sand.Height - visibleHeight;
        Blit(sand, 0, srcY, sand.Width, visibleHeight, left, top + srcY, alpha);
    }

    private void Draw(SpriteFrame frame, int left, int top, double alpha = 1.0) =>
        Blit(frame, 0, 0, frame.Width, frame.Height, left, top, alpha);

    private void Blit(
        SpriteFrame source, int srcX, int srcY, int width, int height, int dstX, int dstY,
        double globalAlpha = 1.0)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var global = (int)Math.Round(Math.Clamp(globalAlpha, 0, 1) * 255);
        if (global <= 0)
        {
            return;
        }

        var sx0 = srcX;
        var sy0 = srcY;
        var sx1 = srcX + width;
        var sy1 = srcY + height;

        if (dstX < 0)
        {
            sx0 -= dstX;
            dstX = 0;
        }

        if (dstY < 0)
        {
            sy0 -= dstY;
            dstY = 0;
        }

        if (dstX + (sx1 - sx0) > _widthPx)
        {
            sx1 = sx0 + (_widthPx - dstX);
        }

        if (dstY + (sy1 - sy0) > _heightPx)
        {
            sy1 = sy0 + (_heightPx - dstY);
        }

        if (sx0 >= sx1 || sy0 >= sy1)
        {
            return;
        }

        var src = source.Pixels;
        var srcStride = source.Width;
        var dst = _pixels;

        for (var y = sy0; y < sy1; y++)
        {
            var srcRow = (y * srcStride + sx0) * 4;
            var dstRow = ((dstY + (y - sy0)) * _widthPx + dstX) * 4;

            for (var x = sx0; x < sx1; x++, srcRow += 4, dstRow += 4)
            {
                var sourceAlpha = src[srcRow + 3];
                if (sourceAlpha == 0)
                {
                    continue;
                }

                var alpha = sourceAlpha * global / 255;
                if (alpha <= 0)
                {
                    continue;
                }

                var srcB = src[srcRow] * global / 255;
                var srcG = src[srcRow + 1] * global / 255;
                var srcR = src[srcRow + 2] * global / 255;

                if (alpha >= 255)
                {
                    dst[dstRow] = (byte)srcB;
                    dst[dstRow + 1] = (byte)srcG;
                    dst[dstRow + 2] = (byte)srcR;
                    dst[dstRow + 3] = 255;
                    continue;
                }

                var inverse = 255 - alpha;
                dst[dstRow] = (byte)(srcB + dst[dstRow] * inverse / 255);
                dst[dstRow + 1] = (byte)(srcG + dst[dstRow + 1] * inverse / 255);
                dst[dstRow + 2] = (byte)(srcR + dst[dstRow + 2] * inverse / 255);
                dst[dstRow + 3] = (byte)(alpha + dst[dstRow + 3] * inverse / 255);
            }
        }
    }

    private void BlitScaledY(
        SpriteFrame source, int srcX, int srcY, int srcWidth, int srcHeight,
        int dstX, int dstY, int dstWidth, int dstHeight, double globalAlpha)
    {
        if (srcWidth <= 0 || srcHeight <= 0 || dstWidth <= 0 || dstHeight <= 0)
        {
            return;
        }

        var global = (int)Math.Round(Math.Clamp(globalAlpha, 0, 1) * 255);
        if (global <= 0)
        {
            return;
        }

        var src = source.Pixels;
        var dst = _pixels;

        for (var dy = 0; dy < dstHeight; dy++)
        {
            var destY = dstY + dy;
            if (destY < 0 || destY >= _heightPx)
            {
                continue;
            }

            var sy = srcY + (int)((long)dy * srcHeight / dstHeight);
            if (sy < 0 || sy >= source.Height)
            {
                continue;
            }

            var srcRow = (sy * source.Width + srcX) * 4;
            var dstRow = (destY * _widthPx + dstX) * 4;

            for (var dx = 0; dx < dstWidth; dx++, srcRow += 4, dstRow += 4)
            {
                var destX = dstX + dx;
                if (destX < 0 || destX >= _widthPx || srcX + dx >= source.Width)
                {
                    break;
                }

                var sourceAlpha = src[srcRow + 3];
                if (sourceAlpha == 0)
                {
                    continue;
                }

                var alpha = sourceAlpha * global / 255;
                if (alpha <= 0)
                {
                    continue;
                }

                var srcB = src[srcRow] * global / 255;
                var srcG = src[srcRow + 1] * global / 255;
                var srcR = src[srcRow + 2] * global / 255;

                if (alpha >= 255)
                {
                    dst[dstRow] = (byte)srcB;
                    dst[dstRow + 1] = (byte)srcG;
                    dst[dstRow + 2] = (byte)srcR;
                    dst[dstRow + 3] = 255;
                    continue;
                }

                var inverse = 255 - alpha;
                dst[dstRow] = (byte)(srcB + dst[dstRow] * inverse / 255);
                dst[dstRow + 1] = (byte)(srcG + dst[dstRow + 1] * inverse / 255);
                dst[dstRow + 2] = (byte)(srcR + dst[dstRow + 2] * inverse / 255);
                dst[dstRow + 3] = (byte)(alpha + dst[dstRow + 3] * inverse / 255);
            }
        }
    }

    private static SpriteFrame Tint(SpriteFrame source, byte red, byte green, byte blue)
    {
        var pixels = new byte[source.Pixels.Length];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = source.Pixels[i + 3];
            if (alpha == 0)
            {
                continue;
            }

            pixels[i] = (byte)(blue * alpha / 255);
            pixels[i + 1] = (byte)(green * alpha / 255);
            pixels[i + 2] = (byte)(red * alpha / 255);
            pixels[i + 3] = alpha;
        }

        return new SpriteFrame(source.Width, source.Height, pixels);
    }

    public byte GetAlpha(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _widthPx || y >= _heightPx)
        {
            return 0;
        }

        return _pixels[(y * _widthPx + x) * 4 + 3];
    }

    public bool Commit(IntPtr hwnd, bool withBounds, int left, int top)
    {
        if (_dibBits == IntPtr.Zero)
        {
            return false;
        }

        Marshal.Copy(_pixels, 0, _dibBits, _pixels.Length);

        var blend = new NativeMethods.BLENDFUNCTION
        {
            BlendOp = NativeMethods.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = NativeMethods.AC_SRC_ALPHA
        };
        var sourcePoint = new NativeMethods.POINT(0, 0);
        var size = new NativeMethods.SIZE(_widthPx, _heightPx);

        if (withBounds)
        {
            var destination = new NativeMethods.POINT(left, top);
            return NativeMethods.UpdateLayeredWindow(
                hwnd, _screenDc, ref destination, ref size, _memDc, ref sourcePoint, 0, ref blend,
                NativeMethods.ULW_ALPHA);
        }

        // 必须每帧传入 psize：psize 为 null 时 Windows 不会重算分层窗口形状，
        // 中途出现的沙漏等新非透明区域会既不显示也不参与命中测试。
        return NativeMethods.UpdateLayeredWindow(
            hwnd, _screenDc, IntPtr.Zero, ref size, _memDc, ref sourcePoint, 0, ref blend,
            NativeMethods.ULW_ALPHA);
    }

    private int Dip(double value) => (int)Math.Round(value * _dpiScale);

    private void CreateDib()
    {
        if (_memDc == IntPtr.Zero)
        {
            return;
        }

        if (_dib != IntPtr.Zero)
        {
            if (_oldBitmap != IntPtr.Zero)
            {
                NativeMethods.SelectObject(_memDc, _oldBitmap);
            }

            NativeMethods.DeleteObject(_dib);
            _dib = IntPtr.Zero;
            _dibBits = IntPtr.Zero;
            _oldBitmap = IntPtr.Zero;
        }

        var info = new NativeMethods.BITMAPINFO
        {
            bmiHeader = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = _widthPx,
                biHeight = -_heightPx,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
                biSizeImage = (uint)(_widthPx * _heightPx * 4)
            }
        };

        _dib = NativeMethods.CreateDIBSection(_memDc, ref info, 0, out _dibBits, IntPtr.Zero, 0);
        if (_dib == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"CreateDIBSection 失败：{Marshal.GetLastWin32Error()}（{_widthPx}×{_heightPx}）");
        }

        _oldBitmap = NativeMethods.SelectObject(_memDc, _dib);
    }

    public void Dispose()
    {
        if (_dib != IntPtr.Zero)
        {
            if (_oldBitmap != IntPtr.Zero)
            {
                NativeMethods.SelectObject(_memDc, _oldBitmap);
            }

            NativeMethods.DeleteObject(_dib);
            _dib = IntPtr.Zero;
        }

        if (_memDc != IntPtr.Zero)
        {
            NativeMethods.DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }

        if (_screenDc != IntPtr.Zero)
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, _screenDc);
            _screenDc = IntPtr.Zero;
        }
    }
}
