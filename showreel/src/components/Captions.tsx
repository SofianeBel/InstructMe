import React from 'react';
import {interpolate} from 'remotion';
import {CLAMP, EASE, ramp} from '../lib/anim';
import {FONT} from '../theme';

export type Step = {text: string; at: number};

/**
 * Big stacked captions on the left of the frame. Each new line rolls up into view,
 * older lines dim, and the whole stack leaves at `exitAt`.
 */
export const StepStack: React.FC<{f: number; steps: Step[]; exitAt: number; top: number; size?: number; dimPrevious?: boolean}> = ({
  f,
  steps,
  exitAt,
  top,
  size = 118,
  dimPrevious = true,
}) => {
  const exit = ramp(f, exitAt, exitAt + 12, EASE.in);
  return (
    <div
      style={{
        position: 'absolute',
        left: 118,
        top,
        fontFamily: FONT.display,
        fontWeight: 800,
        fontSize: size,
        letterSpacing: '-0.045em',
        opacity: 1 - exit,
        translate: `0 ${-50 * exit}px`,
      }}
    >
      {steps.map((s, i) => {
        const next = steps[i + 1];
        const dim = next && dimPrevious ? ramp(f, next.at, next.at + 10) : 0;
        return (
          <div
            key={s.text}
            style={{
              lineHeight: '1.02em',
              opacity: ramp(f, s.at, s.at + 9),
              translate: `0 ${interpolate(f, [s.at, s.at + 16], [0.5, 0], {...CLAMP, easing: EASE.out})}em`,
              filter: `blur(${interpolate(f, [s.at, s.at + 10], [12, 0], CLAMP)}px)`,
              color: `rgba(255,255,255,${1 - 0.62 * dim})`,
              textShadow: `0 6px 36px rgba(0,0,0,${0.3 * (1 - dim)})`,
            }}
          >
            {s.text}
          </div>
        );
      })}
    </div>
  );
};
