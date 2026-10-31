import type { CapacitorConfig } from '@capacitor/cli';

// LAN test build: the WebView loads the React app straight from the Docker API on the Mac,
// so pages and /api/v1 share one origin and the existing cookie + CSRF flow works unchanged.
// scripts/build-android-lan.sh sets GOMOKU_LAN_URL (e.g. http://192.168.1.10:58080) before `cap sync`.
const lanUrl = process.env.GOMOKU_LAN_URL;

const config: CapacitorConfig = {
  appId: 'com.kiotrandev.gomoku',
  appName: 'Gomoku',
  // The LAN build ships only a small offline page (generated from android-lan-shell/) instead of the 28MB web build.
  webDir: lanUrl ? '.android-lan-shell' : 'dist',
  server: lanUrl ? { url: lanUrl, cleartext: lanUrl.startsWith('http://'), errorPath: 'index.html' } : undefined,
  plugins: {
    // LIGHT = dark status-bar icons over the light page background.
    SystemBars: { insetsHandling: 'css', initialViewportFitValueHint: 'cover', style: 'LIGHT' },
  },
};

export default config;
