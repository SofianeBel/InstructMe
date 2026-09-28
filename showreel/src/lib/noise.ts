import {random} from 'remotion';

const hash = (seed: string, i: number) => random(`${seed}:${i}`);

/** Smooth 1D value noise in [0, 1]. */
const noise1 = (seed: string, x: number) => {
  const i = Math.floor(x);
  const f = x - i;
  const u = f * f * (3 - 2 * f);
  return hash(seed, i) * (1 - u) + hash(seed, i + 1) * u;
};

/** Fractal noise in [0, 1]. */
export const fbm = (seed: string, x: number, octaves = 5) => {
  let sum = 0;
  let amp = 1;
  let freq = 1;
  let norm = 0;
  for (let o = 0; o < octaves; o++) {
    sum += amp * noise1(`${seed}${o}`, x * freq);
    norm += amp;
    amp *= 0.5;
    freq *= 2.03;
  }
  return sum / norm;
};

/** A closed SVG path for a mountain range: a noisy ridge line down to `bottom`. */
export const ridgePath = ({
  seed,
  baseY,
  amp,
  scale,
  ridged = 0.55,
  bottom = 1080,
  width = 1920,
  step = 5,
}: {
  seed: string;
  baseY: number;
  amp: number;
  scale: number;
  ridged?: number;
  bottom?: number;
  width?: number;
  step?: number;
}) => {
  const points: string[] = [];
  for (let x = -40; x <= width + 40; x += step) {
    const smooth = fbm(seed, x / scale);
    const sharp = 1 - Math.abs(2 * fbm(`${seed}r`, x / (scale * 0.8), 4) - 1);
    const v = smooth * (1 - ridged) + sharp * ridged;
    points.push(`${x},${(baseY - (v - 0.35) * amp).toFixed(1)}`);
  }
  return `M-40,${bottom} L${points.join(' L')} L${width + 40},${bottom} Z`;
};
