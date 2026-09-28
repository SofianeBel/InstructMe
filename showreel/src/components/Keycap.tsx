import React from 'react';
import {COLOR, FONT} from '../theme';

/** A dark gaming keycap. `press` 0 → 1 pushes it down and lights its blue underglow. */
export const Keycap: React.FC<{label: string; width: number; height?: number; press: number; fontSize?: number; style?: React.CSSProperties}> = ({
  label,
  width,
  height = 150,
  press,
  fontSize = 46,
  style,
}) => {
  const depth = height * 0.11;
  return (
    <div style={{position: 'relative', width, height: height + depth, ...style}}>
      <div
        style={{
          position: 'absolute',
          left: 0,
          right: 0,
          bottom: 0,
          height,
          borderRadius: height * 0.18,
          background: 'linear-gradient(180deg, #1a1d25, #0a0b10)',
          boxShadow: `0 ${depth * 1.6}px ${depth * 3}px rgba(0,0,0,0.6), 0 0 ${24 + press * 60}px rgba(10,132,255,${0.1 + press * 0.75})`,
        }}
      />
      <div
        style={{
          position: 'absolute',
          left: height * 0.03,
          right: height * 0.03,
          top: press * depth,
          height: height - height * 0.02,
          borderRadius: height * 0.16,
          background: `linear-gradient(180deg, ${press > 0.5 ? '#3a4458' : '#3b3f4b'} 0%, #22252e 100%)`,
          border: '1px solid rgba(255,255,255,0.09)',
          boxShadow: `inset 0 1.5px 0 rgba(255,255,255,0.28), inset 0 -10px 18px rgba(0,0,0,0.35), inset 0 0 ${press * 30}px rgba(10,132,255,${press * 0.5})`,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontFamily: FONT.display,
          fontWeight: 600,
          fontSize,
          letterSpacing: '-0.01em',
          color: press > 0.3 ? '#cfe6ff' : '#f2f4f8',
          textShadow: press > 0.3 ? `0 0 18px ${COLOR.accent}` : '0 1px 0 rgba(0,0,0,0.4)',
        }}
      >
        {label}
      </div>
    </div>
  );
};
