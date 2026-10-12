using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AVAZone.Engine;
using AVAZone.Models;

namespace AVAZone.Views;

// The battlefield through the periscope: the far mountains, the volcano and the moon, the obstacles,
// the enemy, shells and wreckage, the gunsight, and cracked glass when the tank is hit.
public partial class GameCanvas
{
    private static Color Green => GameEngine.VectorGreen;

    // The skyline: points round the whole horizon, as (bearing, height in pixels above the horizon).
    private readonly List<(double Azimuth, double Height)> _skyline = BuildSkyline();
    private const double VolcanoAzimuth = 0.62;
    private const double VolcanoTop = 92;
    private const double MoonAzimuth = 2.5;

    // Lava thrown up out of the volcano, in skyline terms: bearing and height, and their speeds.
    private readonly List<(double Az, double H, double VAz, double VH, double Age)> _lava = new();
    private double _eruptionTimer;

    private static List<(double, double)> BuildSkyline()
    {
        var random = new Random(1980);
        var points = new List<(double, double)>();
        double a = 0;
        while (a < Math.PI * 2)
        {
            // Keep the volcano's stretch of sky clear; it is drawn on its own.
            if (Math.Abs(a - VolcanoAzimuth) < 0.16)
            {
                a = VolcanoAzimuth + 0.16;
                points.Add((a, 18));
                continue;
            }
            double peak = random.NextDouble() < 0.35 ? 40 + random.NextDouble() * 45 : 10 + random.NextDouble() * 22;
            points.Add((a, peak));
            a += 0.05 + random.NextDouble() * 0.12;
            points.Add((a, 6 + random.NextDouble() * 12));
            a += 0.04 + random.NextDouble() * 0.1;
        }
        return points;
    }

    private void UpdateScenery(double deltaTime)
    {
        // The volcano throws up a spray of lava every few seconds.
        _eruptionTimer -= deltaTime;
        if (_eruptionTimer <= 0)
        {
            _eruptionTimer = 1.5 + _random.NextDouble() * 2.5;
            int count = 6 + _random.Next(6);
            for (int i = 0; i < count; i++)
                _lava.Add((VolcanoAzimuth + (_random.NextDouble() - 0.5) * 0.02, VolcanoTop,
                    (_random.NextDouble() - 0.5) * 0.12, 40 + _random.NextDouble() * 70, 0));
        }
        for (int i = _lava.Count - 1; i >= 0; i--)
        {
            var (az, h, vaz, vh, age) = _lava[i];
            vh -= 60 * deltaTime;
            az += vaz * deltaTime;
            h += vh * deltaTime;
            age += deltaTime;
            if (h < 25 || age > 4) _lava.RemoveAt(i);
            else _lava[i] = (az, h, vaz, vh, age);
        }
    }

    /// <summary>Where a point on the skyline falls on screen, or null if it's out of view.</summary>
    private Point? SkyPoint(double azimuth, double height)
    {
        double diff = Field.AngleDiff(azimuth, _engine.Camera.Yaw);
        if (Math.Abs(diff) > 1.15) return null;
        return new Point(Camera.ScreenCentre.X + Math.Tan(diff) * Camera.Focal, Camera.ScreenCentre.Y - height);
    }

    private void DrawBattlefield(DrawingContext context)
    {
        DrawSky(context);
        DrawGround(context);
        DrawGunsight(context);
        if (_engine.DeathClock >= 0)
            DrawCrackedGlass(context);
    }

