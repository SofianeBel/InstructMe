import React from 'react';
import {FONT} from '../theme';

// Game HUD. Text that the "OCR" can find is marked with data-ocr (see useOcrLayout).

export const OcrText: React.FC<{
  text: string;
  prefix: string;
  ids?: Record<number, string>;
  style?: React.CSSProperties;
}> = ({text, prefix, ids = {}, style}) => {
  const words = text.split(' ');
  return (
    <span style={style}>
      {words.map((w, i) => (
        <React.Fragment key={i}>
          <span data-ocr={ids[i] ?? `${prefix}-${i}`} style={{display: 'inline-block'}}>
            {w}
          </span>
          {i < words.length - 1 ? ' ' : null}
        </React.Fragment>
      ))}
    </span>
  );
};

const Bar: React.FC<{width: number; height: number; fill: number; from: string; to: string}> = ({width, height, fill, from, to}) => (
  <div
    style={{
      width,
      height,
      borderRadius: height,
      background: 'rgba(5,6,12,0.7)',
      border: '1px solid rgba(226,194,125,0.35)',
      padding: 2,
      boxShadow: '0 2px 10px rgba(0,0,0,0.5)',
    }}
  >
    <div
      style={{
        width: `${fill * 100}%`,
        height: '100%',
        borderRadius: height,
        background: `linear-gradient(90deg, ${from}, ${to})`,
        boxShadow: `0 0 12px ${to}`,
      }}
    />
  </div>
);

export const HealthPanel: React.FC = () => (
  <div style={{position: 'absolute', left: 48, top: 42, display: 'flex', gap: 18, alignItems: 'center'}}>
    <div style={{position: 'relative', width: 70, height: 78}}>
      <svg width={70} height={78} viewBox="0 0 70 78" style={{position: 'absolute', inset: 0}}>
        <polygon points="35,3 67,21 67,57 35,75 3,57 3,21" fill="rgba(8,9,18,0.8)" stroke="#e2c27d" strokeWidth={2.5} />
        <polygon points="35,11 60,25 60,53 35,67 10,53 10,25" fill="none" stroke="rgba(226,194,125,0.35)" strokeWidth={1} />
      </svg>
      <div
        data-ocr="hud-level"
        style={{
          position: 'absolute',
          left: 0,
          right: 0,
          top: 20,
          textAlign: 'center',
          fontFamily: FONT.hud,
          fontWeight: 700,
          fontSize: 30,
          color: '#f4e4bd',
          lineHeight: '36px',
        }}
      >
        24
      </div>
    </div>
    <div style={{display: 'flex', flexDirection: 'column', gap: 9, fontFamily: FONT.hud, fontWeight: 700, color: 'rgba(244,232,210,0.85)'}}>
      <div style={{display: 'flex', alignItems: 'center', gap: 12, fontSize: 19, letterSpacing: '0.08em'}}>
        <span data-ocr="hud-hp" style={{width: 28}}>HP</span>
        <Bar width={300} height={14} fill={0.72} from="#b8322a" to="#ff7a45" />
        <OcrText text="1,240 / 1,720" prefix="hud-hpv" style={{fontSize: 18, fontWeight: 600, color: 'rgba(244,232,210,0.7)'}} />
      </div>
      <div style={{display: 'flex', alignItems: 'center', gap: 12, fontSize: 19, letterSpacing: '0.08em'}}>
        <span data-ocr="hud-st" style={{width: 28}}>ST</span>
        <Bar width={236} height={9} fill={0.58} from="#1f8a7a" to="#5ff0c8" />
      </div>
    </div>
  </div>
);

export const Minimap: React.FC<{t: number}> = ({t}) => {
  const wobble = Math.sin(t * 0.02) * 4;
  return (
    <div style={{position: 'absolute', left: 1684, top: 38, width: 190, height: 190}}>
      <svg width={190} height={190} viewBox="0 0 190 190">
        <defs>
          <clipPath id="mapClip">
            <circle cx={95} cy={95} r={86} />
          </clipPath>
          <radialGradient id="mapFill">
            <stop offset="0" stopColor="#2a2440" stopOpacity="0.85" />
            <stop offset="1" stopColor="#0b0914" stopOpacity="0.9" />
          </radialGradient>
        </defs>
        <circle cx={95} cy={95} r={86} fill="url(#mapFill)" />
        <g clipPath="url(#mapClip)" transform={`rotate(${wobble} 95 95)`} stroke="rgba(226,194,125,0.35)" fill="none" strokeWidth={1.4}>
          <path d="M-10,120 C30,100 60,130 95,110 C130,90 150,115 200,95" />
          <path d="M-10,150 C40,135 70,160 110,145 C150,130 170,150 200,140" />
          <path d="M-10,70 C30,60 50,80 90,62 C130,44 160,70 200,52" />
          <path d="M40,-10 C50,40 45,80 60,200" strokeDasharray="4 5" stroke="rgba(244,232,210,0.35)" />
          <rect x={124} y={42} width={10} height={10} fill="#e2c27d" stroke="none" transform="rotate(45 129 47)" opacity={0.6 + 0.4 * Math.sin(t * 0.15)} />
        </g>
        <circle cx={95} cy={95} r={86} fill="none" stroke="#e2c27d" strokeWidth={2.5} />
        <circle cx={95} cy={95} r={80} fill="none" stroke="rgba(226,194,125,0.25)" strokeWidth={1} />
        <polygon points="95,84 104,106 95,101 86,106" fill="#f4ecd8" />
        <text x={95} y={24} textAnchor="middle" fontFamily={FONT.hud} fontWeight={700} fontSize={17} fill="#e2c27d">
          N
        </text>
      </svg>
    </div>
  );
};

export const QuestTracker: React.FC = () => (
  <div
    style={{
      position: 'absolute',
      left: 1498,
      top: 262,
      width: 380,
      paddingLeft: 18,
      borderLeft: '2px solid rgba(226,194,125,0.7)',
      color: '#f4ecd8',
      textShadow: '0 2px 8px rgba(0,0,0,0.7)',
    }}
  >
    <OcrText text="MAIN QUEST" prefix="q-label" style={{fontFamily: FONT.hud, fontWeight: 700, fontSize: 17, letterSpacing: '0.26em', color: '#e2c27d'}} />
    <div style={{fontFamily: FONT.gameTitle, fontWeight: 700, fontSize: 29, marginTop: 4}}>
      <OcrText text="The Last Light" prefix="q-title" />
    </div>
    <div style={{fontFamily: FONT.gameText, fontWeight: 500, fontSize: 21, marginTop: 10, lineHeight: '30px', color: 'rgba(244,236,216,0.92)'}}>
      <span style={{color: '#e2c27d', marginRight: 10}}>◆</span>
      <OcrText text="Reach the outpost before nightfall" prefix="q-obj1" />
    </div>
    <div style={{fontFamily: FONT.gameText, fontWeight: 500, fontSize: 21, lineHeight: '30px', color: 'rgba(244,236,216,0.55)'}}>
      <span style={{marginRight: 10}}>◇</span>
      <OcrText text="Find the lost scouts 0/3" prefix="q-obj2" />
    </div>
  </div>
);
