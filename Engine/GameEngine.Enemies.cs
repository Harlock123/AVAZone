using System;
using AVAZone.Models;

namespace AVAZone.Engine;

// The enemy (one at a time, as on the cabinet) and the saucer.
public partial class GameEngine
{
    public const int MissilesFrom = 10000;
    public const int SuperTanksFrom = 25000;

    /// <summary>How the enemy gets tougher as the score climbs, from 0 to 1.</summary>
    private double Toughness => Math.Clamp(Score / 80000.0, 0, 1);

    /// <summary>What comes next: tanks at first, then missiles too, then supertanks as well.</summary>
    private EnemyKind NextKind()
    {
        double roll = Random.NextDouble();
        if (Score >= SuperTanksFrom)
            return roll < 0.4 ? EnemyKind.SuperTank : roll < 0.65 ? EnemyKind.Missile : EnemyKind.Tank;
        if (Score >= MissilesFrom)
            return roll < 0.35 ? EnemyKind.Missile : EnemyKind.Tank;
        return EnemyKind.Tank;
    }

    private void SpawnEnemy()
    {
        var kind = NextKind();
        for (int attempt = 0; attempt < 30; attempt++)
        {
            double bearing = Random.NextDouble() * Math.PI * 2;
            double distance = kind == EnemyKind.Missile ? 1700 : 1200 + Random.NextDouble() * 700;
            var p = Field.Wrap(Camera.Position + Field.Forward(bearing) * distance);
            if (ObstacleAt(p, 40) != null) continue;

            Enemy = new Enemy(kind, p, Field.Bearing(p, Camera.Position) + (Random.NextDouble() - 0.5) * 1.2)
            {
                FireTimer = 3.5 + Random.NextDouble() * 2.5,
                ManoeuvreTimer = 3 + Random.NextDouble() * 3,
                Weave = Random.NextDouble() * 10,
                Altitude = 0
            };
            if (kind == EnemyKind.Missile)
            {
                Sound.PlayMissileLaunch();
                Sound.StartMissile();
            }
            else
            {
                Sound.PlayNewEnemy();
            }
            return;
        }
    }

    private void UpdateEnemy(double deltaTime)
    {
        if (Enemy == null)
        {
            if (!PlayerAlive || State == GameState.GameOver) return;
            _enemyTimer -= deltaTime;
            if (_enemyTimer <= 0)
                SpawnEnemy();
            return;
        }

        if (Enemy.Kind == EnemyKind.Missile)
            UpdateMissile(Enemy, deltaTime);
        else
            UpdateTank(Enemy, deltaTime);
    }

    // A tank closes to fighting range, turns its gun on the player and fires when it has a clear shot.
    // Every so often it breaks off and drives off at an angle for a moment, which makes it harder to hit.
    private void UpdateTank(Enemy tank, double deltaTime)
    {
        bool super = tank.Kind == EnemyKind.SuperTank;
        double speed = (super ? 115 : 58) * (1 + 0.25 * Toughness);
        double turnRate = (super ? 1.4 : 0.7) * (1 + 0.3 * Toughness);

        var target = Camera.Position;
        double distance = Field.Distance(tank.Position, target);
        double bearing = Field.Bearing(tank.Position, target);
        double aimError = Field.AngleDiff(bearing, tank.Yaw);
        double move = 0;

        if (tank.ManoeuvreTime > 0)
        {
            tank.ManoeuvreTime -= deltaTime;
            tank.Yaw += Math.Clamp(Field.AngleDiff(tank.ManoeuvreTurn, tank.Yaw), -turnRate * deltaTime, turnRate * deltaTime);
            move = speed;
        }
        else
        {
            tank.Yaw += Math.Clamp(aimError, -turnRate * deltaTime, turnRate * deltaTime);
            if (distance > 900 && Math.Abs(aimError) < 0.6) move = speed;
            else if (distance < 260) move = -speed * 0.6;
            else if (super && Math.Abs(aimError) < 0.3) move = speed * 0.6;

            tank.ManoeuvreTimer -= deltaTime;
            if (tank.ManoeuvreTimer <= 0 && distance < 1300 && PlayerAlive)
            {
                tank.ManoeuvreTurn = bearing + (Random.Next(2) * 2 - 1) * (0.9 + Random.NextDouble() * 0.6);
                tank.ManoeuvreTime = 1.4 + Random.NextDouble() * 1.2;
                tank.ManoeuvreTimer = super ? 3 + Random.NextDouble() * 2 : 5 + Random.NextDouble() * 4;
            }
        }

        if (move != 0)
        {
            var next = Field.Wrap(tank.Position + Field.Forward(tank.Yaw) * move * deltaTime);
            if (IsBlocked(tank.Position, next, tank.Radius))
            {
                // Something in the way: swing off to one side (the same side until it's clear) and drive round it.
                if (tank.ManoeuvreTime <= 0)
                    tank.BlockSide = Random.Next(2) * 2 - 1;
                tank.ManoeuvreTurn = tank.Yaw + tank.BlockSide * 1.3;
                tank.ManoeuvreTime = 1.2;
            }
            else if (Field.Distance(next, Camera.Position) > tank.Radius + TankRadius + 4)
            {
                tank.Position = next;
            }
        }

        // Fire when lined up, in range, with a clear line and no shell already in the air.
        tank.FireTimer -= deltaTime;
        if (PlayerAlive && EnemyShell == null && tank.FireTimer <= 0 && Math.Abs(aimError) < 0.06 &&
            distance < 1500 && LineClear(tank.Position, target) && State != GameState.GameOver)
        {
            // Its aim settles with each shot: the first usually goes wide, later ones less so.
            double spread = Math.Max(0.012, 0.11 * Math.Pow(0.7, tank.ShotsFired)) * (1 - 0.5 * Toughness);
            double wobble = (Random.NextDouble() * 2 - 1) * spread;
            tank.ShotsFired++;
            EnemyShell = new Shell(tank.Position + Field.Forward(tank.Yaw) * (tank.Radius + 6) + new Vec3(0, 12, 0),
                tank.Yaw + wobble, super ? 600 : 460, 3.4, fromPlayer: false);
            tank.FireTimer = (super ? 2.0 : 3.6) * (1 - 0.35 * Toughness) + Random.NextDouble() * 1.5;
            Sound.PlayEnemyCannon();
        }
    }

