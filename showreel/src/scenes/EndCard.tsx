import React from 'react';
import {AbsoluteFill, interpolate, random, useCurrentFrame} from 'remotion';
import {Aurora} from '../components/Effects';
import {Keycap} from '../components/Keycap';
import {LogoMark} from '../components/Logo';
import {CLAMP, EASE, pop, ramp} from '../lib/anim';
import {COLOR, FONT} from '../theme';
import {END} from '../timeline';

const EN = 'Every word. Understood.';
const FR = 'Chaque mot. Compris.';
const GLYPHS = 'abcdefghijklmnopqrstuvwxyzéàç';

/** The tagline is "translated" in place, like the app does for a selected phrase. */
const Tagline: React.FC<{f: number}> = ({f}) => {
  const d = f - END.decode;
  const settled = d < 0 ? 0 : Math.floor(d / 1.4);
  const length = d < 0 ? EN.length : Math.max(FR.length, EN.length - Math.floor(d / 1.1));
  const chars = Array.from({length}, (_, i) => {
    if (d >= 0 && i < settled) return {ch: FR[i] ?? '', fr: true};
    if (d >= 0 && i < settled + 4) return {ch: EN[i] === ' ' || FR[i] === ' ' ? ' ' : GLYPHS[Math.floor(random(`tg-${i}-${Math.floor(f / 2)}`) * GLYPHS.length)], fr: false};
    return {ch: EN[i] ?? '', fr: false};
  });
  const words = EN.split(' ');
  const select = pop(f, END.select, {damping: 12, stiffness: 220, mass: 0.6});
  const unselect = ramp(f, END.decodeEnd + 12, END.decodeEnd + 26, EASE.inOut);

  return (
    <div style={{position: 'relative', display: 'inline-block', padding: '0 6px'}}>
      <div
        style={{
          position: 'absolute',
          inset: '-10px -26px -14px',
          borderRadius: 18,
          border: `4px solid ${COLOR.accent}`,
          background: 'rgba(10,132,255,0.16)',
          boxShadow: '0 0 36px rgba(10,132,255,0.85), inset 0 0 18px rgba(10,132,255,0.35)',
          opacity: Math.min(1, select * 1.4) * (1 - unselect),
          scale: interpolate(select, [0, 1], [1.25, 1]),
        }}
      />
      <div style={{position: 'relative', height: 96, display: 'flex', alignItems: 'center', whiteSpace: 'pre', color: 'rgba(255,255,255,0.92)'}}>
        {f < END.decode
          ? words.map((w, i) => (
              <span
                key={i}
                style={{
                  fontFamily: FONT.display,
                  fontWeight: 600,
                  fontSize: 64,
                  letterSpacing: '-0.03em',
                  display: 'inline-block',
                  opacity: ramp(f, END.tagline + i * 4, END.tagline + i * 4 + 10),
                  translate: `0 ${interpolate(f, [END.tagline + i * 4, END.tagline + i * 4 + 14], [30, 0], {...CLAMP, easing: EASE.out})}px`,
                  filter: `blur(${interpolate(f, [END.tagline + i * 4, END.tagline + i * 4 + 10], [8, 0], CLAMP)}px)`,
                }}
              >
                {i < words.length - 1 ? `${w} ` : w}
              </span>
            ))
          : chars.map((c, i) => (
              <span
                key={i}
                style={
                  c.fr
                    ? {fontFamily: FONT.serif, fontStyle: 'italic', fontSize: 84, letterSpacing: '-0.01em', color: '#fff'}
                    : {fontFamily: FONT.display, fontWeight: 600, fontSize: 64, letterSpacing: '-0.03em'}
                }
              >
                {c.ch}
              </span>
            ))}
      </div>
    </div>
  );
};

