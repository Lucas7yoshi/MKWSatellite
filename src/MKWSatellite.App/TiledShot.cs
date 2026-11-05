using System.Numerics;
using MKWSatellite.App.Rendering;
using MKWSatellite.App.View;

namespace MKWSatellite.App;

/// <summary>
/// A top-down screenshot larger than the window: the course is framed for the full output size, then rendered one
/// window-sized tile per frame (same scale, camera centre moved) and the tiles are stitched. Lines, markers and labels
/// keep their on-screen pixel sizes; a label crossing a tile edge is cut there.
/// </summary>
internal sealed class TiledShot
{
    private readonly Vector2 _center;
    private readonly float _pixelsPerUnit;
    private int _cols, _rows, _tileW, _tileH;
    private int _index = -1; // -1: the first frame only sets the camera up

    public int Width { get; }
    public int Height { get; }
    public byte[] Image { get; }
    public int Count => _cols * _rows;
    public bool Done => _index >= Count;

    private TiledShot(int width, int height, Vector2 center, float pixelsPerUnit)
    {
        Width = width;
        Height = height;
        Image = new byte[width * height * 4];
        _center = center;
        _pixelsPerUnit = pixelsPerUnit;
    }

    public static TiledShot Start(CourseView view, TopDownCamera camera, int width, int height)
    {
        // Frame for the full output, as if the viewport were that large.
        var saved = camera.ViewportSize;
        camera.ViewportSize = new Vector2(width, height);
        view.FrameCourse(camera);
        var shot = new TiledShot(width, height, camera.Center, camera.PixelsPerUnit);
        camera.ViewportSize = saved;
        return shot;
    }

    /// <summary>Points the camera at the current tile (call after the viewport rect is set for the frame).</summary>
    public void Apply(TopDownCamera camera)
    {
        _tileW = (int)camera.ViewportSize.X;
        _tileH = (int)camera.ViewportSize.Y;
        _cols = (Width + _tileW - 1) / _tileW;
        _rows = (Height + _tileH - 1) / _tileH;
        var i = Math.Max(0, _index);
        var (tx, ty) = (i % _cols, i / _cols);
        // Pixel offset of this tile's centre from the output centre, converted to world units.
        var offset = new Vector2(tx * _tileW + _tileW / 2f - Width / 2f, ty * _tileH + _tileH / 2f - Height / 2f);
        camera.PixelsPerUnit = _pixelsPerUnit;
        camera.Center = _center + offset / _pixelsPerUnit;
    }

    /// <summary>Copies the rendered frame (top-down RGBA, viewport at the window's top-left) into the output.</summary>
    public void Capture((int Width, int Height, byte[] Rgba) frame)
    {
        if (_index < 0) { _index = 0; return; }
        var (tx, ty) = (_index % _cols, _index / _cols);
        var x0 = tx * _tileW;
        var y0 = ty * _tileH;
        var w = Math.Min(_tileW, Width - x0);
        var h = Math.Min(_tileH, Height - y0);
        for (var y = 0; y < h; y++)
            Buffer.BlockCopy(frame.Rgba, y * frame.Width * 4, Image, ((y0 + y) * Width + x0) * 4, w * 4);
        _index++;
    }
}
