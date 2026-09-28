import React from 'react';
import {COLOR, FONT} from '../theme';
import {Glass} from './Glass';

type Lit = {down: number; rb: number; a: number; b: number};

const glow = (v: number, color: string) => (v > 0.01 ? `drop-shadow(0 0 ${6 + v * 14}px ${color})` : 'none');

/** A minimal controller that lights up the buttons the player presses. */
export const Controller: React.FC<{lit: Lit; width?: number}> = ({lit, width = 330}) => (
  <svg width={width} height={width * 0.66} viewBox="0 0 380 250" style={{overflow: 'visible'}}>
    <defs>
      <linearGradient id="padBody" x1="0" y1="0" x2="0" y2="1">
        <stop offset="0" stopColor="#f4f6fb" stopOpacity="0.5" />
        <stop offset="1" stopColor="#f4f6fb" stopOpacity="0.2" />
      </linearGradient>
    </defs>
    {/* bumpers */}
    <rect x={62} y={30} width={82} height={22} rx={11} fill="rgba(255,255,255,0.22)" stroke="rgba(255,255,255,0.5)" />
    <g style={{filter: glow(lit.rb, COLOR.accent)}}>
      <rect x={236} y={30 + lit.rb * 3} width={82} height={22} rx={11} fill={lit.rb > 0.05 ? COLOR.accent : 'rgba(255,255,255,0.22)'} stroke="rgba(255,255,255,0.7)" />
      <text x={277} y={46 + lit.rb * 3} textAnchor="middle" fontFamily={FONT.display} fontWeight={700} fontSize={13} fill="#fff">
        RB
      </text>
    </g>
    <text x={103} y={46} textAnchor="middle" fontFamily={FONT.display} fontWeight={700} fontSize={13} fill="rgba(255,255,255,0.75)">
      LB
    </text>
    {/* body */}
    <path
      d="M92,48 C60,48 40,62 32,96 L10,176 C2,212 24,240 56,232 C74,227 86,212 100,194 L124,166 L256,166 L280,194 C294,212 306,227 324,232 C356,240 378,212 370,176 L348,96 C340,62 320,48 288,48 Z"
      fill="url(#padBody)"
      stroke="rgba(255,255,255,0.95)"
      strokeWidth={2.6}
    />
    {/* sticks */}
    <circle cx={140} cy={150} r={24} fill="rgba(255,255,255,0.22)" stroke="rgba(255,255,255,0.8)" strokeWidth={1.5} />
    <circle cx={240} cy={150} r={24} fill="rgba(255,255,255,0.22)" stroke="rgba(255,255,255,0.8)" strokeWidth={1.5} />
    {/* d-pad */}
    <g transform="translate(98 104)">
      <path d="M-11,-33 L11,-33 L11,-11 L33,-11 L33,11 L11,11 L11,33 L-11,33 L-11,11 L-33,11 L-33,-11 L-11,-11 Z" fill="rgba(255,255,255,0.3)" stroke="rgba(255,255,255,0.85)" strokeWidth={1.5} />
      <g style={{filter: glow(lit.down, COLOR.accent)}}>
        <rect x={-11} y={11 + lit.down * 2} width={22} height={22} fill={lit.down > 0.05 ? COLOR.accent : 'transparent'} />
      </g>
      <path d="M0,27 L-6,19 L6,19 Z" fill="#fff" opacity={0.9} />
    </g>
    {/* face buttons */}
    <g transform="translate(282 104)" fontFamily={FONT.display} fontWeight={800} fontSize={17} textAnchor="middle">
      <circle cx={0} cy={-27} r={15} fill="rgba(255,255,255,0.28)" stroke="rgba(255,255,255,0.85)" strokeWidth={1.5} />
      <text x={0} y={-21} fill="rgba(255,255,255,0.8)">
        Y
      </text>
      <circle cx={-27} cy={0} r={15} fill="rgba(255,255,255,0.28)" stroke="rgba(255,255,255,0.85)" strokeWidth={1.5} />
      <text x={-27} y={6} fill="rgba(255,255,255,0.8)">
        X
      </text>
      <g style={{filter: glow(lit.b, '#ff4d5e')}}>
        <circle cx={27} cy={0} r={15 - lit.b * 1.5} fill={lit.b > 0.05 ? '#ff4d5e' : 'rgba(255,255,255,0.18)'} stroke="rgba(255,255,255,0.7)" strokeWidth={1.5} />
        <text x={27} y={6} fill="#fff">
          B
        </text>
      </g>
      <g style={{filter: glow(lit.a, '#3ddc84')}}>
        <circle cx={0} cy={27} r={15 - lit.a * 1.5} fill={lit.a > 0.05 ? '#2fc774' : 'rgba(255,255,255,0.18)'} stroke="rgba(255,255,255,0.7)" strokeWidth={1.5} />
        <text x={0} y={33} fill="#fff">
          A
        </text>
      </g>
    </g>
  </svg>
);

/** Glass panel with the controller and the name of the current action. */
export const PadHud: React.FC<{lit: Lit; action: string; actionKey: number; actionIn: number; style?: React.CSSProperties}> = ({
  lit,
  action,
  actionKey,
  actionIn,
  style,
}) => (
  <Glass radius={40} tone="dark" blur={30} style={{width: 430, padding: '28px 40px 24px', ...style}}>
    <div style={{display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12}}>
      <Controller lit={lit} />
      <div
        key={actionKey}
        style={{
          fontFamily: FONT.display,
          fontWeight: 600,
          fontSize: 30,
          letterSpacing: '-0.01em',
          color: '#fff',
          height: 38,
          opacity: actionIn,
          translate: `0 ${(1 - actionIn) * 14}px`,
        }}
      >
        {action}
      </div>
    </div>
  </Glass>
);