export const EndCard: React.FC = () => {
  const f = useCurrentFrame();
  const hit = pop(f, END.logo, {damping: 11, stiffness: 160, mass: 0.8});
  const reveal = ramp(f, END.wordmark, END.wordmark + 28, EASE.inOut);
  const sweep = interpolate(f, [132, 166], [-30, 130], CLAMP);
  const credits = ramp(f, END.credits, END.credits + 18);

  return (
    <AbsoluteFill style={{overflow: 'hidden'}}>
      <Aurora t={f + 690} />
      <AbsoluteFill style={{background: 'radial-gradient(ellipse 45% 38% at 50% 44%, rgba(10,132,255,0.18), transparent 70%)'}} />

      {/* Impact light. */}
      <AbsoluteFill style={{alignItems: 'center', justifyContent: 'center'}}>
        <div
          style={{
            width: 300,
            height: 300,
            borderRadius: '50%',
            border: '3px solid rgba(190,225,255,0.9)',
            boxShadow: '0 0 60px 12px rgba(10,132,255,0.6)',
            scale: interpolate(f, [0, 34], [0.4, 7], {...CLAMP, easing: EASE.out}),
            opacity: interpolate(f, [0, 34], [0.9, 0], CLAMP),
          }}
        />
      </AbsoluteFill>

      <AbsoluteFill style={{scale: interpolate(f, [0, 180], [1, 1.035])}}>
        {/* Lockup: the mark slides left as the wordmark wipes in. */}
        <div style={{position: 'absolute', left: 0, right: 0, top: 330, display: 'flex', justifyContent: 'center', alignItems: 'center'}}>
          <LogoMark size={150} style={{scale: interpolate(hit, [0, 1], [1.5, 1]), opacity: Math.min(1, hit * 2)}} />
          <div style={{maxWidth: reveal * 860, overflow: 'hidden'}}>
            <div
              style={{
                paddingLeft: 44,
                fontFamily: FONT.display,
                fontWeight: 800,
                fontSize: 156,
                letterSpacing: '-0.05em',
                whiteSpace: 'nowrap',
                lineHeight: 1.1,
                translate: `${(1 - reveal) * -60}px 0`,
                backgroundImage: `linear-gradient(100deg, #fff 0%, #fff ${sweep - 12}%, #bfe2ff ${sweep}%, #fff ${sweep + 12}%, #fff 100%)`,
                WebkitBackgroundClip: 'text',
                backgroundClip: 'text',
                color: 'transparent',
              }}
            >
              Instruct<span style={{backgroundImage: `linear-gradient(135deg, #6cc0ff, ${COLOR.accent} 45%, #8a6cff)`, WebkitBackgroundClip: 'text', backgroundClip: 'text'}}>Me</span>
            </div>
          </div>
        </div>

        <div style={{position: 'absolute', left: 0, right: 0, top: 590, display: 'flex', justifyContent: 'center'}}>
          <Tagline f={f} />
        </div>

        <div
          style={{
            position: 'absolute',
            left: 0,
            right: 0,
            top: 850,
            display: 'flex',
            justifyContent: 'center',
            alignItems: 'center',
            gap: 14,
            opacity: credits,
            translate: `0 ${(1 - credits) * 24}px`,
            fontFamily: FONT.display,
            fontWeight: 500,
            fontSize: 30,
            color: 'rgba(255,255,255,0.7)',
          }}
        >
          <Keycap label="Ctrl" width={100} height={62} fontSize={26} press={0} />
          <span>+</span>
          <Keycap label="Alt" width={90} height={62} fontSize={26} press={0} />
          <span>+</span>
          <Keycap label="L" width={70} height={62} fontSize={26} press={0} />
          <span style={{marginLeft: 26}}>Windows 10 &amp; 11</span>
          <span style={{opacity: 0.5}}>·</span>
          <span>Powered by Claude</span>
        </div>
      </AbsoluteFill>

      <AbsoluteFill style={{backgroundColor: '#eaf4ff', opacity: interpolate(f, [0, 1, 12], [0, 0.6, 0], CLAMP)}} />
    </AbsoluteFill>
  );
};
