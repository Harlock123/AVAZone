using System;
using Avalonia;
using Avalonia.Media;
using AVAZone.Engine;
using AVAZone.Models;

namespace AVAZone.Views;

// The red readouts across the top (radar, warnings, score and tanks left), and the title, entry and
// high-score screens.
public partial class GameCanvas
{
    private static Color Red => GameEngine.OverlayRed;
    private static readonly Point RadarCentre = new(ScreenWidth / 2, 82);
    private const double RadarRadius = 64;

    private void DrawHud(DrawingContext context)
    {
        if (_engine.State == GameState.EnterInitials) return;

        DrawRadar(context);

        // Warnings, down the left.
        if (_engine.IsInPlay)
        {
            if (_engine.IsEnemyInRange)
                context.DrawText(Text("ENEMY IN RANGE", 17, Red), new Point(34, 24));
            if (_engine.EnemyDirection is { } dir && Blink(3))
                context.DrawText(Text($"ENEMY TO {dir}", 17, Red), new Point(34, 52));
            if (_engine.MotionBlockedTime > 0)
                context.DrawText(Text("MOTION BLOCKED BY OBJECT", 17, Red), new Point(34, 80));
        }

        // Score, high score and tanks left, down the right.
        int high = _engine.State == GameState.Attract ? _engine.HighScore : Math.Max(_engine.HighScore, _engine.Score);
        DrawRightText(context, $"SCORE {_engine.Score}", 20, ScreenWidth - 34, 20, Red);
        DrawRightText(context, $"HIGH SCORE {high}", 15, ScreenWidth - 34, 52, Red);
        int spare = Math.Max(0, _engine.Lives - (_engine.PlayerAlive ? 1 : 0));
        for (int i = 0; i < spare && i < 8; i++)
            DrawModelShowcase(context, Wireframe.Tank, new Point(ScreenWidth - 54 - i * 42, 112), 12, 1.0, 0.3, Red, 0.9, 1.2);

        if (_engine.Sound.IsMuted)
            DrawRightText(context, "MUTED", 11, ScreenWidth - 34, 132, Red);

        if (_engine.State == GameState.Attract)
            DrawCentredText(context, "DEMONSTRATION", 18, 160, Red);
    }

    // The radar: a circle with the player at its middle and ahead at the top, a sweep going round, and
    // the enemy as a dot that flares up as the sweep passes it. The saucer never shows.
    private void DrawRadar(DrawingContext context)
    {
        var c = RadarCentre;
        var ring = new Point[32];
        for (int i = 0; i < ring.Length; i++)
        {
            double a = i * Math.PI * 2 / ring.Length;
            ring[i] = new Point(c.X + Math.Cos(a) * RadarRadius, c.Y + Math.Sin(a) * RadarRadius);
        }
        DrawGlowPolygon(context, ring, Red, 0.8, 1.4);

        // The view's edges, as a V from the middle.
        foreach (double side in new[] { -1.0, 1.0 })
        {
            double a = side * Camera.HalfFieldOfView;
            DrawGlowLine(context, c, c + new Vector(Math.Sin(a), -Math.Cos(a)) * RadarRadius * 0.45, Red, 0.6, 1.2);
        }

        if (!_engine.IsInPlay) return;

        double sweep = _engine.RadarSweep;
        DrawGlowLine(context, c, c + new Vector(Math.Sin(sweep), -Math.Cos(sweep)) * RadarRadius, Red, 0.9, 1.5);

        if (_engine.Enemy is { } e && _engine.IsEnemyInRange)
        {
            double bearing = Field.AngleDiff(Field.Bearing(_engine.Camera.Position, e.Position), _engine.Camera.Yaw);
            double r = Field.Distance(_engine.Camera.Position, e.Position) / GameEngine.RadarRange * RadarRadius;
            var dot = c + new Vector(Math.Sin(bearing), -Math.Cos(bearing)) * r;

            // Brightest just after the sweep has passed, fading till it comes round again.
            double since = sweep - (bearing < 0 ? bearing + Math.PI * 2 : bearing);
            if (since < 0) since += Math.PI * 2;
            double glow = 1 - since / (Math.PI * 2);
            context.DrawEllipse(GetBrush(Red, 0.2 + 0.8 * glow), null, dot, 3.5, 3.5);
        }
    }

    // ----- Title screen -----

