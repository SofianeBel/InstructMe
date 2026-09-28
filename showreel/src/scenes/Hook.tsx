import React from 'react';
import {AbsoluteFill, interpolate, random, Sequence, useCurrentFrame} from 'remotion';
import {Aurora} from '../components/Effects';
import {CLAMP, EASE, ramp} from '../lib/anim';
import {COLOR, FONT} from '../theme';
import {HOOK} from '../timeline';

// Five words from five kinds of games, one per 8th note, then the question.

type FlashStyle = {
  word: string;
  genre: string;
  font: string;
  size: number;
  tracking: string;
  color: string;
  shadow: string;
  background: string;
  texture: React.ReactNode;
};

const Grid: React.FC<{color: string; size: number; perspective?: boolean}> = ({color, size, perspective}) => (
  <AbsoluteFill
    style={{
      backgroundImage: `linear-gradient(${color} 1px, transparent 1px), linear-gradient(90deg, ${color} 1px, transparent 1px)`,
      backgroundSize: `${size}px ${size}px`,
      transform: perspective ? 'perspective(700px) rotateX(62deg) translateY(260px) scale(2.4)' : undefined,
      maskImage: perspective ? 'linear-gradient(transparent 20%, #000 75%)' : undefined,
    }}
  />
);

const FLASHES: FlashStyle[] = [
  {
    word: 'FORSAKEN',
    genre: 'Fantasy RPG',
    font: FONT.gameTitle,
    size: 196,
    tracking: '0.06em',
    color: '#f6dd9c',
    shadow: '0 0 40px rgba(255,190,90,0.55), 0 6px 0 #6b3d10',
    background: 'radial-gradient(ellipse at 50% 55%, #5a1414 0%, #250608 55%, #0b0203 100%)',
    texture: (
      <AbsoluteFill style={{background: 'radial-gradient(circle at 50% 50%, rgba(255,170,80,0.18), transparent 45%)'}}>
        <div style={{position: 'absolute', left: 560, right: 560, top: 360, height: 2, background: 'linear-gradient(90deg, transparent, #d9a74a, transparent)'}} />
        <div style={{position: 'absolute', left: 560, right: 560, top: 720, height: 2, background: 'linear-gradient(90deg, transparent, #d9a74a, transparent)'}} />
      </AbsoluteFill>
    ),
  },
  {
    word: 'RELENTLESS',
    genre: 'Sci-fi shooter',
    font: FONT.scifi,
    size: 150,
    tracking: '0.04em',
    color: '#8ff7ff',
    shadow: '0 0 30px rgba(60,230,255,0.9), 0 0 90px rgba(60,200,255,0.5)',
    background: 'radial-gradient(ellipse at 50% 60%, #06343d 0%, #03161c 55%, #010608 100%)',
    texture: (
      <>
        <Grid color="rgba(80,230,255,0.28)" size={70} perspective />
        <AbsoluteFill style={{backgroundImage: 'repeating-linear-gradient(0deg, rgba(0,0,0,0.25) 0 2px, transparent 2px 5px)'}} />
      </>
    ),
  },
  {
    word: 'SCAVENGE',
    genre: 'Retro survival',
    font: FONT.pixel,
    size: 118,
    tracking: '0em',
    color: '#ffe14d',
    shadow: '10px 10px 0 #ff3fa4, 20px 20px 0 rgba(60,20,120,0.9)',
    background: 'linear-gradient(180deg, #2a0e5c 0%, #150633 100%)',
    texture: <Grid color="rgba(255,255,255,0.06)" size={24} />,
  },
  {
    word: 'OUTNUMBERED',
    genre: 'Tactical',
    font: FONT.tactical,
    size: 164,
    tracking: '0.02em',
    color: '#ece6cf',
    shadow: '0 8px 0 rgba(0,0,0,0.6), 0 0 40px rgba(255,170,40,0.35)',
    background: 'radial-gradient(ellipse at 50% 50%, #3a3d22 0%, #181a0d 60%, #0a0b05 100%)',
    texture: (
      <AbsoluteFill>
        <div style={{position: 'absolute', left: 0, right: 0, top: 150, height: 34, backgroundImage: 'repeating-linear-gradient(45deg, #e8a623 0 26px, #111 26px 52px)', opacity: 0.85}} />
        <div style={{position: 'absolute', left: 0, right: 0, bottom: 150, height: 34, backgroundImage: 'repeating-linear-gradient(45deg, #e8a623 0 26px, #111 26px 52px)', opacity: 0.85}} />
      </AbsoluteFill>
    ),
  },
  {
    word: 'LURKING',
    genre: 'Horror',
    font: FONT.horror,
    size: 230,
    tracking: '0.04em',
    color: '#e0102e',
    shadow: '0 0 50px rgba(255,0,40,0.55), 0 4px 0 #3a0008',
    background: 'radial-gradient(ellipse at 50% 60%, #2a0409 0%, #0a0103 55%, #000 100%)',
    texture: <AbsoluteFill style={{background: 'radial-gradient(ellipse 60% 35% at 50% 100%, rgba(120,0,20,0.5), transparent)'}} />,
  },
];

