using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using AVAZone.Models;

namespace AVAZone.Engine;

public enum GameState
{
    StartScreen,
    Playing,
    Paused,
    GameOver,
    Attract,
    EnterInitials,
    HighScores
}

/// <summary>The tank's two treads, as set by the player or the demo driver.</summary>
public readonly record struct TankControls(double LeftTread, double RightTread, bool Fire);

/// <summary>
/// The battle: the player's tank, the one enemy at a time (tank, missile or supertank), the occasional
/// saucer, shells, obstacles, scoring and lives, and the title, attract and high-score cycle around it.
/// </summary>
public partial class GameEngine : IDisposable
{
    private static readonly Random Random = new();

    public const int StartingLives = 3;
    public const int MaxLives = 9;
    public static readonly int[] BonusTankScores = { 15000, 100000 };
    public const double TankRadius = 24;
    public const double DriveSpeed = 110;
    public const double TurnRate = 1.15;          // radians per second, treads full opposite
    public const double ShellSpeed = 760;
    public const double ShellLife = 1.9;
    public const double RadarRange = 2400;
    private const double DeathTime = 3.0;
    private const double EnemyDelay = 2.0;        // seconds between one enemy dying and the next appearing

    private const double GameOverInputDelay = 1.0;
    private const double AttractIdleDelay = 20.0;   // seconds on the title screen before the demo
    private const double AttractLength = 75.0;      // longest a demo runs
    private const double GameOverIdleDelay = 20.0;  // seconds on game over before the title returns
    private const double GameOverBeforeEntry = 2.5; // "GAME OVER" shows this long before initials entry
    private const double EntryInputGrace = 0.8;     // ignore input this long, so a mashed fire button doesn't enter letters
    private const double EntryTimeout = 45.0;       // idle this long in entry and the initials are taken as they are
    private const double HighScoresShowTime = 10.0; // the table stays up this long

    public static readonly Color VectorGreen = Color.FromRgb(90, 255, 110);
    public static readonly Color OverlayRed = Color.FromRgb(255, 70, 60);

    public Camera Camera { get; } = new();
    public GameState State { get; private set; }
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public int Kills { get; private set; }

    public List<Obstacle> Obstacles { get; } = new();
    public Enemy? Enemy { get; private set; }
    public Saucer? Saucer { get; private set; }
    public Shell? PlayerShell { get; private set; }
    public Shell? EnemyShell { get; private set; }
    public List<Explosion> Explosions { get; } = new();
    public List<Debris> Debris { get; } = new();

    public bool PlayerAlive { get; private set; }

    /// <summary>Seconds since the player's tank was hit, or -1. The cracked glass shows meanwhile.</summary>
    public double DeathClock { get; private set; } = -1;

    /// <summary>Where on the screen the killing shot struck the glass, for the cracks.</summary>
    public Avalonia.Point CrackCentre { get; private set; }
    public int CrackSeed { get; private set; }

    /// <summary>The radar's sweep, in radians from straight ahead.</summary>
    public double RadarSweep { get; private set; }
    public double MotionBlockedTime { get; private set; }
    public bool IsMoving { get; private set; }
    public double Clock { get; private set; }
    public double Shake { get; private set; }
    public bool IsInPlay => State is GameState.Playing or GameState.Attract;

    /// <summary>Where the enemy is, for the cabinet's warnings: in range, and if it is off to one side or behind.</summary>
    public bool IsEnemyInRange => Enemy != null && PlayerAlive && Field.Distance(Camera.Position, Enemy.Position) < RadarRange;
    public string? EnemyDirection
    {
        get
        {
            if (Enemy == null || !PlayerAlive) return null;
            double off = Field.AngleDiff(Field.Bearing(Camera.Position, Enemy.Position), Camera.Yaw);
            if (Math.Abs(off) < Camera.HalfFieldOfView) return null;
            if (Math.Abs(off) > 2.3) return "REAR";
            return off < 0 ? "LEFT" : "RIGHT";
        }
    }

    public HighScoreTable Table { get; } = HighScoreTable.Load();
    public int HighScore => Table.TopScore;
    public InitialsEntry? Entry { get; private set; }
    public int HighlightIndex { get; private set; } = -1;
    public bool IsEntryPending => _entryPending;

