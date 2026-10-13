import type { CapacitorConfig } from '@capacitor/cli';

const config: CapacitorConfig = {
  appId: 'com.talkpro.mimic',
  appName: '말투 AI',
  webDir: 'dist',
  android: { allowMixedContent: false, webContentsDebuggingEnabled: false },
};

export default config;
