using System;
using AVAZone.Models;

namespace AVAZone.Engine;

/// <summary>
/// Drives the tank in attract mode: it turns to face the enemy (or the saucer, if that's all there is),
/// closes to fighting range, fires when the gun is on target and the way is clear, swerves when a shell
/// is coming, and backs off obstacles it runs into. Good enough to put on a show; it still gets hit.
/// </summary>
public class AttractDriver
{
    private static readonly Random Random = new();
    private double _swerve;
    private double _swerveTurn;
    private double _unstick;
    private double _hesitation;
    private bool _fireToggle;

    public void Reset()
    {
        _swerve = _unstick = _hesitation = 0;
    }

    public TankControls Drive(GameEngine engine, double deltaTime)
    {
        if (!engine.PlayerAlive)
            return default;

        var camera = engine.Camera;
        _fireToggle = !_fireToggle;

        // Now and then it hesitates.
        _hesitation -= deltaTime;
        if (_hesitation < -5 && Random.NextDouble() < deltaTime * 0.2)
            _hesitation = 0.6 + Random.NextDouble() * 0.8;
        if (_hesitation > 0)
            return default;

        // Backing off something it drove into.
        if (engine.MotionBlockedTime > 1.0 && _unstick <= 0)
            _unstick = 0.9;
        if (_unstick > 0)
        {
            _unstick -= deltaTime;
            return new TankControls(-1, 0.2, false);
        }

        // A shell on its way: swerve.
        if (engine.EnemyShell is { } shell && _swerve <= 0)
        {
            var toUs = Field.Delta(shell.Position, camera.Position);
            double d = Math.Sqrt(toUs.X * toUs.X + toUs.Z * toUs.Z);
            double heading = Math.Atan2(toUs.X, toUs.Z);
            if (d < 600 && Math.Abs(Field.AngleDiff(heading, shell.Yaw)) < 0.15 && Random.NextDouble() < 0.7)
            {
                _swerve = 0.8;
                _swerveTurn = Random.Next(2) * 2 - 1;
            }
        }
        if (_swerve > 0)
        {
            _swerve -= deltaTime;
            return _swerveTurn > 0 ? new TankControls(1, 0.1, false) : new TankControls(0.1, 1, false);
        }

        Thing? target = (Thing?)engine.Enemy ?? engine.Saucer;
        if (target == null)
            return new TankControls(0.6, 0.35, false);   // cruise in a slow circle, looking

        double distance = Field.Distance(camera.Position, target.Position);
        double aim = Field.AngleDiff(Field.Bearing(camera.Position, target.Position), camera.Yaw);
        double tolerance = Math.Max(0.012, Math.Atan2(target.Radius * 0.6, distance));

        // Turn toward the target, harder the further off it is.
        double turn = Math.Clamp(aim * 3, -1, 1);
        double forward = 0;
        if (Math.Abs(aim) < 0.4)
            forward = distance > 700 ? 1 : distance < 300 ? -0.6 : 0;

        bool clear = engine.LineClear(camera.Position, target.Position);
        if (!clear && Math.Abs(aim) < 0.3)
            forward = 0.8;   // drive to get a clear shot
        bool fire = Math.Abs(aim) < tolerance && clear && engine.PlayerShell == null && distance < 1400 && _fireToggle;

        return new TankControls(Math.Clamp(forward + turn, -1, 1), Math.Clamp(forward - turn, -1, 1), fire);
    }
}
