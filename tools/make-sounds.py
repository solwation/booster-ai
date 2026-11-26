#!/usr/bin/env python3
"""Generates the short notification chimes in assets/sounds/ (#240) from plain sine waves.

Everything is synthesised here, no third-party audio: 16-bit mono PCM, 22050 Hz, soft attack and
exponential decay, peak about 0.3 of full scale. Run `python3 tools/make-sounds.py` to regenerate.
Licence: MIT, like the rest of the repository.
"""
import math, os, struct, wave

RATE = 22050
PEAK = 0.30


def note(freq, dur, delay=0.0, gain=1.0):
    """One soft chime note: sine plus a quiet octave, 8 ms attack, exponential decay."""
    n = int(RATE * dur)
    out = []
    for i in range(n):
        t = i / RATE
        env = min(1.0, t / 0.008) * math.exp(-t * 7.0 / dur)
        s = math.sin(2 * math.pi * freq * t) + 0.25 * math.sin(2 * math.pi * 2 * freq * t)
        out.append(s * env * gain)
    return int(RATE * delay), out


def mix(notes):
    length = max(d + len(s) for d, s in notes)
    buf = [0.0] * length
    for d, s in notes:
        for i, v in enumerate(s):
            buf[d + i] += v
    peak = max(abs(v) for v in buf)
    return [v / peak * PEAK for v in buf]


SOUNDS = {
    # A tab finished or waits: two rising notes.
    "finished": [note(784.0, 0.22), note(1046.5, 0.30, 0.13)],
    # A tab failed: two lower, falling notes.
    "error": [note(392.0, 0.25), note(311.1, 0.40, 0.16)],
    # Quota low: one mid note struck twice.
    "quota-low": [note(659.3, 0.18), note(659.3, 0.30, 0.20, 0.8)],
    # Quota back: a quick rising triad.
    "quota-back": [note(523.3, 0.18), note(659.3, 0.18, 0.09), note(784.0, 0.34, 0.18)],
}

out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "sounds")
for name, notes in SOUNDS.items():
    with wave.open(os.path.join(out_dir, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(v * 32767)) for v in mix(notes)))