    public InputHandler Input { get; } = new();
    public SoundManager Sound { get; } = new();

    private bool _wasFiring;
    private double _enemyTimer;
    private double _saucerTimer;
    private double _blockedSoundTimer;
    private int _nextBonus;
    private TankControls _controls;

    private bool _wasStartPressed;
    private double _gameOverTimer;
    private bool _entryPending;
    private double _entryClock;
    private double _entryIdle;
    private double _padRepeat;
    private bool _wasPadUp, _wasPadDown, _wasPadLeft, _wasPadRight, _wasPadAccept, _wasPadDone, _wasLeftMouse;
    private double _tableTimer;
    private bool _tableLeadsToDemo;
    private double _idleTimer;
    private double _attractTimer;
    private bool _wasAnyInputLastFrame;
    private readonly AttractDriver _driver = new();

    public GameEngine()
    {
        State = GameState.StartScreen;
        BuildField();
        Camera.Position = new Vec3(Field.Size / 2, 0, Field.Size / 2);
    }

    public void Dispose()
    {
        // Closing the window mid-entry still keeps the score, under whatever initials are showing.
        if (_entryPending)
            CommitEntry();

        Sound.Dispose();
        Input.Dispose();
    }

    // ----- The battlefield -----

    /// <summary>Scatters cubes and pyramids over the field, none too close together.</summary>
    private void BuildField()
    {
        Obstacles.Clear();
        int attempts = 0;
        while (Obstacles.Count < 70 && attempts++ < 5000)
        {
            var p = new Vec3(Random.NextDouble() * Field.Size, 0, Random.NextDouble() * Field.Size);
            var shape = (ObstacleShape)Random.Next(3);
            double size = shape switch
            {
                ObstacleShape.Cube => 30 + Random.NextDouble() * 18,
                ObstacleShape.WidePyramid => 42 + Random.NextDouble() * 20,
                _ => 24 + Random.NextDouble() * 10
            };
            if (Obstacles.Any(o => Field.Distance(o.Position, p) < o.Radius + size + 180)) continue;
            if (Field.Distance(p, new Vec3(Field.Size / 2, 0, Field.Size / 2)) < 200) continue;
            Obstacles.Add(new Obstacle(p, shape, size, Random.NextDouble() * Math.PI));
        }
    }

    /// <summary>The obstacle something of a given radius would be touching at a point, if any.</summary>
    public Obstacle? ObstacleAt(Vec3 p, double radius) =>
        Obstacles.FirstOrDefault(o => Field.Distance(o.Position, p) < o.Radius + radius);

    /// <summary>
    /// Whether a move would run into an obstacle. A move that takes something further from an obstacle
    /// it is already touching is always allowed, so nothing can get wedged.
    /// </summary>
    public bool IsBlocked(Vec3 from, Vec3 to, double radius)
    {
        foreach (var o in Obstacles)
        {
            double after = Field.Distance(o.Position, to);
            if (after < o.Radius + radius && after < Field.Distance(o.Position, from))
                return true;
        }
        return false;
    }

    /// <summary>Whether a shell from one point would reach another without striking an obstacle.</summary>
    public bool LineClear(Vec3 from, Vec3 to)
    {
        var d = Field.Delta(from, to);
        double length = Math.Sqrt(d.X * d.X + d.Z * d.Z);
        if (length < 1) return true;
        foreach (var o in Obstacles)
        {
            var e = Field.Delta(from, o.Position);
            double along = (e.X * d.X + e.Z * d.Z) / length;
            if (along <= 0 || along >= length) continue;
            double across = Math.Abs(e.X * d.Z - e.Z * d.X) / length;
            if (across < o.ShellRadius) return false;
        }
        return true;
    }

    // ----- Starting, stopping and the attract cycle -----

    public void StartNewGame()
    {
        BeginRun(GameState.Playing);
        Sound.PlayNewEnemy();
    }

    /// <summary>The game plays itself, silently, until its tanks are gone or time runs out.</summary>
    public void StartAttract()
    {
        BeginRun(GameState.Attract);
        _attractTimer = AttractLength;
    }

