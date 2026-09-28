import React from 'react';
import {AbsoluteFill} from 'remotion';
import {Dialog, DIALOG_CHARS} from './Dialog';
import {HealthPanel, Minimap, QuestTracker} from './Hud';
import {Landscape} from './Landscape';

/** The fictional game, 1920×1080. `t` is the game clock: it stops while the screen is frozen. */
export const GameScene: React.FC<{t: number; chars?: number}> = ({t, chars = DIALOG_CHARS}) => (
  <AbsoluteFill style={{overflow: 'hidden', backgroundColor: '#05040a'}}>
    <Landscape t={t} />
    <HealthPanel />
    <Minimap t={t} />
    <QuestTracker />
    <Dialog t={t} chars={chars} />
  </AbsoluteFill>
);
