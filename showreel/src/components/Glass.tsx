import React from 'react';

type GlassProps = {
  radius: number;
  tone?: 'light' | 'dark';
  blur?: number;
  /** 0 → 1 runs a light sweep across the glass once. */
  sweep?: number;
  style?: React.CSSProperties;
  children?: React.ReactNode;
};

/**
 * The app's Liquid Glass: a blurred view of what is behind, a light tint, a specular edge and a soft shadow.
 * Animate opacity and scale on this element itself: an opacity or filter on a parent would cut the blur off from the game.
 */
export const Glass: React.FC<GlassProps> = ({radius, tone = 'light', blur = 26, sweep = 0, style, children}) => {
  const light = tone === 'light';
  return (
    <div
      style={{
        position: 'absolute',
        borderRadius: radius,
        background: light
          ? 'linear-gradient(180deg, rgba(255,255,255,0.64) 0%, rgba(244,246,250,0.48) 100%)'
          : 'linear-gradient(180deg, rgba(255,255,255,0.17) 0%, rgba(255,255,255,0.06) 100%)',
        backdropFilter: `blur(${blur}px) saturate(${light ? 1.8 : 1.6})`,
        boxShadow: light
          ? '0 16px 48px rgba(11,16,32,0.38), 0 2px 8px rgba(11,16,32,0.16)'
          : '0 16px 48px rgba(0,0,0,0.45), 0 2px 8px rgba(0,0,0,0.3)',
        ...style,
      }}
    >
      <div
        style={{
          position: 'absolute',
          inset: 0,
          borderRadius: radius,
          padding: 1.6,
          background: light
            ? 'linear-gradient(135deg, rgba(255,255,255,0.98), rgba(255,255,255,0.28) 50%, rgba(255,255,255,0.66))'
            : 'linear-gradient(135deg, rgba(255,255,255,0.6), rgba(255,255,255,0.08) 50%, rgba(255,255,255,0.32))',
          mask: 'linear-gradient(#000 0 0) content-box, linear-gradient(#000 0 0)',
          maskComposite: 'exclude',
          pointerEvents: 'none',
        }}
      />
      <div
        style={{
          position: 'absolute',
          inset: 0,
          borderRadius: radius,
          background: `linear-gradient(180deg, rgba(255,255,255,${light ? 0.3 : 0.12}) 0%, rgba(255,255,255,0) 45%)`,
          pointerEvents: 'none',
        }}
      />
      {sweep > 0 && sweep < 1 ? (
        <div style={{position: 'absolute', inset: 0, borderRadius: radius, overflow: 'hidden', pointerEvents: 'none'}}>
          <div
            style={{
              position: 'absolute',
              top: '-60%',
              bottom: '-60%',
              width: '34%',
              left: `${-60 + sweep * 190}%`,
              rotate: '20deg',
              background: 'linear-gradient(90deg, rgba(255,255,255,0), rgba(255,255,255,0.6), rgba(255,255,255,0))',
            }}
          />
        </div>
      ) : null}
      <div style={{position: 'relative'}}>{children}</div>
    </div>
  );
};
