import {Easing, interpolate, spring, type SpringConfig} from 'remotion';

export const CLAMP = {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'} as const;

export const EASE = {
  out: Easing.bezier(0.16, 1, 0.3, 1),
  inOut: Easing.bezier(0.65, 0, 0.35, 1),
  in: Easing.bezier(0.55, 0, 1, 0.45),
  soft: Easing.bezier(0.33, 1, 0.68, 1),
};

/** 0 → 1 between frames `a` and `b`. */
export const ramp = (f: number, a: number, b: number, easing = EASE.out) =>
  interpolate(f, [a, b], [0, 1], {...CLAMP, easing});

export const lerp = (a: number, b: number, t: number) => a + (b - a) * t;

/** A spring that starts at frame `start`. It can overshoot 1. */
export const pop = (f: number, start: number, config: Partial<SpringConfig> = {damping: 13, stiffness: 170, mass: 0.7}) =>
  f < start ? 0 : spring({frame: f - start, fps: 60, config});

export type Cam = {x: number; y: number; z: number};

/** Interpolates camera keyframes; each segment eases in and out. */
export const camAt = (f: number, keys: readonly (readonly [number, Cam])[]): Cam => {
  if (f <= keys[0][0]) return keys[0][1];
  for (let i = 0; i < keys.length - 1; i++) {
    const [f0, a] = keys[i];
    const [f1, b] = keys[i + 1];
    if (f <= f1) {
      const t = ramp(f, f0, f1, EASE.inOut);
      // Zoom is interpolated in log space so push-ins feel linear to the eye.
      return {x: lerp(a.x, b.x, t), y: lerp(a.y, b.y, t), z: Math.exp(lerp(Math.log(a.z), Math.log(b.z), t))};
    }
  }
  return keys[keys.length - 1][1];
};

/** Deterministic hand-held shake that decays after `start`. */
export const shake = (f: number, start: number, length: number, amp: number) => {
  if (f < start || f > start + length) return {x: 0, y: 0};
  const k = 1 - (f - start) / length;
  const a = amp * k * k;
  return {x: Math.sin(f * 2.1) * a, y: Math.cos(f * 2.9) * a * 0.8};
};
