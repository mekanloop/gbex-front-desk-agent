using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Gbex.FrontDesk.Agent.Windows.Services;

// Device-independent pen processing. Only the native pad supplies these points;
// there is no mouse/touch input path. Also used by the synthetic CI checks.
internal sealed class SignatureInk(int maxX, int maxY, int screenWidth, int screenHeight)
{
    internal readonly record struct Point(ushort X, ushort Y, ushort Pressure, bool Down);
    private readonly List<Point> _points = [];
    private int _contact; // 0 = up, -1 = ink, 1..3 = a tablet button
    public IReadOnlyList<Point> Points => _points;

    public int Add(ushort x, ushort y, ushort pressure, bool down)
    {
        if (x > maxX || y > maxY) return 0;
        var sx = (int)Math.Round(x * screenWidth / (double)maxX);
        var sy = (int)Math.Round(y * screenHeight / (double)maxY);
        var button = 0;
        if (sy >= screenHeight * 6 / 7)
        {
            var third = screenWidth / 3;
            var first = screenWidth - third * 2;
            button = sx < first ? 1 : sx < first + third ? 2 : 3;
        }

        if (down)
        {
            if (_contact == 0) _contact = button > 0 ? button : -1;
            if (_contact == -1)
            {
                // Do not draw a stroke across tablet button controls.
                _points.Add(new Point(x, y, pressure, button == 0));
            }
            return 0;
        }

        var clicked = button > 0 && button == _contact ? button : 0;
        if (_contact == -1) _points.Add(new Point(x, y, pressure, false));
        _contact = 0;
        return clicked;
    }

    public void Clear()
    {
        _points.Clear();
        _contact = 0;
    }

    public bool IsMeaningful
    {
        get
        {
            var ink = _points.Where(p => p.Down).ToArray();
            // Ignore hover/release coordinates when validating actual ink.
            return ink.Length >= 8
                && ink.Max(p => p.X) - ink.Min(p => p.X) > maxX * 0.01
                && ink.Max(p => p.Y) - ink.Min(p => p.Y) > maxY * 0.01;
        }
    }

    public Bitmap Render()
    {
        if (!IsMeaningful) throw new InvalidOperationException("İmza boş veya çok kısa.");
        const int width = 1200;
        var height = Math.Max(1, (int)Math.Round(width * (maxY / (double)maxX)));
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.Black, 4F)
        {
            StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round,
        };
        Point? previous = null;
        foreach (var point in _points)
        {
            if (!point.Down) { previous = null; continue; }
            if (previous is { } prev)
                graphics.DrawLine(pen,
                    (float)prev.X * width / maxX, (float)prev.Y * height / maxY,
                    (float)point.X * width / maxX, (float)point.Y * height / maxY);
            previous = point;
        }
        return bitmap;
    }
}
