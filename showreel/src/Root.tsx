import './index.css';
import {Composition, Folder} from 'remotion';
import {Demo} from './scenes/Demo';
import {EndCard} from './scenes/EndCard';
import {Features} from './scenes/Features';
import {Hook} from './scenes/Hook';
import {Showreel} from './Showreel';

export const RemotionRoot: React.FC = () => {
  return (
    <>
      <Composition id="Showreel" component={Showreel} durationInFrames={900} fps={60} width={1920} height={1080} />
      <Folder name="Scenes">
        <Composition id="Hook" component={Hook} durationInFrames={120} fps={60} width={1920} height={1080} />
        <Composition id="Demo" component={Demo} durationInFrames={510} fps={60} width={1920} height={1080} />
        <Composition id="Features" component={Features} durationInFrames={90} fps={60} width={1920} height={1080} />
        <Composition id="EndCard" component={EndCard} durationInFrames={180} fps={60} width={1920} height={1080} />
      </Folder>
    </>
  );
};