    // A missile comes in fast and low, zigzagging less as it closes, and hops over anything in its way.
    private void UpdateMissile(Enemy missile, double deltaTime)
    {
        double speed = 270 + 90 * Toughness;
        missile.Weave += deltaTime;
        double distance = Field.Distance(missile.Position, Camera.Position);
        double weave = Math.Sin(missile.Weave * 3.2) * 0.7 * Math.Clamp(distance / 700, 0, 1);
        double wanted = Field.Bearing(missile.Position, Camera.Position) + weave;
        missile.Yaw += Math.Clamp(Field.AngleDiff(wanted, missile.Yaw), -2.6 * deltaTime, 2.6 * deltaTime);
        missile.Position = Field.Wrap(missile.Position + Field.Forward(missile.Yaw) * speed * deltaTime);

        // Something ahead: climb over it, then drop back down.
        bool obstacleAhead = ObstacleAt(Field.Wrap(missile.Position + Field.Forward(missile.Yaw) * 90), 30) != null ||
                             ObstacleAt(missile.Position, 20) != null;
        double targetAltitude = obstacleAhead ? 90 : 6;
        missile.Altitude += Math.Clamp(targetAltitude - missile.Altitude, -160 * deltaTime, 220 * deltaTime);

        if (PlayerAlive && distance < missile.Radius + TankRadius && missile.Altitude < 40)
        {
            Explosions.Add(Explosion.Big(missile.Position + new Vec3(0, 10, 0), VectorGreen));
            Sound.StopMissile();
            Enemy = null;
            _enemyTimer = EnemyDelay;
            DestroyPlayer(missile.Position);
        }
    }

    // ----- The saucer -----

    private void UpdateSaucer(double deltaTime)
    {
        if (Saucer == null)
        {
            if (!PlayerAlive || Score < 2000 || State == GameState.GameOver) return;
            _saucerTimer -= deltaTime;
            if (_saucerTimer <= 0)
            {
                double bearing = Random.NextDouble() * Math.PI * 2;
                var p = Field.Wrap(Camera.Position + Field.Forward(bearing) * (700 + Random.NextDouble() * 500));
                Saucer = new Saucer(p with { Y = 20 }, 14 + Random.NextDouble() * 6);
                _saucerTimer = 35 + Random.NextDouble() * 25;
                Sound.StartSaucer();
            }
            return;
        }

        var s = Saucer;
        s.Age += deltaTime;
        s.Spin += deltaTime * 2.5;
        if (s.Age >= s.Lifetime || !PlayerAlive)
        {
            Saucer = null;
            Sound.StopSaucer();
            return;
        }

        // It drifts this way and that, bobbing, never far from the player.
        if (s.Drift.Length < 1 || Random.NextDouble() < deltaTime * 0.5)
        {
            double a = Random.NextDouble() * Math.PI * 2;
            s.Drift = Field.Forward(a) * (40 + Random.NextDouble() * 50);
        }
        var pull = Field.Delta(s.Position, Camera.Position);
        double d = Math.Sqrt(pull.X * pull.X + pull.Z * pull.Z);
        var toward = d > 1200 ? new Vec3(pull.X, 0, pull.Z) / d * 60 : Vec3.Zero;
        var next = s.Position + (s.Drift + toward) * deltaTime;
        s.Position = Field.Wrap(next) with { Y = 20 + Math.Sin(s.Age * 2) * 6 };
    }
}
