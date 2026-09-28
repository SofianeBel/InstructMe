import {loadFont as loadBlackOps} from '@remotion/google-fonts/BlackOpsOne';
import {loadFont as loadCinzel} from '@remotion/google-fonts/Cinzel';
import {loadFont as loadCreepster} from '@remotion/google-fonts/Creepster';
import {loadFont as loadInstrumentSerif} from '@remotion/google-fonts/InstrumentSerif';
import {loadFont as loadInter} from '@remotion/google-fonts/Inter';
import {loadFont as loadInterTight} from '@remotion/google-fonts/InterTight';
import {loadFont as loadOrbitron} from '@remotion/google-fonts/Orbitron';
import {loadFont as loadPressStart} from '@remotion/google-fonts/PressStart2P';
import {loadFont as loadRajdhani} from '@remotion/google-fonts/Rajdhani';
import {loadFont as loadSpectral} from '@remotion/google-fonts/Spectral';

const interTight = loadInterTight('normal', {weights: ['500', '600', '700', '800'], subsets: ['latin', 'latin-ext']});
const inter = loadInter('normal', {weights: ['400', '500', '600'], subsets: ['latin', 'latin-ext']});
const instrument = loadInstrumentSerif('italic', {weights: ['400'], subsets: ['latin', 'latin-ext']});
const spectral = loadSpectral('normal', {weights: ['500'], subsets: ['latin']});
const cinzel = loadCinzel('normal', {weights: ['700', '900'], subsets: ['latin']});
const rajdhani = loadRajdhani('normal', {weights: ['600', '700'], subsets: ['latin']});
const orbitron = loadOrbitron('normal', {weights: ['900'], subsets: ['latin']});
const pressStart = loadPressStart('normal', {weights: ['400'], subsets: ['latin']});
const blackOps = loadBlackOps('normal', {weights: ['400'], subsets: ['latin']});
const creepster = loadCreepster('normal', {weights: ['400'], subsets: ['latin']});

export const FONT = {
  display: interTight.fontFamily,
  serif: instrument.fontFamily,
  // The app itself uses Segoe UI Variable; Inter is the fallback outside Windows.
  ui: `"Segoe UI Variable Display", "Segoe UI Variable Text", "Segoe UI", ${inter.fontFamily}, sans-serif`,
  body: inter.fontFamily,
  gameText: spectral.fontFamily,
  gameTitle: cinzel.fontFamily,
  hud: rajdhani.fontFamily,
  scifi: orbitron.fontFamily,
  pixel: pressStart.fontFamily,
  tactical: blackOps.fontFamily,
  horror: creepster.fontFamily,
};

/** Resolves when every font used by the game text is ready, so word boxes can be measured. */
export const gameFontsReady = () =>
  Promise.all([spectral.waitUntilDone(), cinzel.waitUntilDone(), rajdhani.waitUntilDone(), inter.waitUntilDone()]);

// Colors from the app (OverlayWindow.xaml) plus the brand night palette.
export const COLOR = {
  ink: '#16181D',
  inkSoft: '#454A55',
  meaning: '#2A2E36',
  accent: '#0A84FF',
  night: '#04050A',
  violet: '#7C5CFF',
  cyan: '#40D8FF',
  gold: '#E2C27D',
};

// The overlay is drawn at 125 % of the app's size, like a Windows display set to 125 % scaling.
export const UI_SCALE = 1.25;
