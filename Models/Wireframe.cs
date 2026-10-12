using System;
using System.Collections.Generic;

namespace AVAZone.Models;

/// <summary>
/// A vector-display model: nothing but line segments in 3D, about a unit in size, nose along +Z and
/// sitting on the ground at Y = 0.
/// </summary>
public class Wireframe
{
    public (Vec3 A, Vec3 B)[] Lines { get; }

    private Wireframe(List<(Vec3, Vec3)> lines) => Lines = lines.ToArray();

    private sealed class Builder
    {
        private readonly List<(Vec3, Vec3)> _lines = new();

        public Builder Line(Vec3 a, Vec3 b)
        {
            _lines.Add((a, b));
            return this;
        }

        public Builder Path(bool closed, params Vec3[] points)
        {
            for (int i = 0; i < points.Length - 1; i++)
                _lines.Add((points[i], points[i + 1]));
            if (closed && points.Length > 2)
                _lines.Add((points[^1], points[0]));
            return this;
        }

        /// <summary>Two outlines joined corner to corner: a solid between a lower and an upper face.</summary>
        public Builder Prism(Vec3[] bottom, Vec3[] top)
        {
            Path(true, bottom).Path(true, top);
            for (int i = 0; i < bottom.Length; i++)
                Line(bottom[i], top[i]);
            return this;
        }

        /// <summary>A ring of points in the level plane.</summary>
        public static Vec3[] FlatRing(Vec3 c, double r, int count, double phase = 0)
        {
            var points = new Vec3[count];
            for (int i = 0; i < count; i++)
            {
                double a = phase + i * Math.PI * 2 / count;
                points[i] = new Vec3(c.X + Math.Sin(a) * r, c.Y, c.Z + Math.Cos(a) * r);
            }
            return points;
        }

        /// <summary>A ring of points round the Z axis.</summary>
        public static Vec3[] Hoop(Vec3 c, double r, int count, double phase = 0)
        {
            var points = new Vec3[count];
            for (int i = 0; i < count; i++)
            {
                double a = phase + i * Math.PI * 2 / count;
                points[i] = new Vec3(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r, c.Z);
            }
            return points;
        }

        public Wireframe Build() => new(_lines);
    }

    private static Vec3 V(double x, double y, double z) => new(x, y, z);

    /// <summary>
    /// The enemy tank: a low, angled hull on its treads, a wedge-shaped turret, a long gun, and the radar
    /// dish on a mast behind. About two units long.
    /// </summary>
    public static readonly Wireframe Tank = BuildTank(super: false);

    /// <summary>The supertank: lower, longer and sharper, with no radar mast.</summary>
    public static readonly Wireframe SuperTank = BuildTank(super: true);

    private static Wireframe BuildTank(bool super)
    {
        var b = new Builder();
        double length = super ? 1.15 : 1.0, width = 0.62;

        // Treads and hull: a lower box with sloped ends, and the deck above.
        var bottom = new[] { V(-width, 0, -length * 0.85), V(width, 0, -length * 0.85), V(width, 0, length * 0.85), V(-width, 0, length * 0.85) };
        var mid = new[] { V(-width, 0.32, -length), V(width, 0.32, -length), V(width, 0.32, length), V(-width, 0.32, length) };
        var deck = new[] { V(-width * 0.85, 0.48, -length * 0.85), V(width * 0.85, 0.48, -length * 0.85), V(width * 0.85, 0.48, length * 0.7), V(-width * 0.85, 0.48, length * 0.7) };
        b.Prism(bottom, mid);
        b.Path(true, deck);
        for (int i = 0; i < 4; i++)
            b.Line(mid[i], deck[i]);

        // Turret: a wedge, wider at the back.
        double th = super ? 0.78 : 0.88;
        var tBottom = new[] { V(-0.42, 0.48, -0.55), V(0.42, 0.48, -0.55), V(0.28, 0.48, 0.35), V(-0.28, 0.48, 0.35) };
        var tTop = new[] { V(-0.32, th, -0.45), V(0.32, th, -0.45), V(0.18, th, 0.18), V(-0.18, th, 0.18) };
        b.Prism(tBottom, tTop);

        // The gun: a square barrel out over the front.
        double gy = (th + 0.48) / 2, reach = super ? 2.0 : 1.75, g = 0.06;
        var gunBase = new[] { V(-g, gy - g, 0.3), V(g, gy - g, 0.3), V(g, gy + g, 0.3), V(-g, gy + g, 0.3) };
        var gunTip = new[] { V(-g, gy - g, reach), V(g, gy - g, reach), V(g, gy + g, reach), V(-g, gy + g, reach) };
        b.Prism(gunBase, gunTip);

        if (!super)
        {
            // The radar on its mast.
            b.Line(V(0, th, -0.3), V(0, th + 0.45, -0.3));
            b.Path(true, V(-0.22, th + 0.45, -0.3), V(0, th + 0.58, -0.25), V(0.22, th + 0.45, -0.3), V(0, th + 0.32, -0.35));
        }
        else
        {
            // Fins along the back of the hull.
            b.Line(V(-width, 0.32, -length), V(-width * 1.2, 0.6, -length * 1.1));
            b.Line(V(width, 0.32, -length), V(width * 1.2, 0.6, -length * 1.1));
        }

        return b.Build();
    }