    private void DrawSky(DrawingContext context)
    {
        double y = Camera.ScreenCentre.Y;
        DrawGlowLine(context, new Point(0, y), new Point(ScreenWidth, y), Green, 0.55, 1.2);

        // The mountains: a jagged line all round the horizon.
        for (int i = 0; i < _skyline.Count; i++)
        {
            var (a1, h1) = _skyline[i];
            var (a2, h2) = _skyline[(i + 1) % _skyline.Count];
            if (a2 < a1) a2 += Math.PI * 2;
            var p1 = SkyPoint(a1, h1);
            var p2 = SkyPoint(a2, h2);
            if (p1 != null && p2 != null)
                DrawGlowLine(context, p1.Value, p2.Value, Green, 0.85, 1.4);
        }

        // The volcano, with its crater, and the lava it throws.
        var volcano = new[] { (-0.16, 18.0), (-0.035, VolcanoTop), (-0.012, VolcanoTop - 6), (0.012, VolcanoTop - 6), (0.035, VolcanoTop), (0.16, 18.0) }
            .Select(v => SkyPoint(VolcanoAzimuth + v.Item1, v.Item2)).ToArray();
        for (int i = 0; i < volcano.Length - 1; i++)
            if (volcano[i] != null && volcano[i + 1] != null)
                DrawGlowLine(context, volcano[i]!.Value, volcano[i + 1]!.Value, Green, 0.9, 1.5);
        foreach (var (az, h, vaz, vh, age) in _lava)
        {
            var p = SkyPoint(az, h);
            var q = SkyPoint(az - vaz * 0.05, h - vh * 0.05);
            if (p != null && q != null)
                DrawGlowLine(context, p.Value, q.Value, Green, Math.Clamp(1 - age / 4, 0, 1), 1.5);
        }

        // The crescent moon.
        if (SkyPoint(MoonAzimuth, 170) is { } moon)
        {
            var outer = new List<Point>();
            var inner = new List<Point>();
            for (int i = 0; i <= 16; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI / 16;
                outer.Add(new Point(moon.X + Math.Cos(a) * 22, moon.Y + Math.Sin(a) * 22));
                inner.Add(new Point(moon.X + 9 + Math.Cos(a) * 12, moon.Y + Math.Sin(a) * 22));
            }
            for (int i = 0; i < 16; i++)
            {
                DrawGlowLine(context, outer[i], outer[i + 1], Green, 0.9, 1.4);
                DrawGlowLine(context, inner[i], inner[i + 1], Green, 0.9, 1.4);
            }
        }
    }

    private void DrawGround(DrawingContext context)
    {
        var camera = _engine.Camera;
        var list = new List<(double Depth, Action Draw)>();
        void Add(Vec3 at, Action draw)
        {
            var c = camera.ToCamera(at);
            if (c.Z > -80 && Field.Distance(camera.Position, at) < ViewDistance) list.Add((c.Z, draw));
        }

        foreach (var o in _engine.Obstacles)
            Add(o.Position, () => DrawModel(context, o.Model, o.Position, o.Size, o.Yaw, 0, Green, 1));

        if (_engine.Enemy is { } e)
        {
            var at = e.Position + new Vec3(0, e.Altitude, 0);
            Add(at, () => DrawModel(context, e.Model, at, e.ModelSize, e.Yaw, 0, Green, 1));
        }
        if (_engine.Saucer is { } s)
            Add(s.Position, () => DrawModel(context, Wireframe.Saucer, s.Position, 28, s.Spin, 0, Green, 1));

        foreach (var shell in new[] { _engine.PlayerShell, _engine.EnemyShell })
            if (shell != null)
                Add(shell.Position, () => DrawShell(context, shell));

        foreach (var explosion in _engine.Explosions)
            Add(explosion.Position, () =>
            {
                foreach (var (a, b) in explosion.GetLines())
                    DrawWorldLine(context, a, b, explosion.Color, explosion.Opacity, 1.6);
            });
        foreach (var debris in _engine.Debris)
        {
            var first = debris.GetLines().FirstOrDefault();
            Add(first.A, () =>
            {
                foreach (var (a, b) in debris.GetLines())
                    DrawWorldLine(context, a, b, debris.Color, debris.Opacity, 1.6);
            });
        }

        foreach (var (_, draw) in list.OrderByDescending(t => t.Depth))
            draw();
    }

