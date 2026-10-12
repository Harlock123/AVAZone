#!/usr/bin/env python3
"""Synthesizes the game's sound effects into ../Sounds as 16-bit mono WAV files.

Nothing is sampled from the original machine: every effect is built here from oscillators and noise,
so the sounds can be tweaked and regenerated. Run it from anywhere: python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Sounds")
random.seed(1980)


def write(name, samples, volume=0.6):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def n(seconds):
    return int(RATE * seconds)


def env(i, total, attack=0.005, release=1.0):
    """Linear attack, then a decay shaped by `release` (higher = faster fade)."""
    t = i / RATE
    a = min(1.0, t / attack) if attack > 0 else 1.0
    return a * (1 - i / total) ** release


def sweep(seconds, f0, f1, wave_fn, release=1.0, attack=0.005):
    total, phase, out = n(seconds), 0.0, []
    for i in range(total):
        f = f0 + (f1 - f0) * i / total
        phase += f / RATE
        out.append(wave_fn(phase) * env(i, total, attack=attack, release=release))
    return out


def square(p):
    return 1.0 if (p % 1) < 0.5 else -1.0


def tri(p):
    return 4 * abs((p % 1) - 0.5) - 1


def saw(p):
    return 2 * (p % 1) - 1


def sine(p):
    return math.sin(2 * math.pi * p)


def noise(seconds, release=1.0, smooth=0.0, attack=0.002):
    total, last, out = n(seconds), 0.0, []
    for i in range(total):
        last = last * smooth + random.uniform(-1, 1) * (1 - smooth)
        out.append(last * env(i, total, attack=attack, release=release))
    return out


def mix(*tracks):
    length = max(len(t) for t in tracks)
    return [sum(t[i] for t in tracks if i < len(t)) for i in range(length)]


def scale(track, k):
    return [s * k for s in track]


def silence(seconds):
    return [0.0] * n(seconds)


os.makedirs(OUT, exist_ok=True)

# The cannon: a hard, short thump with a crack on top.
write("Cannon.wav", mix(noise(0.45, release=3, smooth=0.55), scale(sweep(0.45, 160, 45, tri, release=2.2), 1.2),
                        scale(noise(0.05, release=2, smooth=0.0), 0.6)), 0.8)

# The enemy's gun: the same kind of thump, a little further off and higher.
write("EnemyCannon.wav", mix(noise(0.4, release=3, smooth=0.4), scale(sweep(0.4, 260, 80, tri, release=2.2), 0.9)), 0.55)

# A shell bursting on a block.
write("ShellBurst.wav", mix(noise(0.3, release=2.5, smooth=0.3), scale(sweep(0.3, 400, 120, square, release=2), 0.4)), 0.5)

# A tank blowing apart: a big, rolling blast.
write("TankBoom.wav", mix(noise(1.6, release=1.7, smooth=0.75), scale(sweep(1.6, 110, 28, tri, release=1.4), 1.0),
                          scale(noise(0.12, release=2, smooth=0.1), 0.7)), 0.85)

# The saucer going up: a bright crackle and a falling whistle.
write("SaucerBoom.wav", mix(noise(0.9, release=1.8, smooth=0.4), scale(sweep(0.9, 1500, 200, sine, release=1.2), 0.6)), 0.6)

# The player's tank hit: a splintering crunch of glass over a deep thud.
total = n(1.4)
crunch = []
for i in range(total):
    t = i / RATE
    shard = random.uniform(-1, 1) if random.random() < 0.18 * math.exp(-t * 2.5) else 0.0
    crunch.append(shard + 1.2 * math.sin(2 * math.pi * (55 - 20 * t) * t) * math.exp(-t * 3))
write("Crunch.wav", mix(crunch, scale(noise(1.4, release=2, smooth=0.85), 1.5)), 0.85)

# Driving into an obstacle: a dull clank.
write("Blocked.wav", mix(sweep(0.18, 140, 90, square, release=2), scale(noise(0.1, release=3, smooth=0.6), 0.5)), 0.35)

# The radar's blip as the sweep crosses the enemy.
write("RadarBlip.wav", sweep(0.07, 1450, 1450, sine, release=1.5, attack=0.003), 0.18)

# A new enemy in the area: three quick rising beeps.
beeps = []
for f in (700, 900, 1150):
    beeps += sweep(0.07, f, f, square, release=0.4) + silence(0.04)
write("NewEnemy.wav", beeps, 0.22)

# A missile launched: a rising scream.
write("MissileLaunch.wav", mix(sweep(0.8, 300, 1600, saw, release=0.7, attack=0.05), scale(noise(0.8, release=1, smooth=0.3), 0.4)), 0.35)

# The missile in flight: a steady, wavering whine, made of whole-cycle tones so it loops.
total = n(1.0)
whine = []
for i in range(total):
    t = i / RATE
    whine.append(math.sin(2 * math.pi * 880 * t + 3 * math.sin(2 * math.pi * 8 * t)) + 0.4 * math.sin(2 * math.pi * 1320 * t))
write("Missile.wav", whine, 0.12)

# The saucer: the classic rising-and-falling warble, looped.
total = n(1.0)
warble, phase = [], 0.0
for i in range(total):
    t = i / RATE
    phase += (700 + 350 * math.sin(2 * math.pi * 4 * t)) / RATE
    warble.append(sine(phase))
write("Saucer.wav", warble, 0.18)

# The engine idling: a slow, low throb.
total = n(1.0)
idle, last = [], 0.0
for i in range(total):
    t = i / RATE
    last = last * 0.96 + random.uniform(-1, 1) * 0.04
    throb = 0.6 + 0.4 * math.sin(2 * math.pi * 6 * t)
    idle.append((4 * last + 0.5 * math.sin(2 * math.pi * 40 * t)) * throb)
write("Idle.wav", idle, 0.22)

# The engine with the treads turning: faster and harder.
total = n(1.0)
moving, last = [], 0.0
for i in range(total):
    t = i / RATE
    last = last * 0.9 + random.uniform(-1, 1) * 0.1
    throb = 0.65 + 0.35 * math.sin(2 * math.pi * 14 * t)
    moving.append((3 * last + 0.5 * math.sin(2 * math.pi * 60 * t) + 0.25 * square(60 * t)) * throb)
write("Moving.wav", moving, 0.3)

# An extra tank: a bright little fanfare.
notes = []
for f in (523, 659, 784, 1047, 784, 1047):
    notes += sweep(0.08, f, f, square, release=0.3)
write("ExtraTank.wav", notes, 0.3)

print("Wrote sounds to", os.path.normpath(OUT))
