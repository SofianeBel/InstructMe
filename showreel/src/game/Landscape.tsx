import React, {useMemo} from 'react';
import {random} from 'remotion';
import {fbm, ridgePath} from '../lib/noise';

// A painted dusk landscape for a fictional fantasy RPG. Everything moves with the game clock `t`,
// so when the screen freezes, the embers, fog and beacon stop exactly where they are.

const SUN = {x: 1110, y: 468};

type Ridge = {seed: string; baseY: number; amp: number; scale: number; ridged: number};
const FAR: Ridge = {seed: 'far', baseY: 520, amp: 150, scale: 250, ridged: 0.65};
const MID: Ridge = {seed: 'mid', baseY: 598, amp: 170, scale: 210, ridged: 0.6};
const NEAR: Ridge = {seed: 'near', baseY: 688, amp: 105, scale: 300, ridged: 0.45};
const FRONT: Ridge = {seed: 'front', baseY: 880, amp: 150, scale: 170, ridged: 0.35};

const ridgeY = (r: Ridge, x: number) => {
  const smooth = fbm(r.seed, x / r.scale);
  const sharp = 1 - Math.abs(2 * fbm(`${r.seed}r`, x / (r.scale * 0.8), 4) - 1);
  return r.baseY - (smooth * (1 - r.ridged) + sharp * r.ridged - 0.35) * r.amp;
};

const OUTPOST_X = 1478;
const FIGURE_X = 1768;

const STARS = Array.from({length: 80}, (_, i) => ({
  x: random(`star-x${i}`) * 1920,
  y: random(`star-y${i}`) * 330,
  r: 0.6 + random(`star-r${i}`) * 1.3,
  phase: random(`star-p${i}`) * Math.PI * 2,
}));

const EMBERS = Array.from({length: 46}, (_, i) => ({
  x: random(`em-x${i}`) * 2000 - 40,
  speed: 0.45 + random(`em-s${i}`) * 1.2,
  offset: random(`em-o${i}`) * 1200,
  size: 1.4 + random(`em-r${i}`) * 3.2,
  phase: random(`em-p${i}`) * Math.PI * 2,
  sway: 12 + random(`em-w${i}`) * 30,
}));

const CLOUDS = [
  {x: 380, y: 205, rx: 420, ry: 24, o: 0.55},
  {x: 1520, y: 150, rx: 460, ry: 20, o: 0.45},
  {x: 900, y: 300, rx: 330, ry: 15, o: 0.6},
  {x: 1730, y: 338, rx: 280, ry: 13, o: 0.5},
  {x: 220, y: 372, rx: 250, ry: 12, o: 0.5},
  {x: 1260, y: 392, rx: 360, ry: 10, o: 0.55},
];

const RAYS = [-62, -38, -18, -2, 14, 33, 58, 84, 118, 152, 196, 232];

