import {useLayoutEffect, useRef, useState} from 'react';
import {useDelayRender} from 'remotion';
import {gameFontsReady} from '../theme';

export type Box = {x: number; y: number; w: number; h: number};
export type OcrWord = Box & {id: string; text: string};
export type OcrLayout = {words: OcrWord[]; byId: Record<string, OcrWord>; blocks: Record<string, Box>};

/** Position of `el` inside `root`, in untransformed layout pixels (camera moves do not change it). */
const offsetWithin = (el: HTMLElement, root: HTMLElement): Box => {
  let x = 0;
  let y = 0;
  let node: HTMLElement | null = el;
  while (node && node !== root) {
    x += node.offsetLeft;
    y += node.offsetTop;
    node = node.offsetParent as HTMLElement | null;
  }
  return {x, y, w: el.offsetWidth, h: el.offsetHeight};
};

/**
 * Plays the part of Windows OCR: measures every `[data-ocr]` word and `[data-block]` text block
 * of the game once its fonts are ready. Rendering waits until the boxes are known.
 */
export const useOcrLayout = () => {
  const rootRef = useRef<HTMLDivElement>(null);
  const {delayRender, continueRender, cancelRender} = useDelayRender();
  const [handle] = useState(() => delayRender('Measuring the game text'));
  const [layout, setLayout] = useState<OcrLayout | null>(null);

  useLayoutEffect(() => {
    let alive = true;
    Promise.all([gameFontsReady(), document.fonts.ready])
      .then(() => {
        if (!alive) return;
        const root = rootRef.current;
        if (!root) throw new Error('The game root is not mounted.');
        const words = Array.from(root.querySelectorAll<HTMLElement>('[data-ocr]')).map((el) => ({
          id: el.dataset.ocr ?? '',
          text: el.textContent ?? '',
          ...offsetWithin(el, root),
        }));
        const blocks: Record<string, Box> = {};
        root.querySelectorAll<HTMLElement>('[data-block]').forEach((el) => {
          blocks[el.dataset.block ?? ''] = offsetWithin(el, root);
        });
        setLayout({words, byId: Object.fromEntries(words.map((w) => [w.id, w])), blocks});
        continueRender(handle);
      })
      .catch((err) => cancelRender(err));
    return () => {
      alive = false;
    };
  }, [cancelRender, continueRender, handle]);

  return {rootRef, layout};
};

export const union = (...boxes: Box[]): Box => {
  const x = Math.min(...boxes.map((b) => b.x));
  const y = Math.min(...boxes.map((b) => b.y));
  const r = Math.max(...boxes.map((b) => b.x + b.w));
  const bottom = Math.max(...boxes.map((b) => b.y + b.h));
  return {x, y, w: r - x, h: bottom - y};
};

export const inflate = (b: Box, dx: number, dy: number): Box => ({x: b.x - dx, y: b.y - dy, w: b.w + 2 * dx, h: b.h + 2 * dy});

export const lerpBox = (a: Box, b: Box, t: number): Box => ({
  x: a.x + (b.x - a.x) * t,
  y: a.y + (b.y - a.y) * t,
  w: a.w + (b.w - a.w) * t,
  h: a.h + (b.h - a.h) * t,
});