const Flash: React.FC<{s: FlashStyle; index: number}> = ({s, index}) => {
  const f = useCurrentFrame();
  const split = interpolate(f, [0, 7], [16, 0], {...CLAMP, easing: EASE.out});
  const shakeAmp = interpolate(f, [0, 8], [14, 0], CLAMP);
  const jx = (random(`jx${index}-${f}`) - 0.5) * 2 * shakeAmp;
  const jy = (random(`jy${index}-${f}`) - 0.5) * 2 * shakeAmp;

  return (
    <AbsoluteFill style={{background: s.background, overflow: 'hidden'}}>
      {s.texture}
      <AbsoluteFill
        style={{
          alignItems: 'center',
          justifyContent: 'center',
          scale: interpolate(f, [0, 15], [1.22, 1], {...CLAMP, easing: EASE.out}),
          translate: `${jx}px ${jy}px`,
          filter: `blur(${interpolate(f, [0, 4], [12, 0], CLAMP)}px)`,
        }}
      >
        <div
          style={{
            fontFamily: s.font,
            fontSize: s.size,
            letterSpacing: s.tracking,
            color: s.color,
            textShadow: `${s.shadow}, ${-split}px 0 rgba(255,0,70,0.75), ${split}px 0 rgba(0,210,255,0.75)`,
            whiteSpace: 'nowrap',
          }}
        >
          {s.word}
        </div>
      </AbsoluteFill>
      <div
        style={{
          position: 'absolute',
          left: 110,
          bottom: 96,
          fontFamily: FONT.display,
          fontWeight: 600,
          fontSize: 28,
          letterSpacing: '0.14em',
          textTransform: 'uppercase',
          color: 'rgba(255,255,255,0.72)',
        }}
      >
        <span style={{color: 'rgba(255,255,255,0.4)'}}>0{index + 1}&nbsp;&nbsp;</span>
        {s.genre}
      </div>
      <div style={{position: 'absolute', right: 110, bottom: 108, display: 'flex', gap: 10}}>
        {FLASHES.map((_, i) => (
          <div key={i} style={{width: i === index ? 40 : 12, height: 12, borderRadius: 6, background: i === index ? '#fff' : 'rgba(255,255,255,0.35)'}} />
        ))}
      </div>
      <AbsoluteFill style={{backgroundColor: '#fff', opacity: interpolate(f, [0, 3], [0.35, 0], CLAMP)}} />
    </AbsoluteFill>
  );
};

const Question: React.FC = () => {
  const f = useCurrentFrame();
  const words = [
    {text: 'Stuck', serif: false},
    {text: 'on', serif: false},
    {text: 'a', serif: false},
    {text: 'word?', serif: true},
  ];
  const out = ramp(f, 34, 45, EASE.in);
  const box = ramp(f, 16, 26);
  return (
    <AbsoluteFill style={{overflow: 'hidden'}}>
      <Aurora t={f + 400} strength={0.55 + 0.45 * ramp(f, 0, 20)} />
      <AbsoluteFill
        style={{
          alignItems: 'center',
          justifyContent: 'center',
          scale: 1 + out * 2.2,
          opacity: 1 - out,
          filter: `blur(${out * 18}px)`,
        }}
      >
        <div style={{display: 'flex', alignItems: 'baseline', gap: 34, fontFamily: FONT.display, fontWeight: 800, fontSize: 140, letterSpacing: '-0.05em', color: '#fff'}}>
          {words.map((w, i) => (
            <div
              key={w.text}
              style={{
                position: 'relative',
                fontFamily: w.serif ? FONT.serif : undefined,
                fontWeight: w.serif ? 400 : undefined,
                fontSize: w.serif ? 168 : undefined,
                letterSpacing: w.serif ? '-0.02em' : undefined,
                opacity: ramp(f, i * 3, i * 3 + 8),
                translate: `0 ${interpolate(f, [i * 3, i * 3 + 12], [50, 0], {...CLAMP, easing: EASE.out})}px`,
                filter: `blur(${interpolate(f, [i * 3, i * 3 + 8], [12, 0], CLAMP)}px)`,
              }}
            >
              {w.serif ? (
                <div
                  style={{
                    position: 'absolute',
                    left: -18,
                    right: -18,
                    top: 18,
                    bottom: -8,
                    borderRadius: 16,
                    border: `5px solid ${COLOR.accent}`,
                    background: 'rgba(10,132,255,0.2)',
                    boxShadow: `0 0 40px rgba(10,132,255,0.8)`,
                    opacity: box,
                    scale: interpolate(box, [0, 1], [1.25, 1]),
                  }}
                />
              ) : null}
              <span style={{position: 'relative'}}>{w.text}</span>
            </div>
          ))}
        </div>
      </AbsoluteFill>
      <AbsoluteFill style={{backgroundColor: '#fff', opacity: interpolate(f, [41, 45], [0, 0.9], CLAMP)}} />
    </AbsoluteFill>
  );
};

export const Hook: React.FC = () => (
  <AbsoluteFill style={{backgroundColor: '#000'}}>
    {FLASHES.map((s, i) => (
      <Sequence key={s.word} name={s.word} from={HOOK.flashes[i]} durationInFrames={HOOK.flashLength}>
        <Flash s={s} index={i} />
      </Sequence>
    ))}
    <Sequence name="Question" from={HOOK.question}>
      <Question />
    </Sequence>
  </AbsoluteFill>
);
