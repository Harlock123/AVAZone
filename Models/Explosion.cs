using System;
using Avalonia.Media;

namespace AVAZone.Models;

/// <summary>A burst of glowing debris flying out in every direction, then fading.</summary>
public class Explosion
{
    private static readonly Random Random = new();

    public Vec3 Position { get; set; }
    public Color Color { get; }
    public double Duration { get; }
    public double Age { get; private set; }
    public bool IsFinished => Age >= Duration;
    public double Progress => Math.Min(Age / Duration, 1.0);

    /// <summary>Set for debris that belongs to the world and flows past with it, rather than staying put.</summary>
    public bool MovesWithWorld { get; init; } = true;

    private readonly (Vec3 Direction, double Speed, double Length)[] _particles;

    public Explosion(Vec3 position, Color color, int count, double speed, double duration, double length)
    {
        Position = position;
        Color = color;
        Duration = duration;
        _particles = new (Vec3, double, double)[count];
        for (int i = 0; i < count; i++)
        {
            var dir = new Vec3(Random.NextDouble() * 2 - 1, Random.NextDouble() * 2 - 1, Random.NextDouble() * 2 - 1).Normalized();
            _particles[i] = (dir, speed * (0.35 + Random.NextDouble()), length * (0.4 + Random.NextDouble() * 0.6));
        }
    }

    public static Explosion Small(Vec3 p, Color c) => new(p, c, 12, 60, 0.5, 8);
    public static Explosion Fighter(Vec3 p, Color c) => new(p, c, 30, 120, 1.1, 18);
    public static Explosion Big(Vec3 p, Color c) => new(p, c, 50, 160, 1.6, 26);

    public void Update(double deltaTime) => Age += deltaTime;

    public double Opacity => 1 - Progress;

    public (Vec3 A, Vec3 B)[] GetLines()
    {
        var lines = new (Vec3, Vec3)[_particles.Length];
        double slow = 1 - Progress * 0.45;
        for (int i = 0; i < _particles.Length; i++)
        {
            var (dir, speed, length) = _particles[i];
            double d = speed * Age * slow;
            lines[i] = (Position + dir * d, Position + dir * (d + length * (1 - Progress)));
        }
        return lines;
    }
}