export const Landscape: React.FC<{t: number}> = ({t}) => {
  const paths = useMemo(
    () => ({
      far: ridgePath(FAR),
      mid: ridgePath(MID),
      near: ridgePath(NEAR),
      front: ridgePath(FRONT),
      outpostY: ridgeY(NEAR, OUTPOST_X),
      figureY: ridgeY(FRONT, FIGURE_X),
    }),
    [],
  );

  const beacon = 0.72 + 0.28 * Math.sin(t * 0.16) * Math.sin(t * 0.047 + 1);
  const flutter = Math.sin(t * 0.11) * 5;

  return (
    <svg width={1920} height={1080} viewBox="0 0 1920 1080" style={{position: 'absolute', inset: 0}}>
      <defs>
        <linearGradient id="sky" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#0a1330" />
          <stop offset="0.24" stopColor="#1c2c58" />
          <stop offset="0.4" stopColor="#4a4178" />
          <stop offset="0.5" stopColor="#9a5f7c" />
          <stop offset="0.57" stopColor="#e08a64" />
          <stop offset="0.62" stopColor="#f6b774" />
          <stop offset="0.72" stopColor="#ffd9a0" />
        </linearGradient>
        <radialGradient id="sunGlow" cx={SUN.x} cy={SUN.y} r={760} gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor="#fff3d6" stopOpacity="1" />
          <stop offset="0.06" stopColor="#ffe0a8" stopOpacity="0.85" />
          <stop offset="0.2" stopColor="#ffb27a" stopOpacity="0.42" />
          <stop offset="0.45" stopColor="#e07a7a" stopOpacity="0.14" />
          <stop offset="1" stopColor="#e07a7a" stopOpacity="0" />
        </radialGradient>
        <radialGradient id="rayFill" cx={SUN.x} cy={SUN.y} r={1150} gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor="#fff2d8" stopOpacity="0.16" />
          <stop offset="1" stopColor="#fff2d8" stopOpacity="0" />
        </radialGradient>
        <linearGradient id="cloud" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#ffd2b0" stopOpacity="0.05" />
          <stop offset="1" stopColor="#ffb08a" stopOpacity="0.9" />
        </linearGradient>
        <linearGradient id="far" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0.35" stopColor="#c7909c" />
          <stop offset="0.75" stopColor="#8f6c8e" />
        </linearGradient>
        <linearGradient id="mid" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0.4" stopColor="#6d5380" />
          <stop offset="0.8" stopColor="#443660" />
        </linearGradient>
        <linearGradient id="near" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0.55" stopColor="#35284e" />
          <stop offset="0.9" stopColor="#1d1630" />
        </linearGradient>
        <linearGradient id="front" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0.7" stopColor="#140f1f" />
          <stop offset="1" stopColor="#07050c" />
        </linearGradient>
        <linearGradient id="fog" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#f3b9a4" stopOpacity="0" />
          <stop offset="0.5" stopColor="#f3b9a4" stopOpacity="0.34" />
          <stop offset="1" stopColor="#f3b9a4" stopOpacity="0" />
        </linearGradient>
        <linearGradient id="fogCool" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#8f7fb4" stopOpacity="0" />
          <stop offset="0.5" stopColor="#8f7fb4" stopOpacity="0.3" />
          <stop offset="1" stopColor="#8f7fb4" stopOpacity="0" />
        </linearGradient>
        <radialGradient id="ember">
          <stop offset="0" stopColor="#ffd27a" stopOpacity="0.9" />
          <stop offset="1" stopColor="#ff7a2a" stopOpacity="0" />
        </radialGradient>
        <radialGradient id="beaconGlow">
          <stop offset="0" stopColor="#ffe6a8" stopOpacity="1" />
          <stop offset="0.3" stopColor="#ffb04a" stopOpacity="0.55" />
          <stop offset="1" stopColor="#ff7a2a" stopOpacity="0" />
        </radialGradient>
        <radialGradient id="vignette" cx="0.5" cy="0.45" r="0.75">
          <stop offset="0.55" stopColor="#000" stopOpacity="0" />
          <stop offset="1" stopColor="#000" stopOpacity="0.55" />
        </radialGradient>
        <filter id="rayBlur" x="-10%" y="-10%" width="120%" height="120%">
          <feGaussianBlur stdDeviation="7" />
        </filter>
        <filter id="cloudBlur" x="-20%" y="-200%" width="140%" height="500%">
          <feGaussianBlur stdDeviation="9" />
        </filter>
      </defs>

      <rect width={1920} height={1080} fill="url(#sky)" />

      {STARS.map((s, i) => (
        <circle
          key={i}
          cx={s.x}
          cy={s.y}
          r={s.r}
          fill="#dfe8ff"
          opacity={(0.25 + 0.45 * (0.5 + 0.5 * Math.sin(t * 0.07 + s.phase))) * (1 - s.y / 380)}
        />
      ))}

      <circle cx={SUN.x} cy={SUN.y} r={760} fill="url(#sunGlow)" />

      <g style={{mixBlendMode: 'screen'}} filter="url(#rayBlur)">
        {RAYS.map((a, i) => {
          const angle = ((a + t * 0.02) * Math.PI) / 180;
          const w = (i % 3 === 0 ? 3.6 : 2) * (Math.PI / 180);
          const len = 1500;
          return (
            <polygon
              key={a}
              points={`${SUN.x},${SUN.y} ${SUN.x + Math.cos(angle - w) * len},${SUN.y + Math.sin(angle - w) * len} ${SUN.x + Math.cos(angle + w) * len},${SUN.y + Math.sin(angle + w) * len}`}
              fill="url(#rayFill)"
            />
          );
        })}
      </g>

      <circle cx={SUN.x} cy={SUN.y} r={44} fill="#fff7e4" />

      <g filter="url(#cloudBlur)">
        {CLOUDS.map((c, i) => (
          <ellipse key={i} cx={c.x + t * 0.14 * (1 + i * 0.15)} cy={c.y} rx={c.rx} ry={c.ry} fill="url(#cloud)" opacity={c.o} />
        ))}
      </g>

      <path d={paths.far} fill="url(#far)" opacity={0.92} />
      <rect x={-200 + ((t * 0.35) % 200)} y={470} width={2400} height={170} fill="url(#fog)" />
      <path d={paths.mid} fill="url(#mid)" />
      <rect x={-200 + ((t * 0.55) % 200)} y={580} width={2400} height={150} fill="url(#fogCool)" />

      {/* The outpost from the dialogue, with a beacon fire. */}
      <g transform={`translate(${OUTPOST_X} ${paths.outpostY + 6}) scale(1.15)`}>
        <circle cx={0} cy={-176} r={70} fill="url(#beaconGlow)" opacity={beacon} />
        <path d="M-78,0 L-78,-78 L-70,-100 L-62,-78 L-62,-44 L-20,-44 L-20,-132 L0,-168 L20,-132 L20,-44 L34,-44 L34,-92 L44,-114 L54,-92 L54,-44 L96,-44 L96,0 Z" fill="#1b1430" />
        <path d="M-62,-44 L-62,-52 L-54,-52 L-54,-44 M-44,-44 L-44,-52 L-36,-52 L-36,-44 M60,-44 L60,-52 L68,-52 L68,-44 M78,-44 L78,-52 L86,-52 L86,-44" fill="#1b1430" stroke="#1b1430" strokeWidth={2} />
        <rect x={-5} y={-118} width={10} height={14} rx={2} fill="#ffc978" opacity={0.75 + 0.25 * Math.sin(t * 0.3)} />
        <rect x={-4} y={-82} width={8} height={11} rx={2} fill="#ffc978" opacity={0.8} />
        <rect x={40} y={-80} width={7} height={10} rx={2} fill="#ffc978" opacity={0.65 + 0.3 * Math.sin(t * 0.22 + 2)} />
        <rect x={-73} y={-66} width={6} height={9} rx={2} fill="#ffc978" opacity={0.7} />
        <circle cx={0} cy={-176} r={4.5} fill="#fff4d0" />
      </g>

      <path d={paths.near} fill="url(#near)" />
      <rect x={-200 + ((t * 0.8) % 200)} y={700} width={2400} height={140} fill="url(#fogCool)" opacity={0.8} />

      <path d={paths.front} fill="url(#front)" />

      {/* A lone ranger looking toward the outpost. */}
      <g transform={`translate(${FIGURE_X} ${paths.figureY + 4})`} fill="#06040b">
        <path d={`M-30,0 C-27,-58 -24,-104 -15,-138 L15,-138 C24,-104 30,-58 ${38 + flutter},0 Z`} />
        <path d={`M-24,-126 C-38,-104 -46,-76 ${-48 + flutter * 0.6},-30 L-30,-36 C-30,-78 -22,-108 -12,-130 Z`} />
        <path d="M-15,-138 C-17,-160 -8,-178 2,-178 C13,-178 19,-160 15,-138 Z" />
        <path d="M29,-208 L33,-226 L37,-208 Z" />
        <rect x={31} y={-210} width={3.5} height={210} rx={1.5} transform="rotate(1.5 32 0)" />
      </g>

      {EMBERS.map((e, i) => {
        const y = 1120 - ((t * e.speed * 1.35 + e.offset) % 1250);
        const x = e.x + Math.sin(t * 0.021 * e.speed + e.phase) * e.sway;
        return (
          <g key={i} opacity={Math.min(1, (1120 - y) / 160) * 0.95}>
            <circle cx={x} cy={y} r={e.size * 4.5} fill="url(#ember)" opacity={0.45} />
            <circle cx={x} cy={y} r={e.size * 0.6} fill="#ffe2a6" />
          </g>
        );
      })}

      <rect width={1920} height={1080} fill="url(#vignette)" />
    </svg>
  );
};