    private void DrawTitleScreen(DrawingContext context, bool withText)
    {
        // The battlefield turning slowly behind everything.
        DrawSky(context);
        DrawGround(context);

        if (!withText) return;

        context.FillRectangle(GetBrush(Colors.Black, 0.55), new Rect(0, 0, ScreenWidth, ScreenHeight));

        DrawCentredText(context, "AVAZONE", 80, 14, Red);
        DrawCentredText(context, "TANK BATTLE ON THE VECTOR PLAIN", 20, 108, Green);

        DrawModelShowcase(context, Wireframe.Tank, new Point(ScreenWidth / 2, 262), 95, Clock * 0.5 + 2.4, 0.32, Green, 1, 1.6);

        var legend = new (Wireframe Model, string Name, string Value, double Size)[]
        {
            (Wireframe.Tank, "TANK", "1000", 26),
            (Wireframe.Missile, "MISSILE", "2000", 24),
            (Wireframe.SuperTank, "SUPERTANK", "3000", 24),
            (Wireframe.Saucer, "SAUCER", "5000", 26)
        };
        for (int i = 0; i < legend.Length; i++)
        {
            var (model, name, value, size) = legend[i];
            double cx = 152 + i * 240;
            DrawModelShowcase(context, model, new Point(cx, 422), size, Clock * 0.8 + i, 0.35, Green, 1, 1.3);
            DrawCentredText(context, name, 14, 456, Green, cx);
            DrawCentredText(context, value, 16, 476, Colors.White, cx);
        }

        var controls = new (string Keys, string Action)[]
        {
            ("W / S, UP / DOWN", "Forward, reverse"),
            ("A / D, LEFT / RIGHT", "Turn"),
            ("SPACE, CTRL, click", "Fire"),
            ("Gamepad", "Left stick drives; with the right"),
            ("", "stick too, each works one tread"),
            ("ESC  M", "Pause, mute")
        };
        string[] orders =
        {
            "Find the enemy on the radar and turn to",
            "face it before it lines up its gun.",
            "Tanks first, then missiles from 10,000,",
            "then supertanks from 25,000. Hide behind",
            "the blocks: they stop shells. Saucers are",
            "harmless and worth 5,000. Extra tanks at",
            "15,000 and 100,000."
        };
        for (int i = 0; i < orders.Length; i++)
        {
            double y = 512 + i * 20;
            if (i < controls.Length)
            {
                context.DrawText(Text(controls[i].Keys, 14, Colors.White), new Point(48, y));
                context.DrawText(Text(controls[i].Action, 14, Green), new Point(230, y));
            }
            context.DrawText(Text(orders[i], 14, Green), new Point(580, y));
        }

        var best = _engine.Table.Entries.Count > 0 ? _engine.Table.Entries[0] : null;
        DrawCentredText(context, best == null ? "HIGH SCORE 0" : $"HIGH SCORE  {best.Initials.TrimEnd()}  {best.Score}", 18, 680, Red);
        if (Blink(2))
            DrawCentredText(context, "Press SPACE or click to start", 26, 712, Colors.White);
    }

    // ----- High scores -----

    private string NewEntryHeadline => _engine.Score > _engine.HighScore ? "NEW HIGH SCORE" : "YOU MADE THE TOP TEN";

    private void DrawInitialsEntry(DrawingContext context)
    {
        var entry = _engine.Entry;
        if (entry == null) return;

        DrawPanel(context, new Rect(152, 110, 720, 560));
        DrawCentredText(context, NewEntryHeadline, 40, 140, Red);
        DrawCentredText(context, $"SCORE {_engine.Score}    ENEMIES DESTROYED {_engine.Kills}", 22, 200, Green);
        DrawCentredText(context, "ENTER YOUR INITIALS, COMMANDER", 24, 260, Colors.White);

        bool blinkOn = Blink(3);
        const double cell = 100;
        double left = (ScreenWidth - cell * HighScoreTable.InitialsLength) / 2;
        string initials = entry.Initials;

        for (int i = 0; i < initials.Length; i++)
        {
            bool current = i == entry.Cursor;
            char c = initials[i];
            var letter = Text(c == ' ' ? "_" : c.ToString(), 76, current ? Colors.White : Green);
            double x = left + i * cell + (cell - letter.Width) / 2;
            context.DrawText(letter, new Point(x, 315));

            if (current)
            {
                var up = Text("^", 22, Green);
                var down = Text("v", 22, Green);
                context.DrawText(up, new Point(left + i * cell + (cell - up.Width) / 2, 300));
                context.DrawText(down, new Point(left + i * cell + (cell - down.Width) / 2, 418));
                if (blinkOn)
                    DrawGlowLine(context, new Point(left + i * cell + 18, 412), new Point(left + (i + 1) * cell - 18, 412), Colors.White, 1, 2.5);
            }
        }

        string[] help =
        {
            "Type your initials, or UP/DOWN to choose a letter",
            "and LEFT/RIGHT to move.  ENTER when done.",
            "Gamepad: D-pad to choose, X to accept, START when done",
            "Mouse: wheel to choose, click to accept"
        };
        double y = 480;
        foreach (var line in help)
        {
            DrawCentredText(context, line, 16, y, Green);
            y += 26;
        }
    }

    private void DrawHighScoreTable(DrawingContext context)
    {
        DrawPanel(context, new Rect(192, 60, 640, 640));
        DrawCentredText(context, "HIGH SCORES", 44, 80, Red);

        const double rankX = 250, nameX = 340, scoreRight = 620, killsX = 670;
        double y = 165;
        context.DrawText(Text("RANK", 16, Green), new Point(rankX, y));
        context.DrawText(Text("NAME", 16, Green), new Point(nameX, y));
        DrawRightText(context, "SCORE", 16, scoreRight, y, Green);
        context.DrawText(Text("KILLS", 16, Green), new Point(killsX, y));

        bool blinkOn = Blink(3);
        var entries = _engine.Table.Entries;
        y = 200;
        for (int i = 0; i < HighScoreTable.Size; i++)
        {
            bool highlight = i == _engine.HighlightIndex;
            var color = highlight ? (blinkOn ? Colors.White : Red) : i == 0 ? Red : Colors.White;

            string name = i < entries.Count ? entries[i].Initials : "---";
            string score = i < entries.Count ? entries[i].Score.ToString() : "0";
            string kills = i < entries.Count ? entries[i].Kills.ToString() : "-";

            context.DrawText(Text($"{i + 1}.", 24, color), new Point(rankX, y));
            context.DrawText(Text(name, 24, color), new Point(nameX, y));
            DrawRightText(context, score, 24, scoreRight, y, color);
            DrawRightText(context, kills, 24, killsX + 60, y, color);

            if (highlight)
                DrawGlowLine(context, new Point(rankX - 20, y + 15), new Point(rankX - 8, y + 15), color, 1, 2);
            y += 42;
        }

        if (Blink(2))
            DrawCentredText(context, "Press SPACE or click to play", 24, 650, Colors.White);
    }
}
