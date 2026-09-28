import React from 'react';

/** Windows-style arrow cursor. Its tip is at (x, y). */
export const Cursor: React.FC<{x: number; y: number; size?: number; press?: number; opacity?: number}> = ({x, y, size = 40, press = 0, opacity = 1}) => (
  <svg
    width={size}
    height={size * 1.45}
    viewBox="0 0 22 32"
    style={{
      position: 'absolute',
      left: x - size * 0.06,
      top: y - size * 0.04,
      opacity,
      scale: 1 - press * 0.12,
      transformOrigin: '6% 4%',
      filter: 'drop-shadow(0 3px 5px rgba(0,0,0,0.45))',
      overflow: 'visible',
    }}
  >
    <path d="M1.3,1 L1.3,24.5 L7,19.2 L10.8,28.3 L15,26.5 L11.2,17.6 L18.8,17.6 Z" fill="#ffffff" stroke="#0b0b0f" strokeWidth={1.5} strokeLinejoin="round" />
  </svg>
);
