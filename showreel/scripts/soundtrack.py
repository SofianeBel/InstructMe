#!/usr/bin/env python3
"""Soundtrack generator for the InstructMe showreel.

It synthesizes the whole soundtrack (music and sound effects) with numpy and scipy, and mixes in
the one recorded voice clip (public/voice/reach.wav). It downloads nothing.

Every cue time comes from src/timeline.ts (read through Node), so a re-timed picture gives a
re-timed soundtrack. All randomness uses fixed seeds, so each run writes the same file.

    npm run soundtrack                                  # writes public/soundtrack.wav
    python scripts/soundtrack.py --stems out/stems      # also writes pre-master stems + cue list

Output: 48 kHz, stereo, 24-bit PCM, exactly TOTAL / FPS seconds, about -14 LUFS integrated,
true peak at most -1 dBTP.
"""

from __future__ import annotations

import argparse
import json
import struct
import subprocess
import sys
import time
import zlib
from pathlib import Path

import numpy as np
from scipy import signal
from scipy.io import wavfile
from scipy.ndimage import minimum_filter1d

SR = 48_000
TAU = 2.0 * np.pi
ROOT = Path(__file__).resolve().parent.parent
TARGET_LUFS = -14.0
CEILING_DB = -2.0  # limiter ceiling (true peak); AAC adds up to about 0.8 dB on sharp hits, so the MP4 stays under -1.0 dBTP


# =========================================================================== timeline

def load_timeline() -> dict:
    """Reads the exports of src/timeline.ts with Node (type stripping) as a dict."""
    src = ROOT / "src" / "timeline.ts"
    code = f"import({json.dumps(src.as_uri())}).then((m) => console.log(JSON.stringify(m)))"
    cmd = ["node", "--experimental-strip-types", "--no-warnings", "-e", code]
    try:
        res = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, check=True)
    except (OSError, subprocess.CalledProcessError) as err:
        sys.exit(f"soundtrack: cannot read {src} with Node 22+: {getattr(err, 'stderr', '') or err}")
    return json.loads(res.stdout)


class Cues:
    """Cue times in seconds. Every value is derived from the timeline (frames at FPS)."""

    def __init__(self, tl: dict) -> None:
        fps = tl["FPS"]
        scene = tl["SCENE"]
        hook, demo, feat, end = tl["HOOK"], tl["DEMO"], tl["FEATURES"], tl["END"]

        def at(name: str, frame: float) -> float:
            return (scene[name]["from"] + frame) / fps

        def d(key: str) -> float:
            return at("demo", demo[key])

        self.fps = fps
        self.total = tl["TOTAL"] / fps
        self.n = round(tl["TOTAL"] * SR / fps)
        self.beat = tl["BEAT"] / fps
        self.bar = 4 * self.beat
        # hook
        self.flashes = [at("hook", f) for f in hook["flashes"]]
        self.flash_len = hook["flashLength"] / fps
        self.question = at("hook", hook["question"])
        # demo
        self.game = at("demo", 0)
        self.type_start, self.type_end = d("typeStart"), d("typeEnd")
        self.keys = [d("ctrl"), d("alt"), d("l")]
        self.freeze = d("freeze")
        self.scan_start, self.scan_end = d("scanStart"), d("scanEnd")
        self.cursor_in, self.cursor_arrive = d("cursorIn"), d("cursorArrive")
        self.click = d("click")
        self.drop = d("cardOpen")
        self.speaker_click = d("speakerClick")
        self.voice = d("voice")
        self.down, self.rb, self.a, self.b = d("down"), d("rb"), d("a"), d("b")
        self.unfreeze = d("unfreeze")
        # features
        self.pops = [at("features", f) for f in feat["pops"]]
        self.collapse = at("features", feat["collapse"])
        # end card
        self.logo = at("end", end["logo"])
        self.select = at("end", end["select"])
        self.decode, self.decode_end = at("end", end["decode"]), at("end", end["decodeEnd"])
        # musical sections derived from the cues
        self.riser_start = self.freeze - 2 * self.beat    # riser into the freeze
        self.predrop_start = self.drop - 2 * self.beat    # pre-drop riser
        self.build_start = self.logo - 2 * self.beat      # snare roll / build


# =========================================================================== helpers

def ns(sec: float) -> int:
    """Seconds to samples."""
    return int(round(sec * SR))


def tax(n: int) -> np.ndarray:
    """Time axis (seconds) for n samples."""
    return np.arange(n) / SR


def db(v):
    """Decibels to linear gain."""
    return 10.0 ** (np.asarray(v, dtype=float) / 20.0) if np.ndim(v) else 10.0 ** (v / 20.0)


def rng(name: str) -> np.random.Generator:
    """Random generator with a fixed seed derived from a name (stable across runs and order)."""
    return np.random.default_rng(zlib.crc32(name.encode("utf-8")))


def norm(x: np.ndarray, peak: float = 1.0) -> np.ndarray:
    m = float(np.max(np.abs(x)))
    return x * (peak / m) if m > 0 else x


def fade(n: int, fin: float = 0.0, fout: float = 0.0) -> np.ndarray:
    """Gain curve with cos^2 fade-in and fade-out (seconds); ends exactly at 0."""
    e = np.ones(n)
    i, o = min(ns(fin), n), min(ns(fout), n)
    if i > 0:
        e[:i] = np.sin(0.5 * np.pi * np.arange(i) / i) ** 2
    if o > 0:
        e[n - o:] *= np.cos(0.5 * np.pi * np.arange(1, o + 1) / o) ** 2
    return e


def attack(t: np.ndarray, a: float) -> np.ndarray:
    return np.clip(t / a, 0.0, 1.0)


def stereo(x: np.ndarray, pan=0.0) -> np.ndarray:
    """Equal-power pan of a mono signal (-1 left, +1 right). Stereo input passes through."""
    if x.ndim == 2:
        return x
    ang = (np.clip(pan, -1.0, 1.0) + 1.0) * np.pi / 4.0
    return np.stack([x * np.cos(ang), x * np.sin(ang)])


def balance(x: np.ndarray, pan) -> np.ndarray:
    """Equal-power balance of a stereo signal (pan may vary per sample)."""
    ang = (np.clip(pan, -1.0, 1.0) + 1.0) * np.pi / 4.0
    return np.stack([x[0] * np.cos(ang), x[1] * np.sin(ang)]) * np.sqrt(2.0)


def place(bus: np.ndarray, sig: np.ndarray, t: float, gain: float = 1.0) -> None:
    """Adds sig (mono or stereo) into bus, starting at t seconds (sample accurate)."""
    sig = stereo(sig)
    i0 = ns(t)
    a, b = max(0, i0), min(bus.shape[1], i0 + sig.shape[1])
    if b > a:
        bus[:, a:b] += gain * sig[:, a - i0:b - i0]


def grid(t0: float, t1: float, step: float) -> list[float]:
    """Times t0, t0 + step, ... strictly before t1."""
    return [t0 + i * step for i in range(max(0, int(np.ceil((t1 - t0) / step - 1e-9))))]


_PC = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note: str) -> float:
    """Note name ('A2', 'F#4', 'Bb3') to frequency (A4 = 440 Hz)."""
    pc, i = _PC[note[0]], 1
    if note[1] in "#b":
        pc += 1 if note[1] == "#" else -1
        i = 2
    return 440.0 * 2.0 ** ((12 * (int(note[i:]) + 1) + pc - 69) / 12.0)


def bezier(x1: float, y1: float, x2: float, y2: float):
    """CSS cubic-bezier easing (same curve as Remotion Easing.bezier), vectorized."""
    def bx(s):
        return 3 * (1 - s) ** 2 * s * x1 + 3 * (1 - s) * s * s * x2 + s ** 3

    def by(s):
        return 3 * (1 - s) ** 2 * s * y1 + 3 * (1 - s) * s * s * y2 + s ** 3

    def ease(u):
        u = np.clip(np.asarray(u, dtype=float), 0.0, 1.0)
        lo, hi = np.zeros_like(u), np.ones_like(u)
        for _ in range(40):
            mid = 0.5 * (lo + hi)
            below = bx(mid) < u
            lo, hi = np.where(below, mid, lo), np.where(below, hi, mid)
        return by(0.5 * (lo + hi))

    return ease


EASE_INOUT = bezier(0.65, 0.0, 0.35, 1.0)  # EASE.inOut in src/lib/anim.ts


def inverse(fn, y: float) -> float:
    """Inverse of a monotonic 0..1 easing by bisection."""
    lo, hi = 0.0, 1.0
    for _ in range(50):
        mid = 0.5 * (lo + hi)
        lo, hi = (mid, hi) if float(fn(mid)) < y else (lo, mid)
    return 0.5 * (lo + hi)


# =========================================================================== oscillators

def phase(freq, n: int, phase0: float = 0.0):
    """Unwrapped phase (cycles) and per-sample increment for a fixed or varying frequency."""
    inc = np.broadcast_to(np.asarray(freq, dtype=float) / SR, (n,))
    ph = np.empty(n)
    ph[0] = phase0
    np.cumsum(inc[:-1], out=ph[1:])
    ph[1:] += phase0
    return ph, inc


def sine(freq, n: int, phase0: float = 0.0) -> np.ndarray:
    return np.sin(TAU * phase(freq, n, phase0)[0])


def _blep(t: np.ndarray, dt: np.ndarray) -> np.ndarray:
    y = np.zeros_like(t)
    m = t < dt
    x = t[m] / dt[m]
    y[m] = x + x - x * x - 1.0
    m = t > 1.0 - dt
    x = (t[m] - 1.0) / dt[m]
    y[m] = x * x + x + x + 1.0
    return y


def saw(freq, n: int, phase0: float = 0.0) -> np.ndarray:
    """Band-limited sawtooth (PolyBLEP)."""
    ph, dt = phase(freq, n, phase0)
    t = ph % 1.0
    return 2.0 * t - 1.0 - _blep(t, dt)


def square(freq, n: int, phase0: float = 0.0) -> np.ndarray:
    """Band-limited square (PolyBLEP)."""
    ph, dt = phase(freq, n, phase0)
    t = ph % 1.0
    return np.where(t < 0.5, 1.0, -1.0) + _blep(t, dt) - _blep((t + 0.5) % 1.0, dt)


def modes(t: np.ndarray, table, pitch: float = 1.0) -> np.ndarray:
    """Sum of damped sines: table of (frequency, decay tau, amplitude)."""
    y = np.zeros_like(t)
    for f, tau, a in table:
        y += a * np.sin(TAU * f * pitch * t) * np.exp(-t / tau)
    return y


# =========================================================================== filters

