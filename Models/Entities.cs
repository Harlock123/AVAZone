using System;

namespace AVAZone.Models;

/// <summary>Anything on the battlefield: a position, a heading, and a size for hits and collisions.</summary>
public abstract class Thing
{
    public Vec3 Position { get; set; }
    public double Yaw { get; set; }
    public double Radius { get; protected init; }
    public bool IsAlive { get; set; } = true;
}

public enum ObstacleShape
{
    Cube,
    WidePyramid,
    TallPyramid
}

/// <summary>An indestructible block on the battlefield: it stops tanks, missiles... and shells.</summary>
public class Obstacle : Thing
{
    public ObstacleShape Shape { get; }
    public double Size { get; }

    public Wireframe Model => Shape switch
    {
        ObstacleShape.Cube => Wireframe.Cube,
        ObstacleShape.WidePyramid => Wireframe.WidePyramid,
        _ => Wireframe.TallPyramid
    };

    public Obstacle(Vec3 position, ObstacleShape shape, double size, double yaw)
    {
        Position = position;
        Shape = shape;
        Size = size;
        Yaw = yaw;
        Radius = size * 1.15;
    }

    /// <summary>How far from its middle a shell is stopped: the same for shots and for lines of fire.</summary>
    public double ShellRadius => Radius * 0.85;
}

public enum EnemyKind
{
    Tank,
    SuperTank,
    Missile
}

/// <summary>The enemy: a tank, a supertank or a guided missile. There is only ever one at a time.</summary>
public class Enemy : Thing
{
    public EnemyKind Kind { get; }
    public double FireTimer { get; set; }
    public int ShotsFired { get; set; }
    public double Altitude { get; set; }
    public double Weave { get; set; }

    /// <summary>Seconds left on a sidestep: a short drive off at an angle before turning back to fight.</summary>
    public double ManoeuvreTime { get; set; }
    public double ManoeuvreTurn { get; set; }
    public double ManoeuvreTimer { get; set; }

    /// <summary>Which way it is turning to get round an obstacle: -1 left, 1 right.</summary>
    public int BlockSide { get; set; } = 1;

    public int Points => Kind switch
    {
        EnemyKind.Tank => 1000,
        EnemyKind.SuperTank => 3000,
        _ => 2000
    };

    public Wireframe Model => Kind switch
    {
        EnemyKind.Tank => Wireframe.Tank,
        EnemyKind.SuperTank => Wireframe.SuperTank,
        _ => Wireframe.Missile
    };

    public double ModelSize => Kind == EnemyKind.Missile ? 20 : 32;

    public Enemy(EnemyKind kind, Vec3 position, double yaw)
    {
        Kind = kind;
        Position = position;
        Yaw = yaw;
        Radius = kind == EnemyKind.Missile ? 22 : 34;
    }
}

/// <summary>The flying saucer: it drifts about, never attacks, never shows on radar, and is worth a lot.</summary>
public class Saucer : Thing
{
    public const int Points = 5000;

    public double Age { get; set; }
    public double Lifetime { get; }
    public Vec3 Drift { get; set; }
    public double Spin { get; set; }

    public Saucer(Vec3 position, double lifetime)
    {
        Position = position;
        Lifetime = lifetime;
        Radius = 28;
    }
}

/// <summary>A shell from a tank's gun. It flies straight and level until it hits something or runs out.</summary>
public class Shell : Thing
{
    public Vec3 Velocity { get; }
    public double Life { get; set; }
    public bool FromPlayer { get; }

    public Shell(Vec3 position, double yaw, double speed, double life, bool fromPlayer)
    {
        Position = position;
        Yaw = yaw;
        Velocity = Field.Forward(yaw) * speed;
        Life = life;
        FromPlayer = fromPlayer;
        Radius = 4;
    }
}
