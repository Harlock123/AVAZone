using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NetCoreAudio;

namespace AVAZone.Engine;

public class SoundManager : IDisposable
{
    private readonly string _soundsPath;
    private readonly Dictionary<string, Loop> _loops = new();

    public bool IsMuted { get; set; } = false;

    /// <summary>Set during attract mode: the game is silent without touching the player's mute setting.</summary>
    public bool IsSilenced { get; set; }

    private bool IsQuiet => IsMuted || IsSilenced;

    public SoundManager()
    {
        _soundsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sounds");
    }

    public void PlayCannon() => PlaySound("Cannon.wav");
    public void PlayEnemyCannon() => PlaySound("EnemyCannon.wav");
    public void PlayShellBurst() => PlaySound("ShellBurst.wav");
    public void PlayTankBoom() => PlaySound("TankBoom.wav");
    public void PlaySaucerBoom() => PlaySound("SaucerBoom.wav");
    public void PlayPlayerDeath() => PlaySound("Crunch.wav");
    public void PlayBlocked() => PlaySound("Blocked.wav");
    public void PlayRadarBlip() => PlaySound("RadarBlip.wav");
    public void PlayNewEnemy() => PlaySound("NewEnemy.wav");
    public void PlayMissileLaunch() => PlaySound("MissileLaunch.wav");
    public void PlayExtraTank() => PlaySound("ExtraTank.wav");

    /// <summary>The tank's engine: a low idle, and a harder growl while the treads are turning.</summary>
    public void SetEngine(bool moving)
    {
        if (moving)
        {
            StopLoop("Idle.wav");
            StartLoop("Moving.wav");
        }
        else
        {
            StopLoop("Moving.wav");
            StartLoop("Idle.wav");
        }
    }

    /// <summary>The saucer's warble, while one is about.</summary>
    public void StartSaucer() => StartLoop("Saucer.wav");
    public void StopSaucer() => StopLoop("Saucer.wav");

    /// <summary>The missile's whine as it closes in.</summary>
    public void StartMissile() => StartLoop("Missile.wav");
    public void StopMissile() => StopLoop("Missile.wav");

    public void StopAllLoops()
    {
        foreach (var name in _loops.Keys)
            StopLoop(name);
    }

    // A sound that repeats while something is held (the engines) or going on (the bridge hum). Each start
    // gets its own generation, so a loop left over from a quick stop and start ends instead of doubling up.
    private sealed class Loop
    {
        public bool Playing;
        public int Generation;
        public Player? Player;
    }

    private void StartLoop(string filename)
    {
        if (IsQuiet) return;
        if (!_loops.TryGetValue(filename, out var loop))
            _loops[filename] = loop = new Loop();
        if (loop.Playing) return;

        loop.Playing = true;
        RunLoop(filename, loop, ++loop.Generation);
    }

    private async void RunLoop(string filename, Loop loop, int generation)
    {
        var path = Path.Combine(_soundsPath, filename);
        if (!File.Exists(path))
        {
            loop.Playing = false;
            return;
        }

        var player = new Player();
        loop.Player = player;
        try
        {
            while (loop.Playing && generation == loop.Generation)
            {
                await player.Play(path);
                while (player.Playing && loop.Playing && generation == loop.Generation)
                    await Task.Delay(20);
            }
        }
        catch
        {
            // Silently ignore playback errors
        }
        finally
        {
            if (generation == loop.Generation)
                loop.Playing = false;
            else
                StopQuietly(player);
        }
    }

    private void StopLoop(string filename)
    {
        if (!_loops.TryGetValue(filename, out var loop)) return;
        loop.Playing = false;
        if (loop.Player != null)
            StopQuietly(loop.Player);
    }

    private static void StopQuietly(Player player)
    {
        try
        {
            player.Stop();
        }
        catch
        {
            // Silently ignore stop errors
        }
    }

    private async void PlaySound(string filename)
    {
        if (IsQuiet) return;

        var path = Path.Combine(_soundsPath, filename);
        if (!File.Exists(path)) return;

        try
        {
            // Create new player for each sound to allow overlapping
            var player = new Player();
            await player.Play(path);
        }
        catch
        {
            // Silently ignore playback errors
        }
    }

    public void Dispose()
    {
        StopAllLoops();
    }
}