def filt(x, kind: str, fc, order: int = 2):
    """Butterworth 'lowpass' / 'highpass' / 'bandpass' along the last axis."""
    return signal.sosfilt(signal.butter(order, fc, btype=kind, fs=SR, output="sos"), x, axis=-1)


def rbj(kind: str, f0, q=0.7071, gain_db: float = 0.0):
    """RBJ cookbook biquads, vectorized over f0 / q. Returns b, a of shape (k, 3)."""
    f0 = np.atleast_1d(np.clip(np.asarray(f0, dtype=float), 5.0, 0.49 * SR))
    q = np.broadcast_to(np.asarray(q, dtype=float), f0.shape)
    w = TAU * f0 / SR
    cw, sw = np.cos(w), np.sin(w)
    al = sw / (2.0 * q)
    A = 10.0 ** (gain_db / 40.0)
    if kind == "lp":
        b, a = [(1 - cw) / 2, 1 - cw, (1 - cw) / 2], [1 + al, -2 * cw, 1 - al]
    elif kind == "hp":
        b, a = [(1 + cw) / 2, -(1 + cw), (1 + cw) / 2], [1 + al, -2 * cw, 1 - al]
    elif kind == "bp":
        b, a = [al, np.zeros_like(w), -al], [1 + al, -2 * cw, 1 - al]
    elif kind == "peak":
        b, a = [1 + al * A, -2 * cw, 1 - al * A], [1 + al / A, -2 * cw, 1 - al / A]
    elif kind == "hs":
        s = 2 * np.sqrt(A) * al
        b = [A * ((A + 1) + (A - 1) * cw + s), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - s)]
        a = [(A + 1) - (A - 1) * cw + s, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - s]
    elif kind == "ls":
        s = 2 * np.sqrt(A) * al
        b = [A * ((A + 1) - (A - 1) * cw + s), 2 * A * ((A - 1) - (A + 1) * cw), A * ((A + 1) - (A - 1) * cw - s)]
        a = [(A + 1) + (A - 1) * cw + s, -2 * ((A - 1) + (A + 1) * cw), (A + 1) + (A - 1) * cw - s]
    else:
        raise ValueError(kind)
    b, a = np.stack(b, -1), np.stack(a, -1)
    return b / a[:, :1], a / a[:, :1]


def eq(x, kind: str, f0: float, q: float = 0.7071, gain_db: float = 0.0):
    b, a = rbj(kind, f0, q, gain_db)
    return signal.lfilter(b[0], a[0], x, axis=-1)


