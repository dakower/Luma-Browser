import puppeteer from 'puppeteer-core';
import path from 'path';
import { fileURLToPath } from 'url';
import fs from 'fs';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const htmlPath = path.join(__dirname, 'index.html');

console.log('Launching Luma Showcase in Microsoft Edge...');

const browser = await puppeteer.launch({
  executablePath: edgePath,
  headless: false,
  defaultViewport: { width: 1920, height: 1080 },
  args: [
    '--window-size=1920,1080',
    '--autoplay-policy=no-user-gesture-required',
    '--disable-infobars',
    '--app=' + htmlPath
  ]
});

console.log('Showcase is running live in 1080p 60fps!');
