import React from 'react';
import {AbsoluteFill, useCurrentFrame} from 'remotion';
import {COLOR} from '../theme';

/** Film grain: animated noise, blended softly over the whole picture. */
export const Grain: React.FC<{opacity?: number}> = ({opacity = 0.07}) => {
  const frame = useCurrentFrame();
  return (
    <AbsoluteFill style={{pointerEvents: 'none', mixBlendMode: 'overlay', opacity}}>
      <svg width="100%" height="100%">
        <filter id="grain">
          <feTurbulence type="fractalNoise" baseFrequency="0.85" numOctaves={2} seed={frame % 30} stitchTiles="stitch" />
          <feColorMatrix type="saturate" values="0" />
        </filter>
        <rect width="100%" height="100%" filter="url(#grain)" />
      </svg>
    </AbsoluteFill>
  );
};

/** Deep night background with slow blue and violet light. */
export const Aurora: React.FC<{t: number; strength?: number}> = ({t, strength = 1}) => {
  const a = t * 0.012;
  const x1 = 560 + Math.sin(a) * 260;
  const y1 = 380 + Math.cos(a * 1.3) * 120;
  const x2 = 1380 + Math.cos(a * 0.9) * 240;
  const y2 = 700 + Math.sin(a * 1.1) * 140;
  const x3 = 1000 + Math.sin(a * 1.7 + 2) * 300;
  const y3 = 180 + Math.cos(a * 0.8) * 90;
  return (
    <AbsoluteFill
      style={{
        backgroundColor: COLOR.night,
        backgroundImage: [
          `radial-gradient(900px 640px at ${x1}px ${y1}px, rgba(10,132,255,${0.42 * strength}), transparent 70%)`,
          `radial-gradient(820px 700px at ${x2}px ${y2}px, rgba(124,92,255,${0.36 * strength}), transparent 70%)`,
          `radial-gradient(700px 420px at ${x3}px ${y3}px, rgba(64,216,255,${0.2 * strength}), transparent 70%)`,
          'radial-gradient(1400px 900px at 50% 120%, rgba(10,132,255,0.12), transparent 70%)',
        ].join(', '),
      }}
    />
  );
};

/** Darkens the corners to hold the eye in the middle. */
export const Vignette: React.FC<{opacity?: number; color?: string}> = ({opacity = 0.55, color = '0,0,0'}) => (
  <AbsoluteFill
    style={{
      pointerEvents: 'none',
      background: `radial-gradient(ellipse 75% 70% at 50% 48%, rgba(${color},0) 55%, rgba(${color},${opacity}) 100%)`,
    }}
  />
);
