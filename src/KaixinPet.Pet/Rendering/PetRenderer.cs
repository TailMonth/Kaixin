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

    private double _dpiScale = 1.0;
    private int _widthPx;
    private int _heightPx;
    private byte[] _pixels = Array.Empty<byte>();

    private IntPtr _screenDc;
    private IntPtr _memDc;
    private IntPtr _dib;
    private IntPtr _dibBits;
    private IntPtr _oldBitmap;

    private SpriteFrame[] _breathFrames = Array.Empty<SpriteFrame>();
    private SpriteFrame[] _blinkFrames = Array.Empty<SpriteFrame>();
    private SpriteFrame? _hourglassFrame;
    private SpriteFrame? _sandTopFrame;
    private SpriteFrame? _sandBottomFrame;
    private SpriteFrame? _shadowCatFrame;
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
        _widthPx = Math.Max(1, (int)Math.Round(SceneLayout.WindowWidthDip * dpiScale));
        _heightPx = Math.Max(1, (int)Math.Round(SceneLayout.WindowHeightDip * dpiScale));
        _pixels = new byte[_widthPx * _heightPx * 4];

        var spriteScale = dpiScale / _assets.Character.MasterScale;

        var breath = _assets.Character.Clips["idle.breath"];
        _breathFrames = breath.Frames.Select(i => _assets.IdleAtlas.GetFrame(i, spriteScale)).ToArray();

        var blink = _assets.Character.Clips["idle.blink"];
        _blinkFrames = blink.Frames.Select(i => _assets.EyesAtlas.GetFrame(i, spriteScale)).ToArray();

        _hourglassFrame = _assets.Hourglass.Get(spriteScale);
        _sandTopFrame = _assets.SandTop.Get(spriteScale);
        _sandBottomFrame = _assets.SandBottom.Get(spriteScale);
        _shadowCatFrame = _assets.ShadowCat.Get(spriteScale);
        _shadowHourglassFrame = _assets.ShadowHourglass.Get(spriteScale);

        CreateDib();
    }

    public void Render(AnimationFrameState state)
    {
        Array.Clear(_pixels);

        if (_shadowCatFrame is not null)
        {
            Draw(_shadowCatFrame, Dip(SceneLayout.ShadowCatLeftDip), Dip(SceneLayout.ShadowCatTopDip));
        }

        var catLeft = Dip(SceneLayout.CatFrameLeftDip);
        var catTop = Dip(SceneLayout.CatFrameTopDip);

        if (_breathFrames.Length > 0)
        {
            var index = Math.Clamp(state.BreathFrame, 0, _breathFrames.Length - 1);
            Draw(_breathFrames[index], catLeft, catTop);
        }

        if (state.BlinkFrame >= 0 && _blinkFrames.Length > 0)
        {
            var index = Math.Clamp(state.BlinkFrame, 0, _blinkFrames.Length - 1);
            Draw(_blinkFrames[index], catLeft, catTop);
        }

        if (_scene.TimerVisible && !_scene.IsDragging)
        {
            DrawHourglass();
        }
    }

    private void DrawHourglass()
    {
        if (_hourglassFrame is null || _sandTopFrame is null || _sandBottomFrame is null)
        {
            return;
        }

        if (_shadowHourglassFrame is not null)
        {
            Draw(_shadowHourglassFrame, Dip(SceneLayout.ShadowHourglassLeftDip), Dip(SceneLayout.ShadowHourglassTopDip));
        }

        var left = Dip(SceneLayout.HourglassLeftDip);
        var top = Dip(SceneLayout.HourglassTopDip);
        Draw(_hourglassFrame, left, top);

        var progress = _scene.Progress;
        DrawSand(_sandTopFrame, 1.0 - progress, left, top);
        DrawSand(_sandBottomFrame, progress, left, top);
    }

    private void DrawSand(SpriteFrame sand, double visibleFraction, int left, int top)
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
        Blit(sand, 0, srcY, sand.Width, visibleHeight, left, top + srcY);
    }

    private void Draw(SpriteFrame frame, int left, int top) =>
        Blit(frame, 0, 0, frame.Width, frame.Height, left, top);

    private void Blit(SpriteFrame source, int srcX, int srcY, int width, int height, int dstX, int dstY)
    {
        if (width <= 0 || height <= 0)
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
                var alpha = src[srcRow + 3];
                if (alpha == 0)
                {
                    continue;
                }

                if (alpha == 255)
                {
                    dst[dstRow] = src[srcRow];
                    dst[dstRow + 1] = src[srcRow + 1];
                    dst[dstRow + 2] = src[srcRow + 2];
                    dst[dstRow + 3] = 255;
                    continue;
                }

                var inverse = 255 - alpha;
                dst[dstRow] = (byte)(src[srcRow] + dst[dstRow] * inverse / 255);
                dst[dstRow + 1] = (byte)(src[srcRow + 1] + dst[dstRow + 1] * inverse / 255);
                dst[dstRow + 2] = (byte)(src[srcRow + 2] + dst[dstRow + 2] * inverse / 255);
                dst[dstRow + 3] = (byte)(alpha + dst[dstRow + 3] * inverse / 255);
            }
        }
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