def tv_filter(x, kind: str, fc, q=0.7071, block: int = 32):
    """Time-varying biquad: coefficients follow the per-sample curve fc, updated every block."""
    x2 = np.atleast_2d(x)
    n = x2.shape[1]
    starts = np.arange(0, n, block)
    mid = np.minimum(starts + block // 2, n - 1)
    fcv = np.broadcast_to(np.asarray(fc, dtype=float), (n,))[mid]
    qv = np.broadcast_to(np.asarray(q, dtype=float), (n,))[mid]
    b, a = rbj(kind, fcv, qv)
    y = np.empty_like(x2)
    zi = np.zeros((x2.shape[0], 2))
    for k, s in enumerate(starts):
        y[:, s:s + block], zi = signal.lfilter(b[k], a[k], x2[:, s:s + block], axis=-1, zi=zi)
    return y if np.ndim(x) == 2 else y[0]


def smooth_noise(n: int, rate: float, seed: str) -> np.ndarray:
    """Smooth random curve in [-1, 1] (cubic through random points `rate` times per second)."""
    pts = rng(seed).uniform(-1.0, 1.0, int(n / SR * rate) + 5)
    return cubic_read(pts, np.arange(n) / SR * rate + 1.0)


# =========================================================================== time-domain effects

def shift(x: np.ndarray, d: int) -> np.ndarray:
    """Delays x by d >= 0 samples, keeping its length."""
    y = np.zeros_like(x)
    if d < x.shape[-1]:
        y[..., d:] = x[..., :x.shape[-1] - d]
    return y


def cubic_read(x: np.ndarray, pos: np.ndarray) -> np.ndarray:
    """Catmull-Rom read of x (n,) or (C, n) at fractional sample positions (zero outside)."""
    x2 = np.atleast_2d(x)
    n = x2.shape[1]
    xp = np.pad(x2, ((0, 0), (2, 3)))
    i = np.floor(pos).astype(np.int64)
    f = pos - i
    i = np.clip(i, -1, n) + 2
    y0, y1, y2, y3 = xp[:, i - 1], xp[:, i], xp[:, i + 1], xp[:, i + 2]
    c1 = 0.5 * (y2 - y0)
    c2 = y0 - 2.5 * y1 + 2.0 * y2 - 0.5 * y3
    c3 = 0.5 * (y3 - y0) + 1.5 * (y1 - y2)
    out = ((c3 * f + c2) * f + c1) * f + y1
    return out if np.ndim(x) == 2 else out[0]


def allpass(x: np.ndarray, d: int, g: float) -> np.ndarray:
    """Schroeder allpass y[n] = -g x[n] + x[n-d] + g y[n-d], exact, in blocks of d samples."""
    n = x.shape[-1]
    xp = np.concatenate([np.zeros(d), x])
    yp = np.zeros(n + d)
    for c0 in range(0, n, d):
        c1 = min(c0 + d, n)
        yp[c0 + d:c1 + d] = -g * xp[c0 + d:c1 + d] + xp[c0:c1] + g * yp[c0:c1]
    return yp[d:]


def _odd_prime(v: float) -> int:
    k = max(3, int(v)) | 1
    while any(k % p == 0 for p in range(3, int(k ** 0.5) + 1, 2)):
        k += 2
    return k


def _hadamard(n: int) -> np.ndarray:
    h = np.ones((1, 1))
    while h.shape[0] < n:
        h = np.block([[h, h], [h, -h]])
    return h / np.sqrt(n)


def _fdn(x, rt60, rt60_hf, predelay, size, diffusion, er, seed, lines):
    r = rng(seed)
    n = x.shape[1]
    src = shift(x, ns(predelay))
    diff = np.empty_like(src)
    for ch in range(2):  # input diffusion, different lengths per side
        s = src[ch]
        for dt in (0.0043, 0.0035, 0.0127, 0.0093):
            s = allpass(s, _odd_prime(dt * SR * (1.0 + 0.113 * ch)), diffusion)
        diff[ch] = s
    D = np.array([_odd_prime(v) for v in np.geomspace(0.017, 0.075, lines) * SR * size])
    gdc = 10.0 ** (-3.0 * D / (SR * rt60))
    ratio = 10.0 ** (-3.0 * D / (SR * rt60_hf)) / gdc
    p = (1.0 - ratio) / (1.0 + ratio)          # Jot absorption: one-pole per line
    b0 = gdc * (1.0 - p)
    A = _hadamard(lines)
    sg = r.choice([-1.0, 1.0], lines)
    bin_ = np.zeros((lines, 2))
    bin_[0::2, 0], bin_[1::2, 1] = sg[0::2], sg[1::2]
    cout = A[[1, 2]] * r.choice([-1.0, 1.0], lines)
    maxd, step = int(D.max()), int(D.min())
    buf = np.zeros((lines, maxd + n))
    zi = np.zeros((lines, 1))
    out = np.zeros((2, n))
    for c0 in range(0, n, step):
        c1 = min(c0 + step, n)
        o = np.stack([buf[i, maxd + c0 - D[i]:maxd + c1 - D[i]] for i in range(lines)])
        for i in range(lines):
            o[i], zi[i] = signal.lfilter([b0[i]], [1.0, -p[i]], o[i], zi=zi[i])
        out[:, c0:c1] = cout @ o
        buf[:, maxd + c0:maxd + c1] = A @ o + bin_ @ diff[:, c0:c1]
    if er > 0:  # early reflections from the pre-delayed source
        taps = np.sort(r.uniform(0.003, 0.03, 10)) * size
        for k, tt in enumerate(taps):
            ch = k % 2
            out[ch] += er * 0.8 ** k * r.choice([-1.0, 1.0]) * shift(0.7 * src[ch] + 0.3 * src[1 - ch], ns(tt))
    return out


def reverb(x, rt60=2.0, rt60_hf=0.7, predelay=0.02, size=1.0, diffusion=0.62, er=0.0,
           hp=150.0, lp=11000.0, seed="reverb", lines=16) -> np.ndarray:
    """Stereo 16-line FDN reverb (Hadamard feedback, Jot absorption filters, allpass input
    diffusion, optional early reflections). Returns the wet signal, scaled to unit IR energy."""
    imp = np.zeros((2, ns(min(1.5 * rt60, 6.0))))
    imp[:, 0] = 1.0 / np.sqrt(2.0)
    ir = _fdn(imp, rt60, rt60_hf, predelay, size, diffusion, er, seed, lines)
    scale = 1.0 / np.sqrt(np.sum(ir ** 2))
    wet = _fdn(x, rt60, rt60_hf, predelay, size, diffusion, er, seed, lines) * scale
    return filt(filt(wet, "highpass", hp), "lowpass", lp)


def pingpong(x: np.ndarray, delay_s: float, feedback: float = 0.4, repeats: int = 7,
             hp: float = 450.0, lp: float = 5000.0) -> np.ndarray:
    """Ping-pong delay: repeats bounce L, R, L... and each pass through the loop filters darkens it."""
    s = x.mean(axis=0)
    d = ns(delay_s)
    s_hp = signal.butter(1, hp, "highpass", fs=SR, output="sos")
    s_lp = signal.butter(1, lp, "lowpass", fs=SR, output="sos")
    y = np.zeros_like(x)
    for k in range(1, repeats + 1):
        s = signal.sosfilt(s_lp, signal.sosfilt(s_hp, s))
        y[(k - 1) % 2] += feedback ** (k - 1) * shift(s, k * d)
    return y


def wow(x: np.ndarray, depth=0.0011, rate=0.9, flutter=0.00022, frate=6.5, seed="wow") -> np.ndarray:
    """Tape wow and flutter (modulated delay)."""
    r = rng(seed)
    n = x.shape[-1]
    t = tax(n)
    d = SR * (depth * 0.5 * (1 + np.sin(TAU * rate * t + r.uniform(0, TAU)))
              + flutter * 0.5 * (1 + np.sin(TAU * frate * t + r.uniform(0, TAU)))) + 2.0
    return cubic_read(x, np.arange(n) - d)


def tape_stop(x: np.ndarray, t0: float, dur: float, until: float, curve: float = 1.3) -> np.ndarray:
    """Tape stop at t0: speed (and pitch) fall from 1 to 0 over dur, then silence until `until`."""
    y = x.copy()
    i0, m, i1 = ns(t0), ns(dur), ns(until)
    rate = (1.0 - np.arange(m) / m) ** curve
    pos = i0 + np.concatenate([[0.0], np.cumsum(rate[:-1])])
    y[:, i0:i0 + m] = cubic_read(x, pos) * (rate ** 0.35) * fade(m, 0.0, 0.03)
    y[:, i0 + m:i1] = 0.0
    return y


def duck_shape(n: int, times, attack_s=0.003, release=0.19, curve=2.2) -> np.ndarray:
    """0..1 sidechain amount from trigger times (1 = fully ducked at each trigger)."""
    shp = np.zeros(n)
    la, lr = ns(attack_s), ns(release)
    seg = np.concatenate([np.linspace(0.0, 1.0, la, endpoint=False), (1.0 - np.arange(lr) / lr) ** curve])
    for t in times:
        i0 = ns(t) - la
        a, b = max(0, i0), min(n, i0 + len(seg))
        if b > a:
            shp[a:b] = np.maximum(shp[a:b], seg[a - i0:b - i0])
    return shp


def span_shape(n: int, t_on: float, t_off: float, attack_s=0.04, release=0.3) -> np.ndarray:
    """0..1 curve: ramps up before t_on, holds until t_off, ramps down after (smoothstep)."""
    t = tax(n)
    s = np.minimum(np.clip((t - t_on + attack_s) / attack_s, 0, 1), np.clip(1 - (t - t_off) / release, 0, 1))
    return s * s * (3 - 2 * s)


# =========================================================================== dynamics & metering

def lufs_blocks(x: np.ndarray, block=0.4, hop=0.1):
    """BS.1770 K-weighted mean square per block (momentary windows by default)."""
    y = signal.lfilter([1.53512485958697, -2.69169618940638, 1.19839281085285],
                       [1.0, -1.69065929318241, 0.73248077421585], x, axis=-1)
    y = signal.lfilter([1.0, -2.0, 1.0], [1.0, -1.99004745483398, 0.99007225036621], y, axis=-1)
    p = (y ** 2).sum(axis=0)
    cs = np.concatenate([[0.0], np.cumsum(p)])
    b, h = ns(block), ns(hop)
    starts = np.arange(0, len(p) - b + 1, h)
    return starts, (cs[starts + b] - cs[starts]) / b


def lufs(x: np.ndarray) -> float:
    """Integrated loudness (ITU-R BS.1770-4 gating)."""
    _, ms = lufs_blocks(x)
    lv = -0.691 + 10 * np.log10(ms + 1e-20)
    g = ms[lv > -70.0]
    rel = -0.691 + 10 * np.log10(g.mean()) - 10.0
    return float(-0.691 + 10 * np.log10(ms[(lv > -70.0) & (lv > rel)].mean()))


def true_peak_db(x: np.ndarray, os: int = 8) -> float:
    return float(20 * np.log10(np.max(np.abs(signal.resample_poly(x, os, 1, axis=-1))) + 1e-12))


def glue(x, thresh_db=-20.0, ratio=2.0, attack_s=0.02, release=0.2, knee=6.0, block=32):
    """Stereo-linked RMS bus compressor with a soft knee. Returns output and gain reduction (dB)."""
    n = x.shape[1]
    nb = -(-n // block)
    ms = np.pad((x ** 2).mean(axis=0), (0, nb * block - n)).reshape(nb, block).mean(axis=1)
    a = np.exp(-block / (0.012 * SR))
    lvl = 10 * np.log10(signal.lfilter([1 - a], [1, -a], ms) + 1e-12)
    over = lvl - thresh_db
    slope = 1.0 / ratio - 1.0
    gr = np.where(over <= -knee / 2, 0.0,
                  np.where(over >= knee / 2, slope * over, slope * (over + knee / 2) ** 2 / (2 * knee)))
    aa, ar = np.exp(-block / (attack_s * SR)), np.exp(-block / (release * SR))
    g, out = 0.0, np.empty(nb)
    for i, v in enumerate(gr.tolist()):
        g = aa * g + (1 - aa) * v if v < g else ar * g + (1 - ar) * v
        out[i] = g
    gdb = np.interp(np.arange(n), np.arange(nb) * block + block / 2, out)
    return x * db(gdb), gdb


def soft_clip(x, knee_db=-3.5, ceil_db=0.0, os=4):
    """Oversampled soft-knee clipper: linear below the knee, tanh into the ceiling above it.
    It shaves the few-millisecond peaks where hit layers line up, before the limiter."""
    k, c = db(knee_db), db(ceil_db)
    up = signal.resample_poly(x, os, 1, axis=-1)
    a = np.abs(up)
    over = a > k
    up[over] = np.sign(up[over]) * (k + (c - k) * np.tanh((a[over] - k) / (c - k)))
    return signal.resample_poly(up, 1, os, axis=-1)


def limiter(x, ceiling_db=CEILING_DB, lookahead=0.002, hold=0.015, release=0.08, os=4):
    """Look-ahead true-peak brickwall limiter. Peaks are found on a 4x oversampled copy; the gain
    holds (so low notes do not ripple), releases smoothly and is averaged over the look-ahead."""
    c, n = x.shape
    ceil = db(ceiling_db)
    up = signal.resample_poly(x, os, 1, axis=-1)[:, :n * os]
    pk = np.maximum(np.abs(up).reshape(c, n, os).max(axis=(0, 2)), np.abs(x).max(axis=0))
    target = np.minimum(1.0, ceil / np.maximum(pk, 1e-12))
    la, h = ns(lookahead), ns(hold)
    size = la + h + 1  # window [i - h, i + la]
    tmin = minimum_filter1d(target, size, mode="nearest", origin=h - size // 2)
    rc = np.exp(-1.0 / (release * SR))
    g, cur = np.empty(n), 1.0
    for i, v in enumerate(tmin.tolist()):
        cur = 1.0 - (1.0 - cur) * rc
        if v < cur:
            cur = v
        g[i] = cur
    gp = np.concatenate([np.ones(la), g])
    cs = np.concatenate([[0.0], np.cumsum(gp)])
    gain = (cs[la + 1:] - cs[:-la - 1]) / (la + 1)  # mean of g[i - la .. i]
    return x * gain, gain


# =========================================================================== instruments

def kick(seed, f0=150.0, f1=48.0, ptau=0.032, atau=0.2, dur=0.48, click=0.35, drive=1.8):
    """Sine kick with pitch drop, optional click, and soft saturation."""
    n = ns(dur)
    t = tax(n)
    f = f1 + (f0 - f1) * np.exp(-t / ptau)
    y = sine(f, n) * (1.0 - np.exp(-t / 0.0005)) * np.exp(-t / atau)
    if click > 0:
        nz = filt(rng(seed).standard_normal(n), "bandpass", [1800.0, 8000.0])
        y = y + click * nz * np.exp(-t / 0.0018) * (1 - np.exp(-t / 0.0001))
    return norm(np.tanh(drive * y) * fade(n, 0.0, 0.05))


def heartbeat(seed):
    """Soft 'lub-dub' kick."""
    y = np.zeros(ns(0.5))
    lub = kick(seed + ".lub", f0=115.0, f1=45.0, ptau=0.028, atau=0.12, dur=0.34, click=0.0, drive=1.4)
    dub = kick(seed + ".dub", f0=100.0, f1=43.0, ptau=0.022, atau=0.09, dur=0.28, click=0.0, drive=1.3)
    y[:len(lub)] += lub
    o = ns(0.15)
    y[o:o + len(dub)] += 0.5 * dub[:len(y) - o]
    return norm(filt(y, "lowpass", 170.0))


def clap(seed):
    n = ns(0.45)
    t = tax(n)
    r = rng(seed)
    common = r.standard_normal(n)
    out = np.zeros((2, n))
    for ch in range(2):
        nz = 0.75 * common + 0.66 * r.standard_normal(n)
        tone = filt(nz, "bandpass", [850.0, 3200.0]) + 0.25 * filt(nz, "highpass", 5000.0)
        e = np.zeros(n)
        for k, off in enumerate((0.0, 0.0095, 0.0205, 0.031)):
            tt = np.maximum(t - off, 0.0)
            on = (t >= off) * (1 - np.exp(-tt / 0.0003))
            e += on * (np.exp(-tt / 0.0042) if k < 3 else 0.9 * np.exp(-tt / 0.075))
        out[ch] = tone * e
    return norm(out * fade(n, 0.0, 0.05))


def hat(seed, tau=0.028):
    n = ns(0.15)
    t = tax(n)
    r = rng(seed)
    metal = sum(square(f * 1.9, n, r.random()) for f in (205.3, 304.4, 369.6, 522.7, 540.0, 800.0))
    y = 0.55 * metal / 6.0 + 0.45 * r.standard_normal(n)
    y = eq(filt(y, "highpass", 7200.0, 4), "peak", 10500.0, 1.0, 4.0)
    return norm(y * attack(t, 0.0004) * np.exp(-t / tau) * fade(n, 0.0, 0.02))


def shaker(seed):
    n = ns(0.09)
    t = tax(n)
    y = filt(rng(seed).standard_normal(n), "bandpass", [4500.0, 11000.0])
    return norm(y * attack(t, 0.007) ** 2 * np.exp(-np.maximum(t - 0.007, 0) / 0.025) * fade(n, 0, 0.01))


def snare(seed, tone=190.0):
    n = ns(0.32)
    t = tax(n)
    r = rng(seed)
    body = (sine(tone * (1 + 0.35 * np.exp(-t / 0.012)), n) * np.exp(-t / 0.055)
            + 0.5 * sine(tone * 1.73 * (1 + 0.2 * np.exp(-t / 0.01)), n) * np.exp(-t / 0.04))
    nz = eq(filt(r.standard_normal((2, n)), "bandpass", [1500.0, 9500.0]), "peak", 4500.0, 0.8, 3.0)
    nz[1] = 0.6 * nz[0] + 0.8 * nz[1]
    y = 0.7 * stereo(body) + 0.9 * nz * attack(t, 0.0005) * np.exp(-t / 0.085)
    return norm(y * (1 - np.exp(-t / 0.0004)) * fade(n, 0, 0.03))


_PLUCKS: dict = {}


def pluck(f: float, dur=0.55, decay=0.3, bright=1.0) -> np.ndarray:
    """Soft plucked tone: additive partials, higher partials die faster, slight string stiffness."""
    key = (round(f, 4), dur, decay, bright)
    if key not in _PLUCKS:
        n = ns(dur)
        t = tax(n)
        y = np.zeros(n)
        for k in range(1, 33):
            fk = f * k * np.sqrt(1 + 0.00015 * k * k)
            if fk > 15000:
                break
            y += k ** -1.35 * np.sin(TAU * fk * t) * np.exp(-t * (1 + 0.6 * bright * (k - 1)) / decay)
        _PLUCKS[key] = norm(y * attack(t, 0.0015) * fade(n, 0, 0.05))
    return _PLUCKS[key]


def pad_note(f, hold, seed, attack_s=0.3, release=0.5, voices=5, spread=13.0) -> np.ndarray:
    """Detuned PolyBLEP saw ensemble, voices spread across the stereo field, slow pitch drift."""
    n = ns(hold + release)
    t = tax(n)
    r = rng(seed)
    out = np.zeros((2, n))
    pans = np.linspace(-0.9, 0.9, voices)[r.permutation(voices)]
    for v in range(voices):
        cents = (v - (voices - 1) / 2) / ((voices - 1) / 2) * spread + r.uniform(-1.5, 1.5)
        drift = 2.5 * np.sin(TAU * r.uniform(0.07, 0.2) * t + r.uniform(0, TAU))
        out += stereo(saw(f * 2 ** ((cents + drift) / 1200), n, r.random()), pans[v])
    env = np.ones(n)
    a = max(1, ns(attack_s))
    env[:a] = np.sin(0.5 * np.pi * np.arange(a) / a) ** 2
    hs = ns(hold)
    if n > hs:
        env[hs:] *= np.cos(0.5 * np.pi * np.arange(n - hs) / (n - hs)) ** 2
    return out * env / voices


def bass_note(f: float, gate: float, accent: float) -> np.ndarray:
    """Sub sine + filtered saw pair with a short filter envelope."""
    n = ns(gate + 0.03)
    t = tax(n)
    sub = sine(f, n)
    s = saw(f, n, 0.0) + saw(f * 1.004, n, 0.37)
    s = tv_filter(s, "lp", 170.0 + 1500.0 * accent * np.exp(-t / 0.07), 1.1)
    env = attack(t, 0.003) * (0.78 + 0.22 * np.exp(-t / 0.08)) * fade(n, 0.0, 0.03)
    return np.tanh(1.3 * (0.9 * sub + 0.45 * s) * env)


def stab(notes, seed, dur=0.24, atau=0.1, f_open=7500.0, f_close=1300.0, ctau=0.06,
         saws=3, detune=14.0, drive=1.8, width=0.8) -> np.ndarray:
    """Detuned saw chord stab with a snappy filter envelope and saturation."""
    n = ns(dur)
    t = tax(n)
    r = rng(seed)
    out = np.zeros((2, n))
    for note in notes:
        for v in range(saws):
            c = (v - (saws - 1) / 2) * detune + r.uniform(-3, 3)
            p = width * (v - (saws - 1) / 2) / max(1.0, (saws - 1) / 2)
            out += stereo(saw(hz(note) * 2 ** (c / 1200), n, r.random()), p)
    out = tv_filter(out, "lp", f_close + (f_open - f_close) * np.exp(-t / ctau), 0.9)
    out = norm(out * attack(t, 0.0008) * np.exp(-t / atau) * fade(n, 0.0, min(0.04, dur * 0.3)))
    return np.tanh(drive * out) / np.tanh(drive)


def crack(seed, big=False):
    """Bright noise crack for the slam hits."""
    n = ns(0.5 if big else 0.25)
    t = tax(n)
    nz = rng(seed).standard_normal((2, n))
    nz[1] = 0.55 * nz[0] + 0.835 * nz[1]
    y = eq(eq(filt(nz, "bandpass", [1000.0, 14500.0]), "peak", 4200.0, 0.8, 5.0), "hs", 8000.0, 0.7, 2.0)
    e = (1 - np.exp(-t / 0.00012)) * (0.75 * np.exp(-t / 0.014) + 0.25 * np.exp(-t / (0.09 if big else 0.05)))
    return norm(y * e * fade(n, 0, 0.02))


def boom(seed, f0=85.0, f1=38.0, ptau=0.28, atau=0.5, dur=1.8, drive=1.8, lp=380.0):
    """Sub boom: pitch-dropping sine, saturated for small speakers, low-passed, no subsonics."""
    n = ns(dur)
    t = tax(n)
    y = sine(f1 + (f0 - f1) * np.exp(-t / ptau), n) * attack(t, 0.002) * np.exp(-t / atau)
    y = filt(filt(np.tanh(drive * y), "lowpass", lp), "highpass", 28.0)
    return norm(y * fade(n, 0.0, 0.4))


def impact(seed, dur=0.6, tau=0.09, lp=180.0):
    n = ns(dur)
    t = tax(n)
    y = filt(rng(seed).standard_normal(n), "lowpass", lp) * attack(t, 0.001) * np.exp(-t / tau)
    return norm(y * fade(n, 0, 0.05))


def crash(seed, dur=2.8, tau=0.7, hp=350.0):
    """Wide noise crash with metallic partials; brightness decays with the level."""
    n = ns(dur)
    t = tax(n)
    r = rng(seed)
    nz = r.standard_normal((2, n))
    nz[1] = 0.35 * nz[0] + 0.937 * nz[1]
    y = eq(filt(nz, "bandpass", [hp, 15000.0]), "peak", 6500.0, 0.6, 4.0)
    met = np.zeros((2, n))
    for k in range(48):
        met[k % 2] += (np.sin(TAU * r.uniform(2500, 13000) * t + r.uniform(0, TAU)) * r.uniform(0.3, 1.0)
                       * np.exp(-t / (tau * r.uniform(0.5, 1.4))))
    y = norm(y) + 0.35 * norm(met)
    y = tv_filter(y, "lp", 3500.0 + 14000.0 * np.exp(-t / (tau * 0.6)), 0.7, block=64)
    env = attack(t, 0.001) * (0.6 * np.exp(-t / (tau * 0.25)) + 0.4 * np.exp(-t / tau))
    return norm(y * env * fade(n, 0, 0.5))


def cymbal(seed, dur, tau, hp=3000.0):
    n = ns(dur)
    t = tax(n)
    r = rng(seed)
    nz = r.standard_normal((2, n))
    nz[1] = 0.5 * nz[0] + 0.866 * nz[1]
    met = np.zeros((2, n))
    for k in range(40):
        met[k % 2] += np.sin(TAU * r.uniform(3000, 12000) * t + r.uniform(0, TAU)) * r.uniform(0.2, 1.0)
    y = norm(filt(nz, "bandpass", [hp, 13000.0])) + 0.2 * norm(met)
    return norm(y * attack(t, 0.0005) * np.exp(-t / tau))


def reverse_swell(seed, dur):
    """Reversed cymbal: swells from nothing and stops dead at the end."""
    y = cymbal(seed, dur, dur * 0.2)[:, ::-1]
    return y * fade(y.shape[1], 0.05, 0.0)


def drone(dur, f, seed):
    """Low tension drone (sub sine + low-passed saws), swelling in, hard cut at the end."""
    n = ns(dur)
    t = tax(n)
    r = rng(seed)
    body = (saw(2 * f * (1 + 0.0015 * np.sin(TAU * 0.7 * t)), n, r.random())
            + saw(2 * f * 1.004, n, r.random()))
    body = filt(body, "lowpass", 900.0)
    grit = 0.15 * filt(r.standard_normal(n), "bandpass", [80.0, 400.0])
    y = 0.8 * sine(f, n) + 0.35 * body + 0.12 * saw(4 * f * 1.002, n, r.random()) + grit
    return norm(y * (t / dur) ** 1.8 * fade(n, 0.0, 0.004))


def clock_tick(seed, f):
    n = ns(0.08)
    t = tax(n)
    y = modes(t, ((f, 0.004, 1.0), (f * 1.47, 0.0025, 0.6), (f * 0.5, 0.008, 0.35)))
    y += 0.5 * filt(rng(seed).standard_normal(n), "highpass", 2500.0) * np.exp(-t / 0.0008)
    return norm(y * attack(t, 0.0003))


def type_blip(f):
    n = ns(0.03)
    t = tax(n)
    y = square(f, n) * np.exp(-t / 0.007) * attack(t, 0.0008) * fade(n, 0, 0.005)
    return norm(filt(y, "lowpass", 3800.0))


def wind(dur, seed):
    """Wind: two decorrelated noise bands wandering slowly, gentle gusts."""
    n = ns(dur)
    r = rng(seed)
    out = np.zeros((2, n))
    for ch in range(2):
        cen = 600.0 * 2.0 ** (1.1 * smooth_noise(n, 0.8, f"{seed}.c{ch}"))
        out[ch] = (tv_filter(r.standard_normal(n), "bp", cen, 1.6, block=64)
                   * (0.6 + 0.4 * smooth_noise(n, 1.2, f"{seed}.a{ch}")))
    return norm(filt(out, "lowpass", 2500.0)) * fade(n, 0.1, 0.2)


def riser(dur, seed, hold=0.0, n_from=350.0, n_to=9000.0, tone_from=220.0, tone_to=880.0,
          tone=0.45, q=2.2, floor_db=-30.0):
    """Riser: noise through a rising band-pass + a rising detuned saw tone, growing louder."""
    n = ns(dur + hold)
    t = tax(n)
    u = np.clip(t / dur, 0.0, 1.0)
    nz = rng(seed).standard_normal((2, n))
    nz[1] = 0.5 * nz[0] + 0.866 * nz[1]
    noise = norm(tv_filter(nz, "bp", n_from * (n_to / n_from) ** (u ** 1.2), q))
    f = tone_from * (tone_to / tone_from) ** (u ** 1.35) * (1 + 0.004 * np.sin(TAU * (4 + 5 * u) * t))
    tn = tv_filter(saw(f, n, 0.1) + saw(f * 1.006, n, 0.6), "lp", np.minimum(4 * f, 16000.0), 0.8)
    y = noise + tone * stereo(norm(tn))
    return y * db(floor_db * (1 - u) ** 0.9) * fade(n, 0.0, 0.01)


def whoosh(dur, seed, f_lo=500.0, f_hi=3000.0, peak=0.5, q=0.9, air=0.35, sharp=2.0, env=None,
           pan=(0.0, 0.0)):
    """Band-passed noise whoosh; brighter when louder. `env` overrides the bell envelope."""
    n = ns(dur)
    t = tax(n)
    u = t / dur
    if env is None:
        env = np.where(u < peak, np.sin(0.5 * np.pi * u / peak) ** sharp,
                       np.cos(0.5 * np.pi * (u - peak) / (1 - peak)) ** sharp)
    nz = rng(seed).standard_normal((2, n))
    nz[1] = 0.7 * nz[0] + 0.714 * nz[1]
    body = tv_filter(nz, "bp", f_lo * (f_hi / f_lo) ** env, q)
    y = norm(body) + air * norm(filt(nz, "bandpass", [6000.0, 15000.0])) * env
    y = balance(y * env, pan[0] + (pan[1] - pan[0]) * u)
    return norm(y * fade(n, 0.002, 0.004))


def suck(dur, seed, f_from=500.0, f_to=6000.0):
    """Reverse 'suck': rising, brightening noise that stops dead on the downbeat."""
    n = ns(dur)
    t = tax(n)
    u = t / dur
    nz = rng(seed).standard_normal((2, n))
    nz[1] = 0.6 * nz[0] + 0.8 * nz[1]
    y = tv_filter(nz, "bp", f_from * (f_to / f_from) ** (u ** 1.5), 1.2)
    y = norm(y) + 0.4 * norm(filt(nz, "bandpass", [7000.0, 15000.0])) * u ** 3
    return norm(y * np.exp(-(1 - u) * dur / (0.25 * dur)) * fade(n, 0.0, 0.003))


def chime(notes, seed, dur=2.2, index=1.7, ratio=3.5, itau=0.16, atau=0.75, attack_s=0.003,
          sparkle=0.35, detune=0.0009):
    """FM bell. Inharmonic ratio = glassy; the index decays so the tail turns pure."""
    n = ns(dur)
    t = tax(n)
    out = np.zeros((2, n))
    for j, note in enumerate(notes):
        f = hz(note) if isinstance(note, str) else note
        for ch, s in enumerate((-1.0, 1.0)):
            fc = f * (1 + s * detune)
            y = np.sin(TAU * fc * t + index * np.exp(-t / itau) * np.sin(TAU * fc * ratio * t))
            y += sparkle * np.sin(TAU * fc * 4.01 * t) * np.exp(-t / (atau * 0.25))
            out[ch] += 0.85 ** j * y
    return norm(out * attack(t, attack_s) * np.exp(-t / atau) * fade(n, 0.0, 0.2))


def ice_hit(seed):
    """Crystalline, inharmonic freeze shimmer: free-bar modes on E and B (each voice doubled and
    slightly detuned so it beats), a cloud of ice grains, and a bright crack. The dry sound dies in
    about a second; the hall reverb carries the long tail."""
    n = ns(2.0)
    t = tax(n)
    r = rng(seed)
    out = np.zeros((2, n))
    for base, lvl, pan in ((hz("E6"), 1.0, -0.35), (hz("B6"), 0.8, 0.35), (hz("E7"), 0.55, 0.0),
                           (hz("B7"), 0.35, -0.1)):
        for j, rr in enumerate((1.0, 2.756, 5.404, 8.933)):
            f = base * rr
            if f > 16000:
                continue
            tau = 0.55 / (1 + 0.8 * j) * r.uniform(0.8, 1.2)
            for s in (-1.0, 1.0):  # detuned pair -> glassy beating
                y = np.sin(TAU * f * (1 + s * r.uniform(0.0008, 0.002)) * t + r.uniform(0, TAU))
                out += stereo(0.5 * lvl / (1 + 1.2 * j) * y * np.exp(-t / tau), pan + 0.4 * s)
    for _ in range(90):  # ice crystals forming: dense at first, then sparse
        tg = min(r.exponential(0.2), 1.2)
        L = ns(r.uniform(0.003, 0.014))
        gr = np.sin(TAU * r.uniform(3500, 13000) * tax(L)) * np.hanning(L) * r.uniform(0.2, 0.6)
        place(out, stereo(gr * np.exp(-tg / 0.35), r.uniform(-0.95, 0.95)), tg)
    out += 0.9 * filt(filt(r.standard_normal((2, n)), "highpass", 2500.0), "lowpass", 15000.0) * np.exp(-t / 0.008)
    return norm(out * attack(t, 0.0004) * fade(n, 0.0, 0.3))


def frost(seed, dur=0.9):
    """Frost spreading: bright, flickering high noise burst (the freeze hit's presence layer)."""
    n = ns(dur)
    t = tax(n)
    r = rng(seed)
    nz = r.standard_normal((2, n))
    y = eq(filt(nz, "bandpass", [3000.0, 13000.0]), "peak", 7500.0, 1.2, 4.0)
    flicker = 0.55 + 0.45 * np.abs(smooth_noise(n, 70.0, seed + ".f"))
    env = attack(t, 0.002) * (0.7 * np.exp(-t / 0.07) + 0.3 * np.exp(-t / 0.3))
    return norm(y * flicker * env * fade(n, 0.0, 0.1))


def shutter(seed):
    """Camera shutter: mirror-up click, then the curtain click ~58 ms later."""
    n = ns(0.16)
    t = tax(n)
    r = rng(seed)
    nz = filt(r.standard_normal(n), "highpass", 2000.0)
    y = np.zeros(n)
    for off, table, amp in ((0.0, ((1350, .010, 1), (3150, .006, .7), (5400, .004, .5), (190, .02, .6)), 1.0),
                            (0.058, ((1650, .008, 1), (3600, .005, .6), (6100, .003, .4), (240, .015, .4)), 0.7)):
        tt = np.maximum(t - off, 0.0)
        s = (modes(tt, table) + 0.9 * nz * np.exp(-tt / 0.0015)) * attack(tt, 0.0002)
        y += amp * s * (t >= off)
    return norm(y)


def frozen_bed(dur, seed):
    """Suspended bed: high E/B sine cluster with slow beating (air), and a very low E drone (sub).
    Returned separately so each part can be levelled."""
    n = ns(dur)
    t = tax(n)
    r = rng(seed)
    air = np.zeros((2, n))
    for note, lvl in (("E5", 0.5), ("B5", 0.55), ("E6", 0.4), ("B6", 0.3), ("E7", 0.12)):
        f = hz(note)
        for ch, s in enumerate((-1.0, 1.0)):
            air[ch] += (lvl * np.sin(TAU * (f + s * 0.35) * t + r.uniform(0, TAU))
                        * (1 + 0.25 * np.sin(TAU * r.uniform(0.3, 0.8) * t + r.uniform(0, TAU))))
    sub = stereo(sine(hz("E1"), n) + 0.35 * sine(hz("E2"), n) + 0.08 * sine(hz("B2"), n))
    env = fade(n, 0.7, 0.1)
    return norm(air * env), norm(sub * env)


def scan_sweep(c: Cues, seed: str, n_ticks: int = 20):
    """OCR scan: a filtered-noise 'shhhk' that follows the scan line left to right (same easing as
    the picture) + tiny rising ticks when the line passes words."""
    dur = c.scan_end - c.scan_start
    n = ns(dur)
    t = tax(n)
    u = t / dur
    r = rng(seed)
    ease = EASE_INOUT(u)
    x = -80.0 + 2080.0 * ease                      # scan line x (px), as drawn in Demo.tsx
    pan = np.clip(x / 1920.0 * 2 - 1, -1, 1) * 0.85
    nz = r.standard_normal(n)
    cen = 2300.0 * 2.0 ** (1.1 * u)
    shh = norm(tv_filter(nz, "bp", cen, 1.3) + 0.35 * tv_filter(nz, "bp", cen * 2.1, 4.0))
    speed = np.gradient(ease)
    speed /= speed.max()
    sweep = stereo(shh * (0.3 + 0.7 * speed) * fade(n, 0.03, 0.08), pan)
    ticks = np.zeros((2, n))
    for i, px in enumerate(np.sort(r.uniform(150.0, 1780.0, n_ticks))):
        ui = inverse(EASE_INOUT, (px + 80.0) / 2080.0)
        f = 2000.0 * 2.0 ** (px / 1920.0) * (1 + r.uniform(-0.03, 0.03))
        place(ticks, stereo(tiny_tick(f, f"{seed}.t{i}") * r.uniform(0.6, 1.0), (px / 1920.0 * 2 - 1) * 0.85),
              ui * dur)
    return sweep, ticks


def tiny_tick(f, seed, tau=0.0018):
    n = ns(0.03)
    t = tax(n)
    y = np.sin(TAU * f * t) * np.exp(-t / tau)
    y += 0.3 * filt(rng(seed).standard_normal(n), "highpass", 5000.0) * np.exp(-t / 0.0005)
    return norm(y * attack(t, 0.0002))


def key_click(seed, pitch=1.0):
    """Premium mechanical key: crisp leaf click, then a low bottom-out 'thock' ~7 ms later."""
    n = ns(0.12)
    t = tax(n)
    r = rng(seed)
    click = modes(t, ((4300, .0022, .6), (6900, .0015, .4), (2900, .003, .35)), pitch)
    click += 0.7 * filt(r.standard_normal(n), "highpass", 3000.0) * np.exp(-t / 0.0008)
    t2 = np.maximum(t - 0.0075, 0.0)
    thock = modes(t2, ((215, .018, 1.0), (390, .012, .55), (720, .007, .35)), pitch)
    thock += 0.35 * filt(r.standard_normal(n), "bandpass", [250.0, 1400.0]) * np.exp(-t2 / 0.005)
    thock *= attack(t2, 0.0004) * (t >= 0.0075)
    return norm((0.75 * click + thock) * attack(t, 0.0002))


def mouse_click(seed, release_gap=0.075):
    """Mouse button: press click, softer release click."""
    n = ns(0.16)
    t = tax(n)
    nz = filt(rng(seed).standard_normal(n), "highpass", 3500.0)
    y = np.zeros(n)
    for off, sc, amp in ((0.0, 1.0, 1.0), (release_gap, 1.12, 0.45)):
        tt = np.maximum(t - off, 0.0)
        s = modes(tt, ((3700, .0022, .7), (5600, .0015, .5), (8300, .001, .3), (620, .005, .3)), sc)
        s += 0.6 * nz * np.exp(-tt / 0.0007)
        y += amp * s * attack(tt, 0.00015) * (t >= off)
    return norm(y)


CLACK = {
    "dpad": ((1750, .007, 1.0), (3150, .005, .55), (5300, .003, .35), (430, .012, .4)),
    "bumper": ((1150, .009, 1.0), (2450, .006, .6), (4300, .004, .3), (310, .016, .55)),
    "face": ((1500, .006, .9), (2800, .005, .5), (4800, .003, .3), (260, .02, .5)),
}


def clack(kind, seed):
    """Short plastic controller-button clack."""
    n = ns(0.09)
    t = tax(n)
    r = rng(seed)
    table = [(f * (1 + r.uniform(-0.02, 0.02)), tau, a) for f, tau, a in CLACK[kind]]
    y = modes(t, table) + 0.8 * filt(r.standard_normal(n), "bandpass", [1200.0, 7000.0]) * np.exp(-t / 0.0012)
    return norm(y * attack(t, 0.0002) * fade(n, 0, 0.01))


def blip(f, dur=0.16):
    """Tiny pitched UI blip that settles onto its note."""
    n = ns(dur)
    t = tax(n)
    fc = f * (1 - 0.1 * np.exp(-t / 0.01))
    y = sine(fc, n) + 0.15 * sine(2 * fc, n)
    return norm(y * attack(t, 0.0015) * np.exp(-t / 0.05) * fade(n, 0, 0.03))


def zip_stretch(seed, dur=0.26):
    """Selection stretch: accelerating micro-clicks through a rising band-pass + elastic tone."""
    n = ns(dur)
    t = tax(n)
    u = t / dur
    r = rng(seed)
    ph = np.cumsum(60.0 * 4.0 ** u / SR)
    idx = np.nonzero(np.diff(np.floor(ph)) > 0)[0] + 1
    imp = np.zeros(n)
    imp[idx] = r.uniform(0.5, 1.0, len(idx))
    k = ns(0.0012)
    tr = np.convolve(imp, r.standard_normal(k) * np.exp(-np.arange(k) / (0.0003 * SR)))[:n]
    tr = tv_filter(tr, "bp", 1300.0 * 3.2 ** u, 2.5)
    tone = sine(480.0 * 2.4 ** u * (1 + 0.01 * np.sin(TAU * 18 * t)), n)
    y = (norm(tr) + 0.28 * tone) * np.sin(np.pi * u) ** 0.7
    return balance(stereo(norm(y)), -0.15 + 0.4 * u)


def pop(f, seed):
    """Bright, rounded pitched pop."""
    n = ns(0.22)
    t = tax(n)
    fc = f * (1 + 0.6 * np.exp(-t / 0.006))
    y = sine(fc, n) + 0.14 * sine(2 * fc, n) + 0.05 * sine(3 * fc, n)
    y *= attack(t, 0.0006) * np.exp(-t / 0.055) * fade(n, 0, 0.04)
    y += 0.12 * filt(rng(seed).standard_normal(n), "highpass", 4000.0) * np.exp(-t / 0.0006)
    return norm(y)


# =========================================================================== harmony

PAD = {
    "Am9": ["A2", "E3", "G3", "C4", "E4", "B4"],
    "Fmaj9": ["F3", "C4", "E4", "G4", "A4"],
    "Cadd9": ["C3", "G3", "D4", "E4", "G4"],
    "Gsus4": ["G3", "D4", "G4", "C5"],
    "G": ["G3", "D4", "G4", "B4"],
    "Cmaj9": ["C3", "G3", "E4", "B4", "D5"],
}
BASS = {"Fmaj9": "F1", "Cadd9": "C2", "Gsus4": "G1", "G": "G1"}
ARP = {
    "Am9": ["A4", "C5", "E5", "G5", "B5"],
    "Fmaj9": ["F4", "A4", "C5", "E5", "G5"],
    "Cadd9": ["G4", "C5", "D5", "E5", "G5"],
    "Gsus4": ["G4", "C5", "D5", "G5", "C6"],
    "G": ["G4", "B4", "D5", "G5", "B5"],
}
ARP_PATTERN = [0, 2, 1, 3, 2, 4, 3, 1]
HOOK_STABS = [  # bass note climbs through A minor (A, C, E, G, A); the chords climb with it
    ["A2", "A3", "C4", "E4"],
    ["C3", "C4", "E4", "A4"],
    ["E3", "E4", "A4", "C5"],
    ["G3", "G4", "A4", "C5", "E5"],
    ["A3", "A4", "C5", "E5", "A5"],
]
PENTATONIC = ["C5", "D5", "E5", "G5", "A5", "C6", "D6", "E6"]


def chord_plan(c: Cues):
    """(chord, start, end): 2-second bars from the drop, Gsus4 -> G for the build."""
    prog = ["Fmaj9", "Cadd9", "Gsus4"]
    plan, t, i = [], c.drop, 0
    while t < c.build_start - 1e-6:
        e = min(t + c.bar, c.build_start)
        plan.append((prog[min(i, len(prog) - 1)], t, e))
        t, i = e, i + 1
    plan.append(("G", c.build_start, c.collapse))
    return plan


# =========================================================================== arrangement

class Mix:
    def __init__(self, n: int) -> None:
        self.n = n
        self.buses: dict[str, np.ndarray] = {}
        self.kicks: list[float] = []
        self.cues: list[tuple[float, str]] = []

    def bus(self, name: str) -> np.ndarray:
        if name not in self.buses:
            self.buses[name] = np.zeros((2, self.n))
        return self.buses[name]

    def add(self, name: str, sig: np.ndarray, t: float, gain: float = 1.0, pan=0.0) -> None:
        place(self.bus(name), stereo(sig, pan), t, gain)

    def cue(self, t: float, label: str) -> None:
        self.cues.append((t, label))


def arp_run(m: Mix, chord: str, t0: float, t1: float, c: Cues, gain: float) -> None:
    notes = ARP[chord]
    for k, t in enumerate(grid(t0, t1, c.beat / 4)):
        vel = (1.0, 0.62, 0.8, 0.62)[k % 4]
        f = hz(notes[ARP_PATTERN[k % len(ARP_PATTERN)] % len(notes)])
        m.add("arp", pluck(f), t, gain * vel, pan=0.22 if k % 2 else -0.22)


def build_hook(m: Mix, c: Cues) -> None:
    last = len(c.flashes) - 1
    for k, t in enumerate(c.flashes):
        big = k == last
        gate = 0.6 if big else c.flash_len
        th = kick(f"hook.thump{k}", f0=210.0, f1=45.0, ptau=0.024, atau=0.14 if big else 0.085,
                  dur=gate, click=0.0, drive=2.6)
        m.add("sfx", th, t, db(-2.0))
        cr = crack(f"hook.crack{k}", big)
        m.add("sfx", cr, t, db(-7.0))
        m.add("tape", cr, t, db(-12.0))
        st = stab(HOOK_STABS[min(k, len(HOOK_STABS) - 1)], f"hook.stab{k}", dur=gate,
                  atau=0.16 if big else 0.085, f_open=7500.0 + 900.0 * k, ctau=0.08 if big else 0.05)
        m.add("sfx", st, t, db(-8.5 + 0.5 * k))
        m.add("tape", st, t, db(-8.0))
        if big:
            m.add("sfx", boom("hook.sub", f0=90.0, f1=hz("A1"), ptau=0.08, atau=0.3, dur=0.9), t, db(-5.0))
        m.cue(t, f"hook slam {k + 1}")


def build_question(m: Mix, c: Cues) -> None:
    dur = c.game - c.question
    m.add("sfx", drone(dur, hz("A1"), "q.drone"), c.question, db(-5.0))
    m.cue(c.question, "tension: A drone swell in")
    for k, t in enumerate(grid(c.question, c.game, c.beat / 2)):
        tk = clock_tick(f"q.tick{k}", 2500.0 if k % 2 == 0 else 1900.0)
        m.add("sfx", tk, t, db(-8.0 if k == 0 else -11.0), pan=0.15 if k % 2 == 0 else -0.15)
        m.add("room", tk, t, db(-17.0))
        m.cue(t, "clock tick")
    m.add("sfx", reverse_swell("q.swell", dur), c.question, db(-4.0))
    m.cue(c.game, "reverse swell ends (smash cut)")


def build_game(m: Mix, c: Cues) -> None:
    t0, t1 = c.game, c.freeze
    tail = 0.45  # music runs past the freeze so the tape stop has material
    for note in PAD["Am9"]:
        m.add("pad", pad_note(hz(note), t1 - t0 + tail, f"game.pad.{note}", attack_s=0.12, release=0.3), t0,
              db(-7.5))
    arp_run(m, "Am9", t0, t1 + 0.26, c, db(-20.0))
    for k, t in enumerate(grid(t0, t1, c.beat)):
        m.add("world", heartbeat(f"game.heart{k}"), t, db(-10.0))
    m.add("world", wind(t1 - t0 + tail, "game.wind"), t0, db(-24.0))
    m.cue(t0, "game: Am9 pad, arp, heartbeat, wind")
    r = rng("game.type")
    for t in grid(c.type_start, c.type_end, 3 / c.fps):
        if r.random() < 0.18:
            continue
        m.add("world", type_blip(float(r.choice([880.0, 987.8, 1046.5, 1174.7]))), t, db(-31.0),
              pan=r.uniform(-0.1, 0.1))
    m.cue(c.type_start, "typewriter blips start")
    m.add("world", riser(t1 - c.riser_start, "game.riser", hold=0.3, tone_from=hz("A3"), tone_to=hz("A5"),
                         floor_db=-34.0), c.riser_start, db(-12.0))
    m.cue(c.riser_start, "riser into the freeze")
    for k, (t, pan, pitch) in enumerate(zip(c.keys, (-0.45, -0.25, 0.35), (0.96, 1.0, 1.05))):
        kc = key_click(f"key{k}", pitch)
        m.add("sfx", kc, t, db(-10.0), pan=pan)
        m.add("room", kc, t, db(-18.0), pan=pan)
        m.cue(t, ("Ctrl", "Alt", "L")[k % 3] + " key click")


def build_freeze(m: Mix, c: Cues) -> None:
    t = c.freeze
    m.add("sfx", boom("freeze.boom", f0=80.0, f1=38.0, ptau=0.25, atau=0.45, dur=1.6), t, db(-1.0))
    m.add("sfx", impact("freeze.impact"), t, db(-5.0))
    m.add("sfx", frost("freeze.frost"), t, db(-4.5))
    ice = ice_hit("freeze.ice")
    m.add("sfx", ice, t, db(-1.0))
    m.add("hall", ice, t, db(-6.0))
    sh = shutter("freeze.shutter")
    m.add("sfx", sh, t, db(-6.0))
    m.add("room", sh, t, db(-16.0))
    m.cue(t, "FREEZE: tape stop + ice hit + sub boom + shutter")
    air, sub = frozen_bed(c.drop - t, "freeze.bed")
    m.add("sfx", air, t, db(-29.0))
    m.add("sfx", sub, t, db(-27.0))
    m.add("hall", air, t, db(-24.0))
    sweep, ticks = scan_sweep(c, "scan")
    m.add("sfx", sweep, c.scan_start, db(-22.0))
    m.add("sfx", ticks, c.scan_start, db(-21.0))
    m.add("hall", sweep + ticks, c.scan_start, db(-30.0))
    m.cue(c.scan_start, "scan sweep L->R + 20 word ticks")
    dur = c.cursor_arrive - c.cursor_in
    speed = np.gradient(EASE_INOUT(np.linspace(0, 1, ns(dur))))
    m.add("sfx", whoosh(dur, "cursor", f_lo=600.0, f_hi=2600.0, q=1.1, air=0.25,
                        env=(speed / speed.max()) ** 1.3, pan=(0.65, -0.05)), c.cursor_in, db(-22.0))
    m.cue(c.cursor_in, "cursor whoosh")
    mc = mouse_click("click")
    m.add("sfx", mc, c.click, db(-12.0), pan=-0.05)
    m.add("room", mc, c.click, db(-21.0))
    m.cue(c.click, "mouse click")
    pr = riser(c.drop - c.predrop_start, "predrop", n_from=900.0, n_to=7500.0, tone_from=hz("E4"),
               tone_to=hz("E6"), tone=0.3, q=3.0, floor_db=-24.0)
    m.add("sfx", pr, c.predrop_start, db(-14.0))
    m.add("hall", pr, c.predrop_start, db(-30.0))
    m.add("sfx", suck(0.32, "predrop.suck"), c.drop - 0.32, db(-10.0))
    m.cue(c.predrop_start, "pre-drop riser (suck ends on the drop)")


def build_groove(m: Mix, c: Cues) -> None:
    for i, (name, s, e) in enumerate(chord_plan(c)):
        for note in PAD[name]:
            m.add("pad", pad_note(hz(note), e - s, f"pad.{i}.{note}", attack_s=0.02 if i == 0 else 0.06,
                                  release=0.25), s, db(-5.0))
        for k, tb in enumerate(grid(s, e, c.beat / 2)):
            m.add("bass", bass_note(hz(BASS[name]), c.beat / 2 * 0.82, 1.0 if k % 2 else 0.7), tb, db(-7.5))
        arp_run(m, name, s, e, c, db(-18.0))
        m.cue(s, f"chord {name}")
    m.kicks = grid(c.drop, c.collapse, c.beat)
    kk = kick("groove.kick")
    claps = [clap(f"clap{j}") for j in range(2)]
    for i, t in enumerate(m.kicks):
        m.add("drums", kk, t, db(-4.0))
        if i % 2 == 1:
            m.add("drums", claps[(i // 2) % 2], t, db(-11.0))
            m.add("mhall", claps[(i // 2) % 2], t, db(-17.0))
    hats = [hat(f"hat{j}", tau=0.024 + 0.004 * j) for j in range(4)]
    for j, t in enumerate(grid(c.drop + c.beat / 2, c.collapse, c.beat)):
        m.add("drums", hats[j % 4], t, db(-18.0), pan=0.12)
    shakers = [shaker(f"shaker{j}") for j in range(4)]
    for j, t in enumerate(grid(c.drop, c.collapse, c.beat / 4)):
        m.add("drums", shakers[j % 4], t, db(-27.0) * (0.55, 0.35, 0.8, 0.4)[j % 4], pan=-0.25)
    m.cue(c.drop, "DROP: kick, clap 2+4, off-beat hats, shaker, bass, pad, arp")
    roll = grid(c.build_start, c.build_start + c.beat, c.beat / 2) + grid(c.build_start + c.beat, c.collapse, c.beat / 4)
    for j, t in enumerate(roll):
        u = (t - c.build_start) / (c.collapse - c.build_start)
        sn = snare(f"roll{j}", tone=180.0 * (1 + 0.4 * u))
        m.add("drums", sn, t, db(-13.0 + 11.0 * u))
        m.add("mhall", sn, t, db(-20.0 + 6.0 * u))
    m.cue(c.build_start, "build: snare roll 8ths -> 16ths, riser, high-pass sweep")
    m.add("world", riser(c.collapse - c.build_start, "build.riser", hold=0.05, n_from=500.0, n_to=11000.0,
                         tone_from=hz("G3"), tone_to=hz("G5"), tone=0.4, floor_db=-22.0), c.build_start, db(-6.0))


def build_drop_sfx(m: Mix, c: Cues) -> None:
    t = c.drop
    cr = crash("drop.crash", dur=2.6, tau=0.55, hp=1800.0)
    m.add("sfx", cr, t, db(-3.5))
    m.add("hall", cr, t, db(-17.0))
    m.add("sfx", boom("drop.boom", f0=100.0, f1=42.0, ptau=0.1, atau=0.3, dur=1.0), t, db(-2.5))
    m.add("sfx", impact("drop.impact", dur=0.4, tau=0.06, lp=250.0), t, db(-6.0))
    wh = whoosh(0.55, "card.whoosh", f_lo=700.0, f_hi=5200.0, peak=0.22, q=0.8, air=0.5)
    m.add("sfx", wh, t - 0.12, db(-9.0))
    m.add("hall", wh, t - 0.12, db(-18.0))
    ch = chime(["A5", "E6"], "card.chime")
    m.add("sfx", ch, t, db(-9.0))
    m.add("hall", ch, t, db(-9.5))
    m.cue(t, "card: airy whoosh + glass chime (A5+E6) + crash")
    sc = mouse_click("speaker.click", release_gap=0.06)
    m.add("sfx", sc, c.speaker_click, db(-11.0), pan=0.05)
    m.add("room", sc, c.speaker_click, db(-19.0))
    m.cue(c.speaker_click, "speaker click")


def build_controller(m: Mix, c: Cues) -> None:
    # blips sit an octave above the pad and the arp's delay tails, where nothing masks them
    for kind, t, note, pan, label in (("dpad", c.down, "A6", -0.1, "D-pad down"),
                                      ("bumper", c.rb, "C7", 0.25, "RB"),
                                      ("face", c.a, "E7", 0.1, "A")):
        ck = clack(kind, f"btn.{label}")
        m.add("sfx", ck, t, db(-12.0), pan=pan)
        m.add("room", ck, t, db(-20.0), pan=pan)
        bl = blip(hz(note))
        m.add("sfx", bl, t + 0.004, db(-17.0), pan=pan * 0.5)
        m.add("hall", bl, t + 0.004, db(-24.0))
        m.cue(t, f"{label}: clack + blip {note}")
    m.add("sfx", whoosh(0.2, "glide", f_lo=900.0, f_hi=4200.0, peak=0.4, q=1.0, air=0.4), c.down + 0.03, db(-21.0))
    m.cue(c.down + 0.03, "selection glide swish")
    m.add("sfx", zip_stretch("stretch"), c.rb + 0.03, db(-19.0))
    m.cue(c.rb + 0.03, "selection stretch/zip")
    wh = whoosh(0.4, "card2.whoosh", f_lo=800.0, f_hi=4500.0, peak=0.25, q=0.8, air=0.45)
    m.add("sfx", wh, c.a - 0.08, db(-20.0))
    m.add("hall", wh, c.a - 0.08, db(-26.0))
    ch = chime(["G5", "D6"], "card2.chime", dur=1.6, index=1.3, atau=0.55)
    m.add("sfx", ch, c.a, db(-16.0))
    m.add("hall", ch, c.a, db(-15.0))
    m.cue(c.a, "card re-opens: small whoosh + chime (G5+D6)")
    ck = clack("face", "btn.B")
    m.add("sfx", ck, c.b, db(-12.0), pan=0.15)
    m.add("room", ck, c.b, db(-20.0))
    rw = whoosh(c.unfreeze - c.b + 0.12, "dissolve", f_lo=600.0, f_hi=6000.0, peak=0.85, q=0.7, air=0.6)
    m.add("sfx", rw, c.b, db(-16.0))
    m.add("hall", rw, c.b, db(-22.0))
    m.cue(c.b, "B: clack + reverse whoosh; music filter opens")


def build_features(m: Mix, c: Cues) -> None:
    for k, t in enumerate(c.pops):
        note = PENTATONIC[k % len(PENTATONIC)]
        pan = -0.45 if k % 2 == 0 else 0.45
        p = pop(hz(note), f"pop{k}")
        m.add("sfx", p, t, db(-12.0), pan=pan)
        m.add("hall", p, t, db(-21.0), pan=pan)
        m.cue(t, f"pop {note}")
    m.add("sfx", suck(c.logo - c.collapse, "collapse.suck", f_from=300.0, f_to=8000.0), c.collapse, db(-9.0))
    m.cue(c.collapse, "collapse: music cut, suck-in whoosh")


def logo_pad(dur, seed):
    n = ns(dur)
    t = tax(n)
    r = rng(seed)
    out = np.zeros((2, n))
    for note in PAD["Cmaj9"]:
        out += pad_note(hz(note), dur, f"{seed}.{note}", attack_s=0.6, release=0.3, spread=10.0)[:, :n]
    out = tv_filter(out, "lp", 1500.0 + 2200.0 * attack(t, 1.5), 0.7, block=64)
    for note in ("E6", "G6", "B6", "D7"):  # shimmer
        f = hz(note)
        for ch, s in enumerate((-1.0, 1.0)):
            out[ch] += (0.05 * np.sin(TAU * (f + s * 0.6) * t + r.uniform(0, TAU))
                        * (1 + 0.3 * np.sin(TAU * r.uniform(4.0, 7.0) * t + r.uniform(0, TAU))))
    return out * fade(n, 0.5, 0.0) * (1.0 - 0.3 * t / dur)


def build_logo(m: Mix, c: Cues) -> None:
    t = c.logo
    m.add("sfx", boom("logo.boom", f0=110.0, f1=36.0, ptau=0.2, atau=0.8, dur=2.6), t, db(0.0))
    m.add("sfx", kick("logo.kick", f0=200.0, f1=50.0, ptau=0.03, atau=0.25, dur=0.6, click=0.7, drive=2.0), t,
          db(-2.0))
    m.add("sfx", impact("logo.impact", dur=0.8, tau=0.12, lp=220.0), t, db(-6.0))
    cr = crash("logo.crash", dur=3.0, tau=0.9, hp=300.0)
    m.add("sfx", cr, t, db(0.0))
    m.add("big", cr, t, db(-7.0))
    st = stab(PAD["Cmaj9"] + ["G5"], "logo.stab", dur=2.2, atau=0.55, f_open=11000.0, f_close=2400.0, ctau=0.2,
              saws=5, detune=16.0, drive=1.3)
    m.add("sfx", st, t, db(-1.5))
    m.add("big", st, t, db(-1.0))
    lp = logo_pad(c.total - t, "logo.pad")
    m.add("sfx", lp, t + 0.05, db(-15.0))
    m.add("big", lp, t + 0.05, db(-15.0))
    m.cue(t, "LOGO HIT: sub boom + crash + Cmaj9 stab; Cmaj9 pad rings")
    tk = tiny_tick(4200.0, "select", tau=0.003)
    m.add("sfx", tk, c.select, db(-9.0))
    m.add("room", tk, c.select, db(-17.0))
    m.cue(c.select, "selection tick")
    r = rng("decode")
    for k, tt in enumerate(np.sort(r.uniform(c.decode, c.decode_end - 0.02, 30))):
        m.add("sfx", tiny_tick(r.uniform(2600.0, 6200.0), f"dec{k}"), float(tt), db(-24.0 + r.uniform(-3, 2)),
              pan=r.uniform(-0.3, 0.3))
    m.cue(c.decode, "decode scramble ticks")
    ch = chime(["C5", "G5", "E6"], "resolve", dur=2.0, index=0.9, ratio=2.0, itau=0.3, atau=1.0, attack_s=0.006,
               sparkle=0.08)
    m.add("sfx", ch, c.decode_end, db(-17.0))
    m.add("hall", ch, c.decode_end, db(-17.0))
    m.cue(c.decode_end, "warm resolution chime (C5+G5+E6)")


def load_voice(path: Path, c: Cues):
    """Loads the TTS clip, finds the speech onset/end, cleans it up (HPF 120 Hz, presence)."""
    sr, raw = wavfile.read(path)
    v = raw.astype(np.float64)
    if raw.dtype.kind in "iu":
        v /= float(np.iinfo(raw.dtype).max) + 1.0
    if v.ndim == 2:
        v = v.mean(axis=1)
    if sr != SR:
        v = signal.resample_poly(v, SR, sr)
    w = ns(0.005)
    env = np.sqrt(np.convolve(v ** 2, np.ones(w) / w, "same"))
    idx = np.nonzero(env > env.max() * db(-35.0))[0]
    onset, end = idx[0] / SR, idx[-1] / SR
    v = eq(eq(filt(v, "highpass", 120.0), "peak", 3200.0, 0.9, 3.0), "hs", 8000.0, 0.7, 1.5)
    # gentle 3:1 levelling so the loudest syllable does not reach the master clipper
    top = 10 * np.log10(np.convolve(v ** 2, np.ones(ns(0.01)) / ns(0.01), "same").max())
    v = glue(v[None, :], thresh_db=top - 8.0, ratio=3.0, attack_s=0.003, release=0.08, knee=4.0, block=16)[0][0]
    v *= fade(len(v), 0.004, 0.03)
    return v, c.voice - onset, (c.voice, c.voice + end - onset), onset


# =========================================================================== mix & master

def render(c: Cues, voice_path: Path):
    m = Mix(c.n)
    build_hook(m, c)
    build_question(m, c)
    build_game(m, c)
    build_freeze(m, c)
    build_groove(m, c)
    build_drop_sfx(m, c)
    build_controller(m, c)
    build_features(m, c)
    build_logo(m, c)
    n = c.n
    t = tax(n)

    # ---- music bus: pad bloom, ping-pong arp, reverb, sidechain, tape stop, filters, cut, voice duck
    pad_fc = np.where(t < c.drop - 0.5, 380.0 * (3000.0 / 380.0) ** (attack(t - c.game, 0.8) ** 0.7), 3200.0)
    pad = tv_filter(m.bus("pad"), "lp", pad_fc, 0.8, block=64)
    arp = m.bus("arp")
    arp = arp + db(-8.0) * pingpong(arp, 3 * c.beat / 4)
    mhall = reverb(m.bus("mhall") + db(-8.0) * pad + db(-10.0) * arp + db(-14.0) * m.bus("world"),
                   rt60=1.8, rt60_hf=0.55, predelay=0.02, size=1.0, er=0.3, hp=220.0, lp=8500.0, seed="mhall")
    sc = duck_shape(n, m.kicks)
    music = (pad * (1 - 0.55 * sc) + arp * (1 - 0.35 * sc) + m.bus("bass") * (1 - 0.8 * sc)
             + mhall * (1 - 0.5 * sc) + m.bus("drums") + m.bus("world"))
    music = tape_stop(music, c.freeze, 0.35, c.drop)
    # 'glass' low-pass while the card UI is up, opening wide over 0.5 s when B is pressed
    u = np.clip((t - c.drop) / (c.b - c.drop), 0, 1)
    v = attack(t - c.b, 0.5)
    glass = np.where(t < c.b, 6000.0 * (3400.0 / 6000.0) ** u, 3400.0 * (23000.0 / 3400.0) ** (v * v * (3 - 2 * v)))
    music = tv_filter(music, "lp", np.where(t < c.drop - 0.3, 23000.0, glass), 0.9, block=64)
    # high-pass sweep up through the build
    hp_u = np.clip((t - c.build_start) / (c.collapse - c.build_start), 0, 1)
    music = tv_filter(music, "hp", 20.0 * (380.0 / 20.0) ** (hp_u ** 1.5), 0.75, block=64)
    gain = np.ones(n)
    ic, il, fl = ns(c.collapse), ns(c.logo), ns(0.012)
    gain[ic - fl:ic] = np.cos(0.5 * np.pi * np.arange(1, fl + 1) / fl) ** 2
    gain[ic:il] = 0.0

    # ---- voice: onset aligned to the cue, music ducked 6 dB under it, level set against the music
    vsig, v_t0, (v_on, v_off), v_onset = load_voice(ROOT / voice_path, c)
    gain *= 1.0 - (1.0 - db(-6.0)) * span_shape(n, v_on, v_off, attack_s=0.05, release=0.22)
    music *= gain * db(-1.0)  # music trim: the hits sit on top of the groove
    voice = np.zeros((2, n))
    place(voice, stereo(vsig), v_t0)
    a, b = ns(v_on), ns(v_off)
    _, mv = lufs_blocks(voice[:, a:b], block=(b - a) / SR, hop=1.0)
    _, mm = lufs_blocks(music[:, a:b], block=(b - a) / SR, hop=1.0)
    voice *= db(10 * np.log10(mm[0] / mv[0]) + 4.5)  # voice sits 4.5 LU above the ducked music
    room = reverb(m.bus("room") + db(-13.0) * voice, rt60=0.4, rt60_hf=0.22, predelay=0.005, size=0.45,
                  er=0.6, hp=200.0, lp=8000.0, seed="room")

    # ---- sfx returns
    hall = reverb(m.bus("hall"), rt60=2.4, rt60_hf=0.9, predelay=0.018, size=1.1, er=0.3, hp=180.0, lp=10000.0,
                  seed="hall")
    big = reverb(m.bus("big"), rt60=4.2, rt60_hf=1.5, predelay=0.025, size=1.4, er=0.3, hp=140.0, lp=9000.0,
                 seed="big")
    tape = reverb(m.bus("tape"), rt60=0.9, rt60_hf=0.3, predelay=0.01, size=0.6, hp=250.0, lp=6000.0, seed="tape")
    tape = np.tanh(2.0 * wow(tape)) / 2.0
    sfx = m.bus("sfx") + hall + big + tape + room
    m.cue(v_on, f"voice 'reach' onset (file onset {v_onset * 1000:.0f} ms)")
    return music, sfx, voice, sorted(m.cues)


def master(mix: np.ndarray, c: Cues):
    """Mono lows, glue compression, loudness to -14 LUFS, true-peak limiting, fades."""
    y = filt(mix, "highpass", 22.0, 4)  # DC and subsonic rumble
    mid, side = 0.5 * (y[0] + y[1]), filt(0.5 * (y[0] - y[1]), "highpass", 110.0)
    y = np.stack([mid + side, mid - side])
    y *= db(-18.0 - lufs(y))
    y, _ = glue(y, thresh_db=-20.0, ratio=2.0, attack_s=0.02, release=0.2)
    edges = fade(c.n, 0.001, 0.5)
    g = TARGET_LUFS - lufs(y)
    for _ in range(8):
        pre = y * db(g)
        clipped = soft_clip(pre)
        out, lim = limiter(clipped)
        out *= edges
        err = TARGET_LUFS - lufs(out)
        if abs(err) < 0.02:
            break
        g += err
    clip_db = 20 * np.log10(np.abs(pre).max() / np.abs(clipped).max())
    return out, lim, clip_db


def write_wav24(path: Path, x: np.ndarray) -> None:
    q = np.clip(np.round(x.T * 8388607.0), -8388608, 8388607).astype("<i4")
    data = np.ascontiguousarray(q).view(np.uint8).reshape(-1, 4)[:, :3].tobytes()
    ch = x.shape[0]
    header = (b"RIFF" + struct.pack("<I", 36 + len(data)) + b"WAVE"
              + b"fmt " + struct.pack("<IHHIIHH", 16, 1, ch, SR, SR * ch * 3, ch * 3, 24)
              + b"data" + struct.pack("<I", len(data)))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(header + data)


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate the InstructMe showreel soundtrack.")
    ap.add_argument("--out", default=str(ROOT / "public" / "soundtrack.wav"))
    ap.add_argument("--voice", default="public/voice/reach.wav")
    ap.add_argument("--stems", default=None, help="optional folder for pre-master stems and the cue list")
    args = ap.parse_args()
    t_start = time.time()
    c = Cues(load_timeline())
    music, sfx, voice, cues = render(c, Path(args.voice))
    out, lim, clip_db = master(music + sfx + voice, c)
    out_path = Path(args.out)
    write_wav24(out_path, out)
    if args.stems:
        d = Path(args.stems)
        d.mkdir(parents=True, exist_ok=True)
        for name, x in (("music", music), ("sfx", sfx), ("voice", voice)):
            wavfile.write(d / f"{name}.wav", SR, x.T.astype(np.float32))
        np.save(d / "limiter_gain.npy", lim.astype(np.float32))
        (d / "cues.json").write_text(json.dumps([{"t": round(t, 4), "cue": s} for t, s in cues], indent=1))
    print(f"soundtrack: {out_path}")
    print(f"  {SR} Hz, 2 ch, 24-bit, {out.shape[1]} samples = {out.shape[1] / SR:.3f} s")
    print(f"  loudness {lufs(out):.2f} LUFS integrated, true peak {true_peak_db(out):.2f} dBTP (internal meter)")
    print(f"  peak shaved by clipper {clip_db:.1f} dB, max limiter reduction {-20 * np.log10(lim.min()):.1f} dB, "
          f"built in {time.time() - t_start:.1f} s")
    for t, s in cues:
        print(f"  {t:7.3f} s  {s}")


if __name__ == "__main__":
    main()