    public void ReturnToTitle()
    {
        State = GameState.StartScreen;
        Sound.StopAllLoops();
        Sound.IsSilenced = false;
        Enemy = null;
        Saucer = null;
        PlayerShell = EnemyShell = null;
        Explosions.Clear();
        Debris.Clear();
        DeathClock = -1;
        _idleTimer = 0;
        HighlightIndex = -1;
    }

    private void BeginRun(GameState state)
    {
        State = state;
        Sound.StopAllLoops();
        Sound.IsSilenced = state == GameState.Attract;
        Score = 0;
        Kills = 0;
        Lives = StartingLives;
        _nextBonus = 0;
        PlayerAlive = true;
        DeathClock = -1;
        Enemy = null;
        Saucer = null;
        PlayerShell = EnemyShell = null;
        Explosions.Clear();
        Debris.Clear();
        _enemyTimer = 1.5;
        _saucerTimer = 30 + Random.NextDouble() * 20;
        _wasFiring = true;
        _driver.Reset();
    }

    private void ShowHighScores(bool leadsToDemo)
    {
        State = GameState.HighScores;
        _tableTimer = HighScoresShowTime;
        _tableLeadsToDemo = leadsToDemo;
        if (leadsToDemo)
            HighlightIndex = -1;
    }

    private void BeginInitials()
    {
        State = GameState.EnterInitials;
        Entry = new InitialsEntry(Table.LastInitials);
        _entryClock = 0;
        _entryIdle = 0;
        _wasPadUp = _wasPadDown = _wasPadLeft = _wasPadRight = _wasPadAccept = _wasPadDone = true;
        _wasLeftMouse = true;
    }

    private void CommitEntry()
    {
        _entryPending = false;
        string initials = Entry?.Initials ?? Table.LastInitials;
        HighlightIndex = Table.Insert(initials, Score, Kills);
        Table.Save();
        Entry = null;
    }

    private bool EntryAcceptsInput => State == GameState.EnterInitials && _entryClock >= EntryInputGrace && Entry != null;

    /// <summary>Keyboard input during initials entry, passed straight from the window.</summary>
    public void InitialsKey(Avalonia.Input.Key key)
    {
        if (!EntryAcceptsInput) return;
        var entry = Entry!;
        _entryIdle = 0;

        if (key >= Avalonia.Input.Key.A && key <= Avalonia.Input.Key.Z)
            entry.Type((char)('A' + (key - Avalonia.Input.Key.A)));
        else if (key >= Avalonia.Input.Key.D0 && key <= Avalonia.Input.Key.D9)
            entry.Type((char)('0' + (key - Avalonia.Input.Key.D0)));
        else if (key >= Avalonia.Input.Key.NumPad0 && key <= Avalonia.Input.Key.NumPad9)
            entry.Type((char)('0' + (key - Avalonia.Input.Key.NumPad0)));
        else if (key is Avalonia.Input.Key.OemPeriod or Avalonia.Input.Key.Decimal)
            entry.Type('.');
        else if (key == Avalonia.Input.Key.Up)
            entry.Cycle(1);
        else if (key == Avalonia.Input.Key.Down)
            entry.Cycle(-1);
        else if (key == Avalonia.Input.Key.Left || key == Avalonia.Input.Key.Back)
            entry.Back();
        else if (key == Avalonia.Input.Key.Right)
            entry.Move(1);
        else if (key == Avalonia.Input.Key.Space)
            entry.Advance();
        else if (key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Escape)
            entry.Finish();
    }

