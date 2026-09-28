// Single source of truth for timing. Every value is a frame number at 60 fps.
// scripts/soundtrack.py reads this file too, so picture and sound stay in sync.
// Music: 120 BPM, so one beat = 30 frames and one bar = 120 frames (2 s).

export const FPS = 60;
export const WIDTH = 1920;
export const HEIGHT = 1080;
export const TOTAL = 900; // 15 s
export const BEAT = 30;

// Scene boundaries on the main timeline.
export const SCENE = {
  hook: {from: 0, duration: 120},
  demo: {from: 120, duration: 510},
  features: {from: 630, duration: 90},
  end: {from: 720, duration: 180},
} as const;

// Hook: game words flash on 8th notes, then the question.
export const HOOK = {
  flashes: [0, 15, 30, 45, 60],
  flashLength: 15,
  question: 75,
} as const;

// Demo: one continuous camera move over the game. Frames are local to the scene.
export const DEMO = {
  typeStart: 8,
  typeEnd: 58,
  keysIn: 70,
  ctrl: 90,
  alt: 105,
  l: 120,
  freeze: 120, // L goes down: the screen freezes (4.0 s)
  pill: 128,
  hint: 132,
  scanStart: 138,
  scanEnd: 180,
  pillReady: 182,
  cursorIn: 168,
  cursorArrive: 212,
  click: 225, // mouse click on "reach"
  cardOpen: 240, // the beat drops (6.0 s)
  speakerMove: 282,
  speakerClick: 312,
  voice: 315, // "reach" is spoken
  padIn: 352,
  down: 360, // D-pad down: "reach" -> "give"
  rb: 390, // RB: "give" -> "give up"
  a: 420, // A: open the card for "give up"
  b: 480, // B: back to the game
  unfreeze: 488,
} as const;

// Feature burst. Frames are local to the scene.
export const FEATURES = {
  pops: [8, 15, 23, 30, 38, 45],
  collapse: 78,
} as const;

// End card. Frames are local to the scene.
export const END = {
  logo: 0,
  wordmark: 8,
  tagline: 30,
  select: 75,
  decode: 82,
  decodeEnd: 112,
  credits: 118,
} as const;

/** Converts a scene-local frame to a main-timeline frame. */
export const at = (scene: keyof typeof SCENE, local: number) => SCENE[scene].from + local;
