using System;

namespace AVAZone.Models;

/// <summary>
/// A point or direction in 3D. X is to the right, Y is up and Z is straight ahead, away from the
/// cockpit, so anything in front of the ship has a positive Z.
/// </summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public Vec3 Normalized()
    {
        double length = Length;
        return length < 1e-9 ? Zero : this / length;
    }

    /// <summary>Turns about the vertical axis: positive turns the +Z axis toward +X.</summary>
    public Vec3 RotateY(double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return new Vec3(X * c + Z * s, Y, -X * s + Z * c);
    }

    /// <summary>Turns about the side axis: positive turns the +Z axis toward +Y.</summary>
    public Vec3 RotateX(double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return new Vec3(X, Y * c + Z * s, -Y * s + Z * c);
    }

    /// <summary>Turns about the forward axis: positive turns the +X axis toward +Y.</summary>
    public Vec3 RotateZ(double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return new Vec3(X * c - Y * s, X * s + Y * c, Z);
    }
}