    // Gamepad and mouse input during initials entry: up/down dial the letter (holding repeats),
    // left/right move, a fire button accepts a letter, Start finishes. The wheel dials, a click accepts.
    private void UpdateInitials(double deltaTime)
    {
        _entryClock += deltaTime;
        _entryIdle += deltaTime;
        var entry = Entry!;

        bool up = Input.PadUp, down = Input.PadDown, left = Input.PadLeft, right = Input.PadRight;
        bool accept = Input.PadAccept, done = Input.PadDone, click = Input.IsLeftMousePressed;

        if (EntryAcceptsInput)
        {
            if (up || down || left || right || accept || done || click || Input.MouseWheelDelta != 0)
                _entryIdle = 0;

            int dial = 0;
            if ((up && !_wasPadUp) || (down && !_wasPadDown))
            {
                dial = up ? 1 : -1;
                _padRepeat = 0.4;
            }
            else if (up || down)
            {
                _padRepeat -= deltaTime;
                if (_padRepeat <= 0)
                {
                    dial = up ? 1 : -1;
                    _padRepeat = 0.12;
                }
            }
            if (Input.MouseWheelDelta != 0)
                dial = Math.Sign(Input.MouseWheelDelta);
            if (dial != 0)
                entry.Cycle(dial);

            if (left && !_wasPadLeft) entry.Back();
            if (right && !_wasPadRight) entry.Move(1);
            if ((accept && !_wasPadAccept) || (click && !_wasLeftMouse)) entry.Advance();
            if (done && !_wasPadDone) entry.Finish();
        }

        _wasPadUp = up; _wasPadDown = down; _wasPadLeft = left; _wasPadRight = right;
        _wasPadAccept = accept; _wasPadDone = done; _wasLeftMouse = click;

        if (_entryIdle >= EntryTimeout)
            entry.Finish();

        if (entry.IsDone)
        {
            CommitEntry();
            ShowHighScores(leadsToDemo: false);
        }
    }

    // ----- The frame -----

    public void Update(double deltaTime)
    {
        Clock += deltaTime;
        bool start = Input.IsFiring;
        bool startPressed = start && !_wasStartPressed;
        _wasStartPressed = start;
        bool newInput = Input.IsAnyInput && !_wasAnyInputLastFrame;
        _wasAnyInputLastFrame = Input.IsAnyInput;

        switch (State)
        {
            case GameState.Paused:
                return;

            case GameState.StartScreen:
                Camera.Yaw += deltaTime * 0.12;
                if (startPressed)
                {
                    StartNewGame();
                    return;
                }
                _idleTimer = Input.IsAnyInput ? 0 : _idleTimer + deltaTime;
                if (_idleTimer >= AttractIdleDelay)
                    ShowHighScores(leadsToDemo: true);
                return;

            case GameState.HighScores:
                Camera.Yaw += deltaTime * 0.12;
                if (startPressed)
                {
                    StartNewGame();
                    return;
                }
                if (newInput)
                {
                    ReturnToTitle();
                    return;
                }
                _tableTimer -= deltaTime;
                if (_tableTimer <= 0)
                {
                    if (_tableLeadsToDemo)
                        StartAttract();
                    else
                        ReturnToTitle();
                }
                return;

            case GameState.EnterInitials:
                UpdateEffects(deltaTime);
                UpdateInitials(deltaTime);
                return;

            case GameState.GameOver:
                UpdateEffects(deltaTime);
                if (DeathClock >= 0) DeathClock += deltaTime;
                _gameOverTimer -= deltaTime;
                if (_entryPending)
                {
                    if (_gameOverTimer <= GameOverInputDelay - GameOverBeforeEntry)
                        BeginInitials();
                    return;
                }
                if (startPressed && _gameOverTimer <= 0)
                {
                    StartNewGame();
                    return;
                }
                _idleTimer = Input.IsAnyInput ? 0 : _idleTimer + deltaTime;
                if (_idleTimer >= GameOverIdleDelay)
                    ReturnToTitle();
                return;

            case GameState.Attract:
                if (startPressed)
                {
                    StartNewGame();
                    return;
                }
                if (newInput)
                {
                    ReturnToTitle();
                    return;
                }
                _attractTimer -= deltaTime;
                if (_attractTimer <= 0)
                {
                    ReturnToTitle();
                    return;
                }
                _controls = _driver.Drive(this, deltaTime);
                break;

            default:
                _controls = new TankControls(Input.LeftTread, Input.RightTread, Input.IsFiring);
                break;
        }

        UpdateBattle(deltaTime);
    }

    public void Pause()
    {
        if (State == GameState.Playing)
        {
            State = GameState.Paused;
            Sound.StopAllLoops();
        }
    }

    public void Resume()
    {
        if (State == GameState.Paused)
        {
            State = GameState.Playing;
            _wasFiring = true;
        }
    }

