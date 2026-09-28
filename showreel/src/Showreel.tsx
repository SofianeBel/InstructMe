import {Audio} from '@remotion/media';
import React from 'react';
import {AbsoluteFill, getStaticFiles, Series, staticFile} from 'remotion';
import {Grain} from './components/Effects';
import {Demo} from './scenes/Demo';
import {EndCard} from './scenes/EndCard';
import {Features} from './scenes/Features';
import {Hook} from './scenes/Hook';

const hasSoundtrack = getStaticFiles().some((file) => file.name === 'soundtrack.wav');

export const Showreel: React.FC = () => (
  <AbsoluteFill style={{backgroundColor: '#000'}}>
    <Series>
      <Series.Sequence name="Hook" durationInFrames={120}>
        <Hook />
      </Series.Sequence>
      <Series.Sequence name="Demo" durationInFrames={510}>
        <Demo />
      </Series.Sequence>
      <Series.Sequence name="Features" durationInFrames={90}>
        <Features />
      </Series.Sequence>
      <Series.Sequence name="End card" durationInFrames={180}>
        <EndCard />
      </Series.Sequence>
    </Series>
    <Grain />
    {hasSoundtrack ? <Audio src={staticFile('soundtrack.wav')} /> : null}
  </AbsoluteFill>
);
