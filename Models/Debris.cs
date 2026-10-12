using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace AVAZone.Models;

/// <summary>
/// A destroyed tank flying apart: chunks of its own wireframe thrown up and outward, tumbling, falling
/// back and bouncing once on the ground before they fade.
/// </summary>
public class Debris
{
    private static readonly Random Random = new();
    private const double Gravity = 260;

    private sealed class Piece
    {
        public required (Vec3 A, Vec3 B)[] Lines;
        public Vec3 Position;
        public Vec3 Velocity;
        public double Spin, SpinRate, Tilt, TiltRate;
        public bool Bounced;
    }

    private readonly List<Piece> _pieces = new();

    public Color Color { get; }
    public double Age { get; private set; }
    public double Duration { get; }
    public bool IsFinished => Age >= Duration;
    public double Opacity => Math.Clamp((Duration - Age) / 0.8, 0, 1);

    /// <summary>Breaks a model up into a handful of chunks, each a few of its lines.</summary>
    public Debris(Wireframe model, Vec3 position, double yaw, double size, Color color, double duration = 2.6)
    {
        Color = color;
        Duration = duration;
        var lines = model.Lines;
        int chunk = Math.Max(3, lines.Length / 7);
        for (int start = 0; start < lines.Length; start += chunk)
        {
            int count = Math.Min(chunk, lines.Length - start);
            var piece = new (Vec3, Vec3)[count];
            var centre = Vec3.Zero;
            for (int i = 0; i < count; i++)
            {
                var (a, b) = lines[start + i];
                var wa = (a * size).RotateY(yaw);
                var wb = (b * size).RotateY(yaw);
                piece[i] = (wa, wb);
                centre += (wa + wb) * 0.5;
            }
            centre /= count;
            for (int i = 0; i < count; i++)
                piece[i] = (piece[i].Item1 - centre, piece[i].Item2 - centre);

            var outward = new Vec3(centre.X, 0, centre.Z).Normalized();
            _pieces.Add(new Piece
            {
                Lines = piece,
                Position = position + centre,
                Velocity = outward * (40 + Random.NextDouble() * 90) + new Vec3((Random.NextDouble() - 0.5) * 50, 120 + Random.NextDouble() * 170, (Random.NextDouble() - 0.5) * 50),
                SpinRate = (Random.NextDouble() - 0.5) * 12,
                TiltRate = (Random.NextDouble() - 0.5) * 10
            });
        }
    }

    public void Update(double deltaTime)
    {
        Age += deltaTime;
        foreach (var p in _pieces)
        {
            p.Velocity += new Vec3(0, -Gravity * deltaTime, 0);
            p.Position += p.Velocity * deltaTime;
            p.Spin += p.SpinRate * deltaTime;
            p.Tilt += p.TiltRate * deltaTime;
            if (p.Position.Y < 0)
            {
                // One bounce, then it lies where it lands.
                p.Position = p.Position with { Y = 0 };
                p.Velocity = p.Bounced ? Vec3.Zero : new Vec3(p.Velocity.X * 0.4, -p.Velocity.Y * 0.35, p.Velocity.Z * 0.4);
                p.SpinRate *= 0.4;
                p.TiltRate *= 0.4;
                p.Bounced = true;
            }
        }
    }

    /// <summary>Every line of every chunk, where it is now, in world space.</summary>
    public IEnumerable<(Vec3 A, Vec3 B)> GetLines()
    {
        foreach (var p in _pieces)
            foreach (var (a, b) in p.Lines)
                yield return (p.Position + a.RotateX(p.Tilt).RotateY(p.Spin), p.Position + b.RotateX(p.Tilt).RotateY(p.Spin));
    }
}
