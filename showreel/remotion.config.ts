/**
 * Note: When using the Node.JS APIs, the config file
 * doesn't apply. Instead, pass options directly to the APIs.
 *
 * All configuration options: https://remotion.dev/docs/config
 */

import { Config } from "@remotion/cli/config";

Config.setRspack(true);
// GPU compositing (D3D11 through ANGLE) keeps the glass blur and gradients fast and smooth.
Config.setChromiumOpenGlRenderer("angle");
// Lossless frames and BT.709 limited range: the colors match the browser in every player.
Config.setVideoImageFormat("png");
Config.setColorSpace("bt709");
Config.setCrf(16);
Config.setOverwriteOutput(true);