    /// <summary>A guided missile: a long body, a pointed nose and four tail fins. About two units long.</summary>
    public static readonly Wireframe Missile = BuildMissile();

    private static Wireframe BuildMissile()
    {
        var b = new Builder();
        var back = Builder.Hoop(V(0, 0, -0.9), 0.18, 6);
        var front = Builder.Hoop(V(0, 0, 0.55), 0.18, 6);
        b.Prism(back, front);
        foreach (var p in front)
            b.Line(p, V(0, 0, 1.15));
        for (int i = 0; i < 4; i++)
        {
            double a = Math.PI / 4 + i * Math.PI / 2;
            double x = Math.Cos(a), y = Math.Sin(a);
            b.Path(false, V(x * 0.18, y * 0.18, -0.4), V(x * 0.55, y * 0.55, -0.85), V(x * 0.55, y * 0.55, -1.0), V(x * 0.18, y * 0.18, -0.9));
        }
        return b.Build();
    }

    /// <summary>The flying saucer: a wide flat disc, a dome on top and an antenna. Radius 1.</summary>
    public static readonly Wireframe Saucer = BuildSaucer();

    private static Wireframe BuildSaucer()
    {
        var b = new Builder();
        var rim = Builder.FlatRing(V(0, 0, 0), 1, 12);
        var upper = Builder.FlatRing(V(0, 0.22, 0), 0.55, 12);
        var lower = Builder.FlatRing(V(0, -0.16, 0), 0.5, 12);
        var dome = Builder.FlatRing(V(0, 0.42, 0), 0.25, 8);
        b.Path(true, rim).Path(true, upper).Path(true, lower).Path(true, dome);
        for (int i = 0; i < 12; i++)
        {
            b.Line(rim[i], upper[i]);
            b.Line(rim[i], lower[i]);
        }
        for (int i = 0; i < 8; i++)
            b.Line(dome[i], upper[i * 12 / 8]);
        b.Line(V(0, 0.42, 0), V(0, 0.75, 0));
        return b.Build();
    }

    /// <summary>A cube obstacle: half-size 1, sitting on the ground.</summary>
    public static readonly Wireframe Cube = BuildCube();

    private static Wireframe BuildCube()
    {
        var b = new Builder();
        var bottom = new[] { V(-1, 0, -1), V(1, 0, -1), V(1, 0, 1), V(-1, 0, 1) };
        var top = new[] { V(-1, 2, -1), V(1, 2, -1), V(1, 2, 1), V(-1, 2, 1) };
        b.Prism(bottom, top);
        return b.Build();
    }

    /// <summary>A pyramid obstacle with a square base of half-size 1, and a height given.</summary>
    public static Wireframe Pyramid(double height)
    {
        var b = new Builder();
        var bottom = new[] { V(-1, 0, -1), V(1, 0, -1), V(1, 0, 1), V(-1, 0, 1) };
        b.Path(true, bottom);
        foreach (var p in bottom)
            b.Line(p, V(0, height, 0));
        return b.Build();
    }

    public static readonly Wireframe WidePyramid = Pyramid(1.3);
    public static readonly Wireframe TallPyramid = Pyramid(3.2);
}
