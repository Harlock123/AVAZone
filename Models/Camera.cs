using System;
using Avalonia;

namespace AVAZone.Models;

/// <summary>
/// The view through the tank's periscope: where the tank is, which way it faces, and how points in
/// front of it land on the 1024×768 screen. The horizon sits a little below the middle, leaving the top
/// of the screen for the radar and the score, as on the cabinet.
/// </summary>
public class Camera
{
    public const double ScreenWidth = 1024;
    public const double ScreenHeight = 768;
    public const double Focal = 600;
    public const double Near = 3;
    public const double EyeHeight = 12;
    public static readonly Point ScreenCentre = new(ScreenWidth / 2, 440);

    /// <summary>The tank's place on the battlefield (Y is ignored: the eye is always at EyeHeight).</summary>
    public Vec3 Position { get; set; }

    /// <summary>Heading, in radians: 0 faces +Z, and it grows turning right.</summary>
    public double Yaw { get; set; }

    /// <summary>A point on the battlefield, seen from the periscope: X right, Y up, Z ahead.</summary>
    public Vec3 ToCamera(Vec3 world)
    {
        var d = Field.Delta(Position, world);
        double c = Math.Cos(Yaw), s = Math.Sin(Yaw);
        return new Vec3(d.X * c - d.Z * s, world.Y - EyeHeight, d.X * s + d.Z * c);
    }

    public static Point Project(Vec3 camera) =>
        new(ScreenCentre.X + camera.X / camera.Z * Focal, ScreenCentre.Y - camera.Y / camera.Z * Focal);

    public static double ProjectedSize(double size, double depth) => size / Math.Max(depth, Near) * Focal;

    /// <summary>Half the horizontal field of view, in radians.</summary>
    public static double HalfFieldOfView => Math.Atan2(ScreenWidth / 2, Focal);
}

/// <summary>
/// The battlefield: a square of ground that wraps at its edges, so it seems to go on for ever. Distances
/// and bearings are always taken the short way round.
/// </summary>
public static class Field
{
    public const double Size = 5000;

    public static Vec3 Wrap(Vec3 p)
    {
        double x = p.X % Size, z = p.Z % Size;
        if (x < 0) x += Size;
        if (z < 0) z += Size;
        return new Vec3(x, p.Y, z);
    }

    /// <summary>The shortest offset from one point to another across the ground (Y is just the difference).</summary>
    public static Vec3 Delta(Vec3 from, Vec3 to)
    {
        double dx = to.X - from.X, dz = to.Z - from.Z;
        if (dx > Size / 2) dx -= Size;
        else if (dx < -Size / 2) dx += Size;
        if (dz > Size / 2) dz -= Size;
        else if (dz < -Size / 2) dz += Size;
        return new Vec3(dx, to.Y - from.Y, dz);
    }

    /// <summary>Distance across the ground, ignoring height.</summary>
    public static double Distance(Vec3 a, Vec3 b)
    {
        var d = Delta(a, b);
        return Math.Sqrt(d.X * d.X + d.Z * d.Z);
    }

    /// <summary>The heading from one point toward another.</summary>
    public static double Bearing(Vec3 from, Vec3 to)
    {
        var d = Delta(from, to);
        return Math.Atan2(d.X, d.Z);
    }

    public static double AngleDiff(double target, double current) => Math.IEEERemainder(target - current, Math.PI * 2);

    public static Vec3 Forward(double yaw) => new(Math.Sin(yaw), 0, Math.Cos(yaw));
}