    private void UpdateBattle(double deltaTime)
    {
        Shake = Math.Max(0, Shake - deltaTime);
        MotionBlockedTime = Math.Max(0, MotionBlockedTime - deltaTime);
        _blockedSoundTimer = Math.Max(0, _blockedSoundTimer - deltaTime);

        // The radar sweeps round once every two seconds, and blips as it crosses the enemy.
        double before = RadarSweep;
        RadarSweep = (RadarSweep + deltaTime * Math.PI) % (Math.PI * 2);
        if (Enemy != null && IsEnemyInRange)
        {
            double at = Field.AngleDiff(Field.Bearing(Camera.Position, Enemy.Position), Camera.Yaw);
            if (at < 0) at += Math.PI * 2;
            bool crossed = before <= RadarSweep ? at >= before && at < RadarSweep : at >= before || at < RadarSweep;
            if (crossed) Sound.PlayRadarBlip();
        }

        if (PlayerAlive)
            DriveTank(deltaTime);
        else
            UpdateDeath(deltaTime);

        UpdateEnemy(deltaTime);
        UpdateSaucer(deltaTime);
        UpdateShells(deltaTime);
        UpdateEffects(deltaTime);

        if (State == GameState.Playing && PlayerAlive)
            Sound.SetEngine(IsMoving);
    }

    private void UpdateEffects(double deltaTime)
    {
        foreach (var e in Explosions) e.Update(deltaTime);
        Explosions.RemoveAll(e => e.IsFinished);
        foreach (var d in Debris) d.Update(deltaTime);
        Debris.RemoveAll(d => d.IsFinished);
    }

    // ----- The player's tank -----

    private void DriveTank(double deltaTime)
    {
        double left = Math.Clamp(_controls.LeftTread, -1, 1), right = Math.Clamp(_controls.RightTread, -1, 1);
        double forward = (left + right) / 2 * DriveSpeed;
        double turn = (left - right) / 2 * TurnRate;
        IsMoving = Math.Abs(left) > 0.05 || Math.Abs(right) > 0.05;

        Camera.Yaw = (Camera.Yaw + turn * deltaTime) % (Math.PI * 2);
        if (Math.Abs(forward) > 0.01)
        {
            var next = Field.Wrap(Camera.Position + Field.Forward(Camera.Yaw) * forward * deltaTime);
            bool blocked = IsBlocked(Camera.Position, next, TankRadius) ||
                           (Enemy is { Kind: not EnemyKind.Missile } e && Field.Distance(e.Position, next) < e.Radius + TankRadius);
            if (blocked)
            {
                MotionBlockedTime = 1.2;
                if (_blockedSoundTimer <= 0)
                {
                    Sound.PlayBlocked();
                    _blockedSoundTimer = 0.6;
                }
            }
            else
            {
                Camera.Position = next;
            }
        }

        // One shell in flight at a time.
        bool fire = _controls.Fire;
        bool pressed = fire && !_wasFiring;
        _wasFiring = fire;
        if (pressed && PlayerShell == null)
        {
            PlayerShell = new Shell(Camera.Position + Field.Forward(Camera.Yaw) * (TankRadius + 4) + new Vec3(0, 7, 0),
                Camera.Yaw, ShellSpeed, ShellLife, fromPlayer: true);
            Sound.PlayCannon();
            Shake = Math.Max(Shake, 0.12);
        }
    }

    /// <summary>The player's tank is hit: the glass cracks, and the battle pauses before the next tank.</summary>
    private void DestroyPlayer(Vec3 from)
    {
        if (!PlayerAlive) return;
        PlayerAlive = false;
        IsMoving = false;
        DeathClock = 0;
        Shake = 0.8;
        CrackSeed = Random.Next();
        var c = Camera.ToCamera(from + new Vec3(0, 8, 0));
        CrackCentre = c.Z > 1
            ? new Avalonia.Point(Math.Clamp(Camera.Project(c).X, 200, 824), Math.Clamp(Camera.Project(c).Y, 300, 600))
            : new Avalonia.Point(512, 450);
        PlayerShell = null;
        EnemyShell = null;
        Sound.StopAllLoops();
        Sound.PlayPlayerDeath();
        Lives--;

        if (Lives <= 0)
        {
            // A demo that runs out of tanks lingers on the cracked glass, then hands back to the title.
            if (State == GameState.Attract)
            {
                _attractTimer = Math.Min(_attractTimer, 3);
                return;
            }
            State = GameState.GameOver;
            _gameOverTimer = GameOverInputDelay;
            _idleTimer = 0;
            _entryPending = Table.Qualifies(Score);
        }
    }

