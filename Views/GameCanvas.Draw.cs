using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAZone.Models;

namespace AVAZone.Views;

// Drawing primitives shared by every part of the screen.
public partial class GameCanvas
{
    private static readonly Color TextColor = Color.FromRgb(120, 200, 255);
    private static readonly Color Gold = Color.FromRgb(255, 215, 80);
    private static readonly Color AlertRed = Color.FromRgb(255, 70, 60);
    private static readonly Color LaserColor = Color.FromRgb(255, 60, 50);
    private static readonly Color CrosshairColor = Color.FromRgb(255, 235, 120);

    private static readonly Typeface HudFont = new(new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"), FontStyle.Normal, FontWeight.Bold);

    private readonly Random _random = new();

    // Pens and brushes are cached by colour, opacity and width: a frame draws a few thousand lines.
    private readonly Dictionary<(Color, byte, double), IPen> _pens = new();
    private readonly Dictionary<(Color, byte), IBrush> _brushes = new();

    private double Clock => _engine.Clock;

    private bool Blink(double perSecond) => (int)(Clock * perSecond) % 2 == 0;

    // A vector-monitor look: each line is drawn once wide and faint, then again thin and bright.
    private void DrawGlowLine(DrawingContext context, Point a, Point b, Color color, double alpha, double width)
    {
        if (alpha <= 0.01) return;
        context.DrawLine(GetPen(color, alpha * 0.22, width * 3.5), a, b);
        context.DrawLine(GetPen(color, alpha, width), a, b);
    }

    private void DrawGlowPolygon(DrawingContext context, IReadOnlyList<Point> points, Color color, double alpha, double width)
    {
        for (int i = 0; i < points.Count; i++)
            DrawGlowLine(context, points[i], points[(i + 1) % points.Count], color, alpha, width);
    }

    private void DrawFrame(DrawingContext context, Rect rect, Color color, double alpha = 0.7)
    {
        DrawGlowPolygon(context, new[] { rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft }, color, alpha, 1.5);
    }

    private IPen GetPen(Color color, double alpha, double width)
    {
        byte a = (byte)Math.Clamp(alpha * 255, 0, 255);
        // Widths are rounded so distance-scaled lines don't fill the cache with near-duplicates.
        width = Math.Round(width * 4) / 4;
        var key = (color, a, width);
        if (!_pens.TryGetValue(key, out var pen))
        {
            pen = new Pen(GetBrush(color, alpha), width, lineCap: PenLineCap.Round);
            if (_pens.Count > 4000) _pens.Clear();
            _pens[key] = pen;
        }
        return pen;
    }

    private IBrush GetBrush(Color color, double alpha)
    {
        byte a = (byte)Math.Clamp(alpha * 255, 0, 255);
        var key = (color, a);
        if (!_brushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B));
            if (_brushes.Count > 2000) _brushes.Clear();
            _brushes[key] = brush;
        }
        return brush;
    }

    private static Color Lerp(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(from.R + (to.R - from.R) * t),
            (byte)(from.G + (to.G - from.G) * t),
            (byte)(from.B + (to.B - from.B) * t));
    }

    private static FormattedText Text(string text, double size, Color color, double alpha = 1) => new(
        text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, HudFont, size,
        new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(alpha, 0, 1)), color.R, color.G, color.B)));

    private static void DrawCentredText(DrawingContext context, string text, double size, double y, Color color,
        double centreX = ScreenWidth / 2, double alpha = 1)
    {
        var formatted = Text(text, size, color, alpha);
        context.DrawText(formatted, new Point(centreX - formatted.Width / 2, y));
    }

    private static void DrawRightText(DrawingContext context, string text, double size, double right, double y, Color color)
    {
        var formatted = Text(text, size, color);
        context.DrawText(formatted, new Point(right - formatted.Width, y));
    }

    // A dark panel over the screen, for the pause, entry and table screens.
    private void DrawPanel(DrawingContext context, Rect rect)
    {
        context.FillRectangle(GetBrush(Colors.Black, 0.85), rect);
        DrawFrame(context, rect, Gold, 0.6);
    }

    // ----- 3D -----

    /// <summary>
    /// Nothing further than this is drawn. It is well inside half the battlefield's width, so nothing drawn
    /// can ever straddle the point where the field wraps round.
    /// </summary>
    private const double ViewDistance = 2300;

    private static double DistanceFade(double depth, double start = 1600, double end = ViewDistance) =>
        Math.Clamp(1 - (depth - start) / (end - start), 0, 1);

    /// <summary>Draws a line given in camera space, trimmed where it passes behind the cockpit.</summary>
    private void DrawCameraLine(DrawingContext context, Vec3 a, Vec3 b, Color color, double alpha, double width)
    {
        const double near = Camera.Near;
        if (a.Z < near && b.Z < near) return;
        if (a.Z < near || b.Z < near)
        {
            double t = (near - a.Z) / (b.Z - a.Z);
            var clipped = a + (b - a) * t;
            if (a.Z < near) a = clipped;
            else b = clipped;
        }
        DrawGlowLine(context, Camera.Project(a), Camera.Project(b), color, alpha, width);
    }

    /// <summary>Draws a line given in world space.</summary>
    private void DrawWorldLine(DrawingContext context, Vec3 a, Vec3 b, Color color, double alpha, double width)
    {
        var camera = _engine.Camera;
        DrawCameraLine(context, camera.ToCamera(a), camera.ToCamera(b), color, alpha, width);
    }

    /// <summary>
    /// Draws a model in the world, turned by yaw (about the vertical), then pitch, then roll (about its
    /// own nose), at a size, fading with distance.
    /// </summary>
    private void DrawModel(DrawingContext context, Wireframe model, Vec3 position, double size, double yaw, double roll,
        Color color, double alpha, double pitch = 0)
    {
        var camera = _engine.Camera;
        var centre = camera.ToCamera(position);
        if (centre.Z < -size * 2) return;
        alpha *= DistanceFade(centre.Z);
        if (alpha <= 0.01) return;
        double width = Math.Clamp(420 / Math.Max(centre.Z, 1), 1.0, 2.4);

        foreach (var (a, b) in model.Lines)
        {
            var wa = position + (a * size).RotateZ(roll).RotateX(pitch).RotateY(yaw);
            var wb = position + (b * size).RotateZ(roll).RotateX(pitch).RotateY(yaw);
            DrawCameraLine(context, camera.ToCamera(wa), camera.ToCamera(wb), color, alpha, width);
        }
    }

    /// <summary>
    /// Draws a model turned and tipped toward the viewer, for the title and legend: yaw spins it,
    /// pitch tips it so the top shows.
    /// </summary>
    private void DrawModelShowcase(DrawingContext context, Wireframe model, Point centre, double size, double yaw,
        double pitch, Color color, double alpha, double width)
    {
        const double cameraDistance = 5;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);

        Point Project(Vec3 v)
        {
            // Spin about the vertical axis, then look down on it from above and in front.
            double x = v.X * cy + v.Z * sy;       // to the right on screen
            double z = -v.X * sy + v.Z * cy;      // away from the camera
            double up = v.Y * cp + z * sp;
            double away = z * cp - v.Y * sp;
            double k = cameraDistance / (cameraDistance + away);
            return new Point(centre.X + x * size * k, centre.Y - up * size * k);
        }

        foreach (var (a, b) in model.Lines)
            DrawGlowLine(context, Project(a), Project(b), color, alpha, width);
    }
}
