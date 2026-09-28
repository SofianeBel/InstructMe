import {Gamepad2, Languages, Layers, Snowflake, TextSelect, Volume2} from 'lucide-react';
import React from 'react';
import {AbsoluteFill, interpolate, useCurrentFrame} from 'remotion';
import {Aurora} from '../components/Effects';
import {Glass} from '../components/Glass';
import {LogoMark} from '../components/Logo';
import {GameScene} from '../game/GameScene';
import {CLAMP, EASE, pop, ramp} from '../lib/anim';
import {COLOR, FONT} from '../theme';
import {DEMO, FEATURES, SCENE} from '../timeline';

// The game shrinks into a floating screen and the features gather around it.

const ITEMS = [
  {label: 'Frozen capture', Icon: Snowflake, side: -1, row: 0},
  {label: 'Meaning in context', Icon: Languages, side: 1, row: 0},
  {label: 'Word or phrase', Icon: TextSelect, side: -1, row: 1},
  {label: 'Pronunciation', Icon: Volume2, side: 1, row: 1},
  {label: 'Controller ready', Icon: Gamepad2, side: -1, row: 2},
  {label: 'Liquid Glass', Icon: Layers, side: 1, row: 2},
];

const SCREEN_SCALE = 0.38;
// Game clock at the first frame of this scene, so the game keeps running across the cut.
const T0 = SCENE.demo.duration - (DEMO.unfreeze - DEMO.freeze);

export const Features: React.FC = () => {
  const f = useCurrentFrame();
  const shrink = ramp(f, 0, 26, EASE.inOut);
  const collapse = ramp(f, FEATURES.collapse, FEATURES.collapse + 12, EASE.in);
  const scale = interpolate(shrink, [0, 1], [1, SCREEN_SCALE]) * interpolate(collapse, [0, 1], [1, 0.12]);
  const radius = interpolate(shrink, [0, 1], [0, 34]) / scale;

  return (
    <AbsoluteFill style={{overflow: 'hidden'}}>
      <Aurora t={f + 600} strength={shrink} />

      {/* Floating game screen. */}
      <div
        style={{
          position: 'absolute',
          left: 0,
          top: 0,
          width: 1920,
          height: 1080,
          transformOrigin: '50% 50%',
          scale,
          rotate: `${interpolate(shrink, [0, 1], [0, -1.2])}deg`,
          borderRadius: radius,
          overflow: 'hidden',
          boxShadow: `0 ${60 / scale}px ${140 / scale}px rgba(0,0,0,${0.6 * shrink}), 0 0 0 ${2 / scale}px rgba(255,255,255,${0.35 * shrink}), 0 0 ${120 / scale}px rgba(10,132,255,${0.35 * shrink})`,
          opacity: 1 - ramp(f, FEATURES.collapse + 6, FEATURES.collapse + 12),
        }}
      >
        <GameScene t={T0 + f} />
      </div>

      {/* Feature pills. */}
      {ITEMS.map((it, i) => {
        const at = FEATURES.pops[i];
        const p = pop(f, at, {damping: 12, stiffness: 190, mass: 0.6});
        const y = 350 + it.row * 190;
        const x = 960 + it.side * ((1920 * SCREEN_SCALE) / 2 + 52);
        const flyX = (960 - x) * collapse;
        const flyY = (540 - y) * collapse;
        return (
          <Glass
            key={it.label}
            radius={44}
            tone="dark"
            blur={24}
            sweep={interpolate(f, [at + 4, at + 26], [0, 1], CLAMP)}
            style={{
              left: x,
              top: y,
              height: 88,
              padding: '0 32px 0 16px',
              display: 'flex',
              alignItems: 'center',
              translate: `${it.side < 0 ? '-100%' : '0%'} -50%`,
              marginLeft: (1 - p) * -it.side * 140 + flyX,
              marginTop: flyY,
              scale: interpolate(p, [0, 1], [0.6, 1]) * (1 - collapse * 0.8),
              opacity: Math.min(1, p * 1.5) * (1 - collapse),
            }}
          >
            <div style={{display: 'flex', alignItems: 'center', gap: 18}}>
              <div
                style={{
                  width: 56,
                  height: 56,
                  borderRadius: 28,
                  background: `linear-gradient(145deg, #4fb0ff, ${COLOR.accent} 50%, #6a4dff)`,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  boxShadow: '0 6px 20px rgba(10,132,255,0.5), inset 0 1px 0 rgba(255,255,255,0.5)',
                }}
              >
                <it.Icon size={29} color="#fff" strokeWidth={2.2} />
              </div>
              <div style={{fontFamily: FONT.display, fontWeight: 600, fontSize: 36, letterSpacing: '-0.02em', color: '#fff', whiteSpace: 'nowrap'}}>{it.label}</div>
            </div>
          </Glass>
        );
      })}

      {/* The screen becomes the app mark. */}
      {collapse > 0 ? (
        <AbsoluteFill style={{alignItems: 'center', justifyContent: 'center'}}>
          <LogoMark size={150} style={{scale: interpolate(collapse, [0, 1], [0.2, 1]), opacity: ramp(f, FEATURES.collapse + 4, FEATURES.collapse + 10)}} />
        </AbsoluteFill>
      ) : null}
    </AbsoluteFill>
  );
};
