import React from 'react';
import {AbsoluteFill, interpolate, useCurrentFrame} from 'remotion';
import {StepStack} from '../components/Captions';
import {Cursor} from '../components/Cursor';
import {CARD_HEIGHT, CARD_WIDTH, DefinitionCard, GIVE_UP, REACH, SPEAKER_OFFSET} from '../components/DefinitionCard';
import {Vignette} from '../components/Effects';
import {Glass} from '../components/Glass';
import {Keycap} from '../components/Keycap';
import {PadHud} from '../components/PadHud';
import {DIALOG_CHARS} from '../game/Dialog';
import {GameScene} from '../game/GameScene';
import {camAt, CLAMP, EASE, pop, ramp, shake, type Cam} from '../lib/anim';
import {inflate, lerpBox, union, useOcrLayout, type Box} from '../lib/useOcrLayout';
import {COLOR, FONT, UI_SCALE as S} from '../theme';
import {DEMO} from '../timeline';

/** Game clock: runs, stops at the freeze, resumes after B. */
export const demoGameTime = (f: number) =>
  f < DEMO.freeze ? f : f < DEMO.unfreeze ? DEMO.freeze : f - (DEMO.unfreeze - DEMO.freeze);

/** Camera that puts world point (wx, wy) at screen point (sx, sy). */
const frameOn = (wx: number, wy: number, sx: number, sy: number, z: number): Cam => ({x: wx - (sx - 960) / z, y: wy - (sy - 540) / z, z});

const toWorld = (cam: Cam, x: number, y: number) => ({x: cam.x + (x - 960) / cam.z, y: cam.y + (y - 540) / cam.z});

const quad = (a: {x: number; y: number}, c: {x: number; y: number}, b: {x: number; y: number}, t: number) => ({
  x: (1 - t) * (1 - t) * a.x + 2 * (1 - t) * t * c.x + t * t * b.x,
  y: (1 - t) * (1 - t) * a.y + 2 * (1 - t) * t * c.y + t * t * b.y,
});

const KEYS = [
  {label: 'Ctrl', width: 210, at: DEMO.ctrl},
  {label: 'Alt', width: 190, at: DEMO.alt},
  {label: 'L', width: 150, at: DEMO.l},
];
const KEY_GAP = 20;
const PLUS = 30;
const KEYS_WIDTH = KEYS.reduce((w, k) => w + k.width, 0) + 2 * PLUS + 4 * KEY_GAP;
const KEYS_TOP = 470;
const L_CENTER = {x: 960 + KEYS_WIDTH / 2 - 75, y: KEYS_TOP + 80};
// The keys blow away just after the freeze, so the lit L stays readable for a moment.
const BLAST = DEMO.freeze + 2;

const PILL_TEXT = (f: number) => (f < DEMO.scanStart ? 'Capture…' : f < DEMO.pillReady ? 'Détection du texte…' : 'Lecture · Capture figée');

