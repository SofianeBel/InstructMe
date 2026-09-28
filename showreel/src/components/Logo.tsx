import React from 'react';
import {COLOR} from '../theme';

/** App mark: a glass tile with an "i" framed by the blue selection corners the app draws around a word. */
export const LogoMark: React.FC<{size: number; style?: React.CSSProperties}> = ({size, style}) => (
  <div
    style={{
      position: 'relative',
      width: size,
      height: size,
      borderRadius: size * 0.27,
      background: `linear-gradient(145deg, #4fb0ff 0%, ${COLOR.accent} 42%, #5b3dff 100%)`,
      boxShadow: `0 ${size * 0.14}px ${size * 0.5}px rgba(10,132,255,0.45), inset 0 ${size * 0.012}px 0 rgba(255,255,255,0.6), inset 0 -${size * 0.03}px ${size * 0.08}px rgba(20,0,80,0.35)`,
      flexShrink: 0,
      ...style,
    }}
  >
    <div
      style={{
        position: 'absolute',
        inset: 0,
        borderRadius: size * 0.27,
        background: 'linear-gradient(180deg, rgba(255,255,255,0.42) 0%, rgba(255,255,255,0) 48%)',
      }}
    />
    <svg width={size} height={size} viewBox="0 0 100 100" style={{position: 'absolute', inset: 0}}>
      <g fill="none" stroke="rgba(255,255,255,0.75)" strokeWidth={4} strokeLinecap="round">
        <path d="M24,34 L24,24 L34,24" />
        <path d="M66,24 L76,24 L76,34" />
        <path d="M76,66 L76,76 L66,76" />
        <path d="M34,76 L24,76 L24,66" />
      </g>
      <circle cx={50} cy={35} r={6.2} fill="#fff" />
      <rect x={44.2} y={45} width={11.6} height={25} rx={5.8} fill="#fff" />
    </svg>
  </div>
);
