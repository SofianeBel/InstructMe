import React from 'react';
import {FONT} from '../theme';

// The in-game line the player does not understand. The ids of the key words are used by the demo.
export const DIALOG_LINES = [
  ['Commander,', 'we', 'must', 'reach', 'the', 'outpost', 'before', 'nightfall.'],
  ['Stay', 'strong.', "Don't", 'give', 'up', 'now!'],
] as const;

const KEY_IDS: Record<string, string> = {'0-3': 'reach', '1-3': 'give', '1-4': 'up'};

// One extra character per word for the space (or line break) after it, as the typing below counts.
export const DIALOG_CHARS = DIALOG_LINES.flat().reduce((n, word) => n + word.length + 1, 0);

export const DIALOG_BOX = {x: 300, y: 770, w: 1320, h: 190};

const Portrait: React.FC = () => (
  <svg width={130} height={130} viewBox="0 0 130 130" style={{position: 'absolute', left: 34, top: 30}}>
    <defs>
      <radialGradient id="portraitBg" cx="0.72" cy="0.32" r="0.9">
        <stop offset="0" stopColor="#f3a766" />
        <stop offset="0.45" stopColor="#7a3f5f" />
        <stop offset="1" stopColor="#170f24" />
      </radialGradient>
      <clipPath id="portraitClip">
        <circle cx={65} cy={65} r={61} />
      </clipPath>
    </defs>
    <g clipPath="url(#portraitClip)">
      <rect width={130} height={130} fill="url(#portraitBg)" />
      <path d="M6,140 C16,102 40,90 65,88 C90,90 114,102 124,140 Z" fill="#0b0812" />
      <path d="M38,96 C33,62 42,32 65,27 C88,32 97,62 92,96 C82,103 48,103 38,96 Z" fill="#0b0812" />
      <path d="M52,90 C50,68 56,52 65,50 C74,52 80,68 78,90 Z" fill="#1e1527" />
      <path d="M66,28 C88,33 97,62 92,96" fill="none" stroke="#ffb070" strokeWidth={2.2} opacity={0.85} />
      <path d="M92,100 C104,104 116,116 122,136" fill="none" stroke="#ffb070" strokeWidth={1.6} opacity={0.5} />
      <circle cx={59} cy={71} r={1.7} fill="#ffe0a8" opacity={0.85} />
      <circle cx={71} cy={71} r={1.7} fill="#ffe0a8" opacity={0.85} />
    </g>
    <circle cx={65} cy={65} r={61} fill="none" stroke="#e2c27d" strokeWidth={3} />
  </svg>
);

const Corner: React.FC<{x: string; y: string}> = ({x, y}) => (
  <div
    style={{
      position: 'absolute',
      [x]: -6,
      [y]: -6,
      width: 12,
      height: 12,
      rotate: '45deg',
      background: '#e2c27d',
      boxShadow: '0 0 10px rgba(226,194,125,0.6)',
    }}
  />
);

export const Dialog: React.FC<{t: number; chars: number}> = ({t, chars}) => {
  let offset = 0;
  return (
    <div
      style={{
        position: 'absolute',
        left: DIALOG_BOX.x,
        top: DIALOG_BOX.y,
        width: DIALOG_BOX.w,
        height: DIALOG_BOX.h,
        borderRadius: 6,
        background: 'linear-gradient(180deg, rgba(14,14,28,0.84), rgba(6,6,14,0.93))',
        border: '1.5px solid rgba(226,194,125,0.6)',
        boxShadow: '0 24px 70px rgba(0,0,0,0.55), inset 0 0 40px rgba(226,194,125,0.06)',
      }}
    >
      <div style={{position: 'absolute', inset: 7, border: '1px solid rgba(226,194,125,0.18)', borderRadius: 3}} />
      <Corner x="left" y="top" />
      <Corner x="right" y="top" />
      <Corner x="left" y="bottom" />
      <Corner x="right" y="bottom" />
      <Portrait />
      <div
        data-ocr="name"
        style={{
          position: 'absolute',
          left: 196,
          top: 20,
          fontFamily: FONT.gameTitle,
          fontWeight: 700,
          fontSize: 22,
          letterSpacing: '0.24em',
          color: '#e2c27d',
          textShadow: '0 0 12px rgba(226,194,125,0.35)',
        }}
      >
        KAEL
      </div>
      <div
        data-block="dialog"
        style={{
          position: 'absolute',
          left: 196,
          top: 56,
          fontFamily: FONT.gameText,
          fontWeight: 500,
          fontSize: 38,
          lineHeight: '52px',
          color: '#f3ebdd',
          textShadow: '0 2px 10px rgba(0,0,0,0.65)',
          whiteSpace: 'nowrap',
        }}
      >
        {DIALOG_LINES.map((line, li) => (
          <div key={li}>
            {line.map((word, wi) => {
              const start = offset;
              offset += word.length + 1;
              const shown = Math.max(0, Math.min(word.length, chars - start));
              return (
                <React.Fragment key={wi}>
                  <span data-ocr={KEY_IDS[`${li}-${wi}`] ?? `d${li}-${wi}`} style={{display: 'inline-block'}}>
                    {word.slice(0, shown)}
                    <span style={{opacity: 0}}>{word.slice(shown)}</span>
                  </span>
                  {wi < line.length - 1 ? ' ' : null}
                </React.Fragment>
              );
            })}
          </div>
        ))}
      </div>
      <div
        style={{
          position: 'absolute',
          right: 28,
          bottom: 16 + Math.abs(Math.sin(t * 0.12)) * 6,
          color: '#e2c27d',
          fontSize: 20,
          opacity: chars >= DIALOG_CHARS ? 1 : 0,
        }}
      >
        ▼
      </div>
    </div>
  );
};
