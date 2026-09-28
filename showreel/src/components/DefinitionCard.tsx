import {Volume2} from 'lucide-react';
import React from 'react';
import {interpolate, random} from 'remotion';
import {CLAMP, EASE, ramp} from '../lib/anim';
import {COLOR, FONT, UI_SCALE as S} from '../theme';
import {Glass} from './Glass';

export type Definition = {meta: string; word: string; ipa: string; translation: string; meaning: string};

export const REACH: Definition = {
  meta: 'ANGLAIS · VERBE',
  word: 'reach',
  ipa: '/riːtʃ/',
  translation: 'atteindre',
  meaning: 'Ici : arriver jusqu’à l’avant-poste avant la tombée de la nuit.',
};

export const GIVE_UP: Definition = {
  meta: 'ANGLAIS · VERBE À PARTICULE',
  word: 'give up',
  ipa: '/ɡɪv ʌp/',
  translation: 'abandonner',
  meaning: 'Ici : ne pas renoncer maintenant, il faut tenir bon.',
};

export const CARD_WIDTH = 400 * S;
export const CARD_HEIGHT = 266 * S; // loaded card with a two-line meaning
export const SPEAKER_OFFSET = {x: CARD_WIDTH - 24 * S - 20 * S, y: 18 * S + 20 * S}; // button center inside the card

const LETTERS = 'abcdeéfghijklmnopqrstuvwxyzàç';

/** Letters settle one by one, like a translation resolving. */
const decode = (text: string, f: number, seed: string) =>
  text
    .split('')
    .map((ch, i) => {
      if (ch === ' ' || f >= i * 1.7 + 4) return ch;
      if (f < 0) return '';
      return LETTERS[Math.floor(random(`${seed}-${i}-${Math.floor(f / 2)}`) * LETTERS.length)];
    })
    .join('');

type CardProps = {
  def: Definition;
  /** Frames since the card opened. */
  f: number;
  /** Frames of the "Analyse du contexte…" state before the answer arrives. */
  loading?: number;
  /** Frames since the speaker button was clicked, or null. */
  speak?: number | null;
  hover?: number;
  style?: React.CSSProperties;
};

export const DefinitionCard: React.FC<CardProps> = ({def, f, loading = 12, speak = null, hover = 0, style}) => {
  const c = f - loading; // frames since the answer arrived
  const loaded = c >= 0;
  const shimmerX = interpolate(f, [0, loading + 6], [-120, 220]);
  const ipaOpen = ramp(c, 0, 10);
  const words = def.meaning.split(' ');
  const press = speak === null ? 0 : interpolate(speak, [0, 3, 10], [0, 1, 0], CLAMP);

  return (
    <Glass
      radius={26 * S}
      sweep={interpolate(f, [4, 34], [0, 1], CLAMP)}
      style={{
        width: CARD_WIDTH,
        padding: `${18 * S}px ${24 * S}px ${22 * S}px`,
        fontFamily: FONT.ui,
        ...style,
      }}
    >
      <div style={{display: 'flex', justifyContent: 'space-between', alignItems: 'center', height: 40 * S}}>
        <div style={{fontSize: 13 * S, fontWeight: 600, color: COLOR.inkSoft, letterSpacing: '0.03em'}}>{loaded ? def.meta : 'ANGLAIS'}</div>
        <div style={{position: 'relative', width: 40 * S, height: 40 * S}}>
          {speak !== null
            ? [0, 7, 14].map((d) => (
                <div
                  key={d}
                  style={{
                    position: 'absolute',
                    left: '50%',
                    top: '50%',
                    width: 40 * S,
                    height: 40 * S,
                    marginLeft: -20 * S,
                    marginTop: -20 * S,
                    borderRadius: '50%',
                    border: `${2.2 * S}px solid ${COLOR.accent}`,
                    opacity: interpolate(speak - d, [0, 4, 30], [0, 0.75, 0], CLAMP),
                    scale: interpolate(speak - d, [0, 30], [1, 2.6], {...CLAMP, easing: EASE.out}),
                  }}
                />
              ))
            : null}
          <div
            style={{
              position: 'absolute',
              inset: 0,
              borderRadius: '50%',
              background: `rgba(255,255,255,${0.4 + 0.35 * hover})`,
              border: '1px solid rgba(255,255,255,0.9)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              scale: 1 - press * 0.12,
              boxShadow: speak !== null ? `0 0 ${interpolate(speak, [0, 10, 45], [0, 22, 0], CLAMP)}px ${COLOR.accent}` : 'none',
            }}
          >
            <Volume2 size={19 * S} color={speak !== null && speak < 45 ? COLOR.accent : COLOR.ink} strokeWidth={2.2} />
          </div>
        </div>
      </div>

      <div style={{fontSize: 36 * S, fontWeight: 600, color: COLOR.ink, marginTop: 2 * S, lineHeight: 1.15, letterSpacing: '-0.01em'}}>{def.word}</div>
      <div
        style={{
          fontSize: 18 * S,
          color: COLOR.inkSoft,
          lineHeight: 1.3,
          maxHeight: 18 * S * 1.3 * ipaOpen,
          opacity: ipaOpen,
          overflow: 'hidden',
        }}
      >
        {def.ipa}
      </div>
      <div
        style={{
          height: 1.2 * S,
          margin: `${14 * S}px 0 ${12 * S}px`,
          background: 'linear-gradient(90deg, rgba(255,255,255,0.9), rgba(255,255,255,0.35))',
          scale: `${interpolate(f, [2, 16], [0, 1], {...CLAMP, easing: EASE.out})} 1`,
          transformOrigin: 'left center',
        }}
      />
      <div style={{fontSize: 30 * S, fontWeight: 600, color: COLOR.ink, lineHeight: 1.2, minHeight: 30 * S * 1.2}}>
        {loaded ? decode(def.translation, c, def.word) : '…'}
      </div>
      <div style={{fontSize: 17 * S, color: COLOR.meaning, lineHeight: `${25 * S}px`, marginTop: 6 * S, minHeight: 50 * S}}>
        {loaded ? (
          words.map((w, i) => (
            <span
              key={i}
              style={{
                display: 'inline-block',
                marginRight: '0.28em',
                opacity: ramp(c, 6 + i * 1.3, 14 + i * 1.3),
                translate: `0 ${interpolate(c, [6 + i * 1.3, 14 + i * 1.3], [8, 0], {...CLAMP, easing: EASE.out})}px`,
              }}
            >
              {w}
            </span>
          ))
        ) : (
          <span
            style={{
              backgroundImage: `linear-gradient(100deg, ${COLOR.meaning} 0%, ${COLOR.meaning} ${shimmerX - 30}%, #ffffff ${shimmerX}%, ${COLOR.meaning} ${shimmerX + 30}%, ${COLOR.meaning} 100%)`,
              WebkitBackgroundClip: 'text',
              backgroundClip: 'text',
              color: 'transparent',
            }}
          >
            Analyse du contexte…
          </span>
        )}
      </div>
    </Glass>
  );
};