    private void DrawShell(DrawingContext context, Shell shell)
    {
        var c = _engine.Camera.ToCamera(shell.Position);
        if (c.Z < Camera.Near) return;
        var p = Camera.Project(c);
        double r = Math.Clamp(Camera.ProjectedSize(5, c.Z), 1.5, 10);
        // A small bright box, like the cabinet's shells.
        DrawGlowPolygon(context, new[] { new Point(p.X - r, p.Y - r * 0.6), new Point(p.X + r, p.Y - r * 0.6), new Point(p.X + r, p.Y + r * 0.6), new Point(p.X - r, p.Y + r * 0.6) },
            Green, 1, 1.5);
    }

    // The gunsight: lines either side of centre, with ticks above and below that spring out when the
    // gun is on the enemy.
    private void DrawGunsight(DrawingContext context)
    {
        if (!_engine.PlayerAlive || !_engine.IsInPlay) return;
        var c = Camera.ScreenCentre;
        bool onTarget = IsOnTarget();
        double tick = onTarget ? 46 : 20;

        DrawGlowLine(context, c + new Vector(-90, 0), c + new Vector(-24, 0), Green, 0.9, 1.6);
        DrawGlowLine(context, c + new Vector(24, 0), c + new Vector(90, 0), Green, 0.9, 1.6);
        DrawGlowLine(context, c + new Vector(-24, -16), c + new Vector(-24, 16), Green, 0.9, 1.6);
        DrawGlowLine(context, c + new Vector(24, -16), c + new Vector(24, 16), Green, 0.9, 1.6);
        DrawGlowLine(context, c + new Vector(0, -tick - 30), c + new Vector(0, -30), Green, 0.9, 1.6);
        DrawGlowLine(context, c + new Vector(0, 30), c + new Vector(0, 30 + tick), Green, 0.9, 1.6);
        if (onTarget)
        {
            DrawGlowLine(context, c + new Vector(-12, -30 - tick), c + new Vector(12, -30 - tick), Green, 0.9, 1.6);
            DrawGlowLine(context, c + new Vector(-12, 30 + tick), c + new Vector(12, 30 + tick), Green, 0.9, 1.6);
        }
    }

    private bool IsOnTarget()
    {
        var camera = _engine.Camera;
        foreach (var t in new Thing?[] { _engine.Enemy, _engine.Saucer })
        {
            if (t == null) continue;
            var c = camera.ToCamera(t.Position);
            if (c.Z < 10) continue;
            if (Math.Abs(c.X) < t.Radius * 0.9 && c.Z < 1500) return true;
        }
        return false;
    }

    // A shot through the glass: cracks running out from where it struck, branching as they go.
    private void DrawCrackedGlass(DrawingContext context)
    {
        var random = new Random(_engine.CrackSeed);
        var centre = _engine.CrackCentre;
        double grow = Math.Min(1, _engine.DeathClock * 6);
        for (int i = 0; i < 11; i++)
        {
            double angle = i * Math.PI * 2 / 11 + random.NextDouble() * 0.4;
            var p = centre;
            double length = 0, total = (200 + random.NextDouble() * 500) * grow;
            while (length < total)
            {
                double step = 25 + random.NextDouble() * 40;
                angle += (random.NextDouble() - 0.5) * 0.6;
                var q = p + new Vector(Math.Cos(angle), Math.Sin(angle)) * step;
                DrawGlowLine(context, p, q, Colors.White, 0.85, 1.5);
                if (random.NextDouble() < 0.3)
                {
                    double branch = angle + (random.NextDouble() - 0.5) * 1.6;
                    DrawGlowLine(context, q, q + new Vector(Math.Cos(branch), Math.Sin(branch)) * (20 + random.NextDouble() * 50), Colors.White, 0.6, 1.2);
                }
                p = q;
                length += step;
            }
        }
        // A ring of shattered glass round the hole.
        var ring = new Point[10];
        for (int i = 0; i < ring.Length; i++)
        {
            double a = i * Math.PI * 2 / ring.Length;
            double r = 18 + random.NextDouble() * 14;
            ring[i] = centre + new Vector(Math.Cos(a) * r, Math.Sin(a) * r);
        }
        DrawGlowPolygon(context, ring, Colors.White, 0.9, 1.5);
    }
}