export const Demo: React.FC = () => {
  const f = useCurrentFrame();
  const {rootRef, layout} = useOcrLayout();
  const t = demoGameTime(f);
  const chars = interpolate(f, [DEMO.typeStart, DEMO.typeEnd], [0, DIALOG_CHARS], CLAMP);

  // Measured geometry in world pixels, with fallbacks for the first layout pass.
  const reach = layout?.byId.reach ?? {x: 860, y: 826, w: 100, h: 52};
  const give = layout?.byId.give ?? {x: 860, y: 878, w: 80, h: 52};
  const up = layout?.byId.up ?? {x: 950, y: 878, w: 44, h: 52};
  const block = layout?.blocks.dialog ?? {x: 496, y: 826, w: 1000, h: 104};

  // Same rules as the app: the box is the word inflated by (5, 4); the card goes above the text,
  // aligned 12 px left of the word (OverlayWindow.Select and CardPlacement.Place).
  const selReach = inflate(reach, 5 * S, 4 * S);
  const selGive = inflate(give, 5 * S, 4 * S);
  const selGiveUp = inflate(union(give, up), 5 * S, 4 * S);
  const cardX = (word: Box) => Math.min(Math.max(word.x - 12 * S, 16 * S), 1920 - CARD_WIDTH - 16 * S);
  const cardBottom = block.y - 14 * S;
  const cardA = {x: cardX(reach), y: cardBottom - CARD_HEIGHT};
  const cardB = {x: cardX(give), y: cardBottom - CARD_HEIGHT};

  // ---- Camera ----
  const readY = (cardBottom - CARD_HEIGHT + reach.y + reach.h) / 2;
  const base = camAt(f, [
    [0, {x: 1010, y: 500, z: 1.16}],
    [118, {x: 960, y: 540, z: 1.03}],
    [134, {x: 960, y: 540, z: 1.03}],
    [210, frameOn(reach.x + reach.w / 2, block.y + 30, 1190, 650, 1.42)],
    [232, frameOn(reach.x + reach.w / 2, block.y + 20, 1240, 690, 1.52)],
    [266, frameOn(cardA.x + CARD_WIDTH / 2, readY, 1330, 560, 1.8)],
    [352, frameOn(cardA.x + CARD_WIDTH / 2, readY, 1325, 556, 1.87)],
    [372, frameOn(cardB.x + CARD_WIDTH / 2, readY + 10, 1330, 560, 1.8)],
    [480, frameOn(cardB.x + CARD_WIDTH / 2, readY + 10, 1325, 556, 1.86)],
    [506, {x: 960, y: 540, z: 1.0}],
  ]);
  const s1 = shake(f, DEMO.freeze, 18, 14);
  const s2 = shake(f, DEMO.cardOpen, 12, 5);
  const s3 = shake(f, DEMO.click, 8, 2.5);
  const cam: Cam = {x: base.x - (s1.x + s2.x + s3.x) / base.z, y: base.y - (s1.y + s2.y + s3.y) / base.z, z: base.z};

  // ---- Freeze wave ----
  const frozen = f >= DEMO.freeze && f < DEMO.unfreeze + 14;
  const waveR = interpolate(f, [DEMO.freeze, DEMO.freeze + 34], [100, 2600], {...CLAMP, easing: EASE.out});
  const waveWorld = toWorld(cam, L_CENTER.x, L_CENTER.y);
  const tintOut = 1 - ramp(f, DEMO.unfreeze - 2, DEMO.unfreeze + 12, EASE.inOut);

  // ---- Overlay UI (world space) ----
  const dissolve = ramp(f, DEMO.b, DEMO.b + 12, EASE.in);
  const uiAlpha = 1 - dissolve;
  const pillIn = pop(f, DEMO.pill, {damping: 15, stiffness: 160});
  const hintIn = pop(f, DEMO.hint, {damping: 15, stiffness: 160});

  const scanX = interpolate(f, [DEMO.scanStart, DEMO.scanEnd], [-80, 2000], {...CLAMP, easing: EASE.inOut});
  const scanAlpha = interpolate(f, [DEMO.scanStart, DEMO.scanStart + 4, DEMO.scanEnd - 6, DEMO.scanEnd], [0, 1, 1, 0], CLAMP);
  const boxesResidual = interpolate(f, [DEMO.scanEnd, DEMO.scanEnd + 18, DEMO.click, DEMO.click + 10], [1, 0.2, 0.2, 0], CLAMP);

  const glide = pop(f, DEMO.down + 1, {damping: 14, stiffness: 190, mass: 0.6});
  const extend = pop(f, DEMO.rb + 1, {damping: 14, stiffness: 190, mass: 0.6});
  const sel = lerpBox(lerpBox(selReach, selGive, glide), selGiveUp, extend);
  const selIn = pop(f, DEMO.click, {damping: 12, stiffness: 220, mass: 0.6});

  const hover = interpolate(f, [DEMO.cursorArrive - 2, DEMO.cursorArrive + 4, DEMO.click, DEMO.click + 4], [0, 1, 1, 0], CLAMP);

  const openA = pop(f, DEMO.cardOpen, {damping: 14, stiffness: 150, mass: 0.8});
  const closeA = ramp(f, DEMO.down, DEMO.down + 9, EASE.in);
  const openB = pop(f, DEMO.a, {damping: 14, stiffness: 150, mass: 0.8});

  // ---- Cursor ----
  const start = {x: 1580, y: 1090};
  const onWord = {x: reach.x + reach.w * 0.6, y: reach.y + reach.h * 0.66};
  const speaker = {x: cardA.x + SPEAKER_OFFSET.x + 4, y: cardA.y + SPEAKER_OFFSET.y + 6};
  const p1 = ramp(f, DEMO.cursorIn, DEMO.cursorArrive, EASE.inOut);
  const p2 = ramp(f, DEMO.speakerMove, DEMO.speakerClick - 6, EASE.inOut);
  let cursor = quad(start, {x: onWord.x + 280, y: onWord.y + 40}, onWord, p1);
  if (f >= DEMO.speakerMove) cursor = quad(onWord, {x: onWord.x + 60, y: speaker.y + 40}, speaker, p2);
  const cursorAlpha = interpolate(f, [DEMO.cursorIn, DEMO.cursorIn + 6, DEMO.padIn - 8, DEMO.padIn], [0, 1, 1, 0], CLAMP);
  const cursorPress =
    interpolate(f, [DEMO.click, DEMO.click + 3, DEMO.click + 9], [0, 1, 0], CLAMP) +
    interpolate(f, [DEMO.speakerClick, DEMO.speakerClick + 3, DEMO.speakerClick + 9], [0, 1, 0], CLAMP);
  const speakerHover = interpolate(f, [DEMO.speakerClick - 8, DEMO.speakerClick - 2, DEMO.padIn - 8, DEMO.padIn], [0, 1, 1, 0], CLAMP);

  // ---- Controller ----
  const blink = (at: number) => interpolate(f, [at, at + 2, at + 16], [0, 1, 0], CLAMP);
  const lit = {down: blink(DEMO.down), rb: blink(DEMO.rb), a: blink(DEMO.a), b: blink(DEMO.b)};
  const action =
    f < DEMO.rb ? '▼  Move to a word' : f < DEMO.a ? 'RB  Grow to a phrase' : f < DEMO.b ? 'A  Show the meaning' : 'B  Back to the game';
  const actionKey = f < DEMO.rb ? 0 : f < DEMO.a ? 1 : f < DEMO.b ? 2 : 3;
  const actionAt = [DEMO.padIn, DEMO.rb, DEMO.a, DEMO.b][actionKey];
  const padIn = pop(f, DEMO.padIn, {damping: 16, stiffness: 140});
  const padOut = ramp(f, DEMO.b + 12, DEMO.b + 26, EASE.in);

  const worldTransform = `translate(960px, 540px) scale(${cam.z}) translate(${-cam.x}px, ${-cam.y}px)`;

  return (
    <AbsoluteFill style={{backgroundColor: '#000', overflow: 'hidden'}}>
      <div ref={rootRef} style={{position: 'absolute', left: 0, top: 0, width: 1920, height: 1080, transformOrigin: '0 0', transform: worldTransform}}>
        <GameScene t={t} chars={chars} />

        {/* Freeze grade: spreads from the L key as a wave, lifts after B. */}
        {frozen ? (
          <div
            style={{
              position: 'absolute',
              inset: -400,
              backdropFilter: 'saturate(0.7) brightness(0.93) contrast(1.06)',
              backgroundColor: 'rgba(96,150,255,0.10)',
              mask: `radial-gradient(circle at ${waveWorld.x + 400}px ${waveWorld.y + 400}px, #000 ${Math.max(0, waveR / cam.z - 90)}px, transparent ${waveR / cam.z}px)`,
              opacity: tintOut,
            }}
          />
        ) : null}

        {/* Scan line and word boxes: what Windows OCR finds. */}
        {f >= DEMO.scanStart && f < DEMO.click + 12 && layout
          ? layout.words.map((w) => {
              const seen = interpolate(scanX - (w.x + w.w / 2), [0, 140], [0, 1], CLAMP);
              if (seen <= 0) return null;
              return (
                <div
                  key={w.id}
                  style={{
                    position: 'absolute',
                    left: w.x - 3 * S,
                    top: w.y - 2 * S,
                    width: w.w + 6 * S,
                    height: w.h + 4 * S,
                    borderRadius: 5 * S,
                    border: '1.5px solid rgba(150,205,255,0.95)',
                    backgroundColor: 'rgba(10,132,255,0.16)',
                    boxShadow: '0 0 14px rgba(10,132,255,0.45)',
                    opacity: seen * boxesResidual,
                    scale: interpolate(seen, [0, 1], [1.35, 1], {easing: EASE.out}),
                  }}
                />
              );
            })
          : null}
        {scanAlpha > 0 ? (
          <div style={{position: 'absolute', left: scanX - 260, top: -40, width: 264, height: 1160, opacity: scanAlpha}}>
            <div style={{position: 'absolute', inset: 0, background: 'linear-gradient(90deg, rgba(10,132,255,0), rgba(10,132,255,0.22))'}} />
            <div style={{position: 'absolute', right: 0, top: 0, bottom: 0, width: 4, background: '#e8f4ff', boxShadow: '0 0 24px 6px rgba(80,170,255,0.9)'}} />
          </div>
        ) : null}

        {/* Hover: the app's white highlight under the mouse. */}
        {hover > 0 ? (
          <div
            style={{
              position: 'absolute',
              left: reach.x - 3 * S,
              top: reach.y - 3 * S,
              width: reach.w + 6 * S,
              height: reach.h + 6 * S,
              borderRadius: 5 * S,
              backgroundColor: 'rgba(255,255,255,0.15)',
              border: '1.25px solid rgba(255,255,255,0.55)',
              opacity: hover,
            }}
          />
        ) : null}

        {/* Selection: blue box with a glow, gliding and growing with the controller. */}
        {f >= DEMO.click && uiAlpha > 0 ? (
          <>
            <div
              style={{
                position: 'absolute',
                left: sel.x,
                top: sel.y,
                width: sel.w,
                height: sel.h,
                borderRadius: 7 * S,
                border: `${2.5 * S}px solid ${COLOR.accent}`,
                backgroundColor: 'rgba(10,132,255,0.18)',
                boxShadow: `0 0 ${16 * S}px rgba(10,132,255,0.85), inset 0 0 ${8 * S}px rgba(10,132,255,0.35)`,
                opacity: Math.min(1, selIn * 1.4) * uiAlpha,
                scale: interpolate(selIn, [0, 1], [1.3, 1]),
              }}
            />
          </>
        ) : null}

        {/* Click ripple under the cursor. */}
        {f >= DEMO.click && f < DEMO.click + 20 ? (
          <div
            style={{
              position: 'absolute',
              left: onWord.x - 40,
              top: onWord.y - 40,
              width: 80,
              height: 80,
              borderRadius: '50%',
              border: '2.5px solid rgba(255,255,255,0.9)',
              opacity: interpolate(f, [DEMO.click, DEMO.click + 18], [0.9, 0], CLAMP),
              scale: interpolate(f, [DEMO.click, DEMO.click + 18], [0.2, 1.2], {...CLAMP, easing: EASE.out}),
            }}
          />
        ) : null}

        {/* Card for "reach". Anchored by its bottom edge, so it grows upward like the app re-placing it. */}
        {f >= DEMO.cardOpen && f < DEMO.down + 10 ? (
          <DefinitionCard
            def={REACH}
            f={f - DEMO.cardOpen}
            speak={f >= DEMO.speakerClick ? f - DEMO.speakerClick : null}
            hover={speakerHover}
            style={{
              left: cardA.x,
              top: cardBottom,
              translate: `0 calc(-100% + ${(1 - openA) * 60}px)`,
              transformOrigin: `${reach.x - cardA.x + reach.w / 2}px 100%`,
              scale: interpolate(openA, [0, 1], [0.35, 1]) * (1 - 0.06 * closeA),
              opacity: Math.min(1, openA * 1.6) * (1 - closeA),
            }}
          />
        ) : null}

        {/* Card for "give up", opened with A. */}
        {f >= DEMO.a && uiAlpha > 0 ? (
          <DefinitionCard
            def={GIVE_UP}
            f={f - DEMO.a}
            loading={10}
            style={{
              left: cardB.x,
              top: cardBottom,
              translate: `0 calc(-100% + ${(1 - openB) * 60}px)`,
              transformOrigin: `${give.x - cardB.x + give.w}px 100%`,
              scale: interpolate(openB, [0, 1], [0.35, 1]) * (1 - 0.04 * dissolve),
              opacity: Math.min(1, openB * 1.6) * uiAlpha,
            }}
          />
        ) : null}

        {/* App chrome: status pill (top) and hint bar (bottom), both Liquid Glass. */}
        {f >= DEMO.pill && uiAlpha > 0 ? (
          <Glass
            radius={20 * S}
            style={{
              left: 960,
              top: 28 * S,
              padding: `${9 * S}px ${24 * S}px ${10 * S}px`,
              translate: `-50% ${(1 - pillIn) * -90}px`,
              opacity: Math.min(1, pillIn * 1.5) * uiAlpha,
              fontFamily: FONT.ui,
              fontSize: 17 * S,
              fontWeight: 500,
              color: COLOR.ink,
              whiteSpace: 'nowrap',
            }}
          >
            {PILL_TEXT(f)}
          </Glass>
        ) : null}
        {f >= DEMO.hint && uiAlpha > 0 ? (
          <Glass
            radius={22 * S}
            style={{
              left: 960,
              top: 1080 - 36 * S,
              padding: `${11 * S}px ${28 * S}px ${12 * S}px`,
              translate: `-50% calc(-100% + ${(1 - hintIn) * 90}px)`,
              opacity: Math.min(1, hintIn * 1.5) * uiAlpha,
              fontFamily: FONT.ui,
              fontSize: 15 * S,
              color: COLOR.ink,
              whiteSpace: 'pre',
            }}
          >
            {'Souris : sélectionner   ·   Maj + clic : étendre   ·   Manette : naviguer   ·   RB : étendre   ·   B / Échap : quitter'}
          </Glass>
        ) : null}

        {cursorAlpha > 0 ? <Cursor x={cursor.x} y={cursor.y} size={34 * S} press={Math.min(1, cursorPress)} opacity={cursorAlpha} /> : null}
      </div>

      {/* ---- Screen space ---- */}
      <AbsoluteFill style={{backgroundColor: '#fff', opacity: interpolate(f, [0, 9], [0.85, 0], CLAMP)}} />
      {frozen ? <Vignette opacity={0.3 * ramp(f, DEMO.freeze, DEMO.freeze + 20) * tintOut} color="20,60,140" /> : null}

      {/* Shortcut keys. */}
      {f >= DEMO.keysIn && f < BLAST + 16 ? (
        <>
          <AbsoluteFill
            style={{
              background: 'radial-gradient(ellipse 50% 40% at 50% 52%, rgba(3,4,10,0.6), rgba(3,4,10,0) 70%)',
              opacity: interpolate(f, [DEMO.keysIn, DEMO.keysIn + 10, BLAST, BLAST + 10], [0, 1, 1, 0], CLAMP),
            }}
          />
          <div
            style={{
              position: 'absolute',
              left: 960 - KEYS_WIDTH / 2,
              top: KEYS_TOP,
              display: 'flex',
              alignItems: 'center',
              gap: KEY_GAP,
              scale: interpolate(f, [BLAST, BLAST + 14], [1, 1.28], {...CLAMP, easing: EASE.out}),
              opacity: interpolate(f, [BLAST, BLAST + 12], [1, 0], CLAMP),
              filter: `blur(${interpolate(f, [BLAST, BLAST + 12], [0, 14], CLAMP)}px)`,
            }}
          >
            {KEYS.map((k, i) => {
              const inT = pop(f, DEMO.keysIn + i * 4, {damping: 13, stiffness: 180, mass: 0.7});
              return (
                <React.Fragment key={k.label}>
                  {i > 0 ? (
                    <div style={{width: PLUS, textAlign: 'center', fontFamily: FONT.display, fontWeight: 500, fontSize: 48, color: 'rgba(255,255,255,0.7)', opacity: inT}}>+</div>
                  ) : null}
                  <Keycap
                    label={k.label}
                    width={k.width}
                    press={interpolate(f, [k.at - 4, k.at], [0, 1], CLAMP)}
                    style={{translate: `0 ${(1 - inT) * 140}px`, opacity: Math.min(1, inT * 1.5)}}
                  />
                </React.Fragment>
              );
            })}
          </div>
        </>
      ) : null}

      {/* The freeze: flash, a refracting wave and its bright ring. */}
      {f >= DEMO.freeze && f < DEMO.freeze + 40 ? (
        <>
          <div
            style={{
              position: 'absolute',
              left: L_CENTER.x - waveR,
              top: L_CENTER.y - waveR,
              width: waveR * 2,
              height: waveR * 2,
              borderRadius: '50%',
              backdropFilter: 'blur(10px) brightness(1.15) saturate(0.6)',
              mask: 'radial-gradient(circle closest-side, transparent 72%, #000 90%, transparent 100%)',
              opacity: interpolate(f, [DEMO.freeze, DEMO.freeze + 30], [1, 0], CLAMP),
            }}
          />
          <div
            style={{
              position: 'absolute',
              left: L_CENTER.x - waveR,
              top: L_CENTER.y - waveR,
              width: waveR * 2,
              height: waveR * 2,
              borderRadius: '50%',
              border: '3px solid rgba(220,238,255,0.95)',
              boxShadow: '0 0 50px 10px rgba(110,180,255,0.7), inset 0 0 50px 10px rgba(110,180,255,0.5)',
              opacity: interpolate(f, [DEMO.freeze, DEMO.freeze + 28], [1, 0], CLAMP),
            }}
          />
          <AbsoluteFill style={{backgroundColor: '#eaf4ff', opacity: interpolate(f, [DEMO.freeze, DEMO.freeze + 1, DEMO.freeze + 3, DEMO.freeze + 9], [0, 0.6, 0.16, 0], CLAMP)}} />
        </>
      ) : null}

      {/* Captions. */}
      {f >= DEMO.freeze && f < DEMO.padIn ? (
        <StepStack
          f={f}
          top={292}
          exitAt={DEMO.padIn - 12}
          steps={[
            {text: 'Freeze.', at: DEMO.freeze + 4},
            {text: 'Pick.', at: DEMO.cursorIn + 18},
            {text: 'Understand.', at: DEMO.cardOpen + 4},
          ]}
        />
      ) : null}
      {f >= DEMO.padIn - 4 && f < DEMO.b + 30 ? (
        <StepStack
          f={f}
          top={150}
          size={104}
          dimPrevious={false}
          exitAt={DEMO.b + 8}
          steps={[
            {text: 'Controller', at: DEMO.padIn - 2},
            {text: 'ready.', at: DEMO.padIn + 4},
          ]}
        />
      ) : null}

      {f >= DEMO.padIn && padOut < 1 ? (
        <PadHud
          lit={lit}
          action={action}
          actionKey={actionKey}
          actionIn={ramp(f, actionAt, actionAt + 10)}
          style={{
            left: 118,
            top: 400,
            translate: `${(1 - padIn) * -80 - padOut * 60}px 0`,
            opacity: Math.min(1, padIn * 1.4) * (1 - padOut),
          }}
        />
      ) : null}
    </AbsoluteFill>
  );
};