    // After a hit the enemy withdraws; once the glass clears the next tank rolls out where the last one died.
    private void UpdateDeath(double deltaTime)
    {
        DeathClock += deltaTime;
        if (DeathClock < DeathTime || Lives <= 0) return;

        DeathClock = -1;
        PlayerAlive = true;
        Enemy = null;
        Saucer = null;
        Sound.StopAllLoops();
        _enemyTimer = EnemyDelay;
        _wasFiring = true;
    }

    private void AddScore(int points)
    {
        Score += points;
        while (_nextBonus < BonusTankScores.Length && Score >= BonusTankScores[_nextBonus])
        {
            _nextBonus++;
            if (Lives < MaxLives)
            {
                Lives++;
                Sound.PlayExtraTank();
            }
        }
    }

    // ----- Shells -----

    private void UpdateShells(double deltaTime)
    {
        if (PlayerShell is { } ps && !MoveShell(ps, deltaTime))
            PlayerShell = null;
        if (EnemyShell is { } es && !MoveShell(es, deltaTime))
            EnemyShell = null;
    }

    /// <summary>Moves a shell, in small steps so it can't skip through anything. Returns false once it's spent.</summary>
    private bool MoveShell(Shell shell, double deltaTime)
    {
        const int steps = 4;
        for (int i = 0; i < steps; i++)
        {
            shell.Position = Field.Wrap(shell.Position + shell.Velocity * (deltaTime / steps));
            shell.Life -= deltaTime / steps;
            if (shell.Life <= 0)
                return false;

            if (Obstacles.FirstOrDefault(o => Field.Distance(o.Position, shell.Position) < o.ShellRadius) is { } obstacle &&
                shell.Position.Y < ObstacleHeight(obstacle))
            {
                Explosions.Add(Explosion.Small(shell.Position, VectorGreen));
                Sound.PlayShellBurst();
                return false;
            }

            if (shell.FromPlayer)
            {
                if (Enemy is { } e && Field.Distance(e.Position, shell.Position) < e.Radius &&
                    Math.Abs(e.Altitude + 8 - shell.Position.Y) < 30)
                {
                    DestroyEnemy();
                    return false;
                }
                if (Saucer is { } s && Field.Distance(s.Position, shell.Position) < s.Radius)
                {
                    DestroySaucer();
                    return false;
                }
            }
            else if (PlayerAlive && Field.Distance(Camera.Position, shell.Position) < TankRadius * 0.8)
            {
                DestroyPlayer(shell.Position);
                return false;
            }
        }
        return true;
    }

    private static double ObstacleHeight(Obstacle o) => o.Shape switch
    {
        ObstacleShape.Cube => o.Size * 2,
        ObstacleShape.WidePyramid => o.Size * 1.3,
        _ => o.Size * 3.2
    };

    private void DestroyEnemy()
    {
        var e = Enemy!;
        AddScore(e.Points);
        Kills++;
        Debris.Add(new Debris(e.Model, e.Position + new Vec3(0, e.Altitude, 0), e.Yaw, e.ModelSize, VectorGreen));
        Explosions.Add(Explosion.Fighter(e.Position + new Vec3(0, 10 + e.Altitude, 0), VectorGreen));
        Sound.PlayTankBoom();
        Sound.StopMissile();
        Enemy = null;
        EnemyShell = null;
        _enemyTimer = EnemyDelay;
    }

    private void DestroySaucer()
    {
        var s = Saucer!;
        AddScore(Saucer.Points);
        Debris.Add(new Debris(Wireframe.Saucer, s.Position, s.Spin, 28, VectorGreen, 2.0));
        Explosions.Add(Explosion.Fighter(s.Position, VectorGreen));
        Sound.StopSaucer();
        Sound.PlaySaucerBoom();
        Saucer = null;
    }
}
