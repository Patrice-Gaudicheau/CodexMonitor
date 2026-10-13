// Documentation artwork only. Requires sharp; never changes application assets.
// Run with NODE_PATH pointing to your local sharp installation.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');
const sharp = require('sharp');

const tagline = 'Your Codex usage, always one glance away.';
const disclaimer = 'Unofficial community project. Not affiliated with, endorsed by, or sponsored by OpenAI.';
const supportingMessage = 'Your quota, right on the Windows taskbar.';
const supportingDetail = 'No CLI to open. No /status. No Codex app menus.';
const input = name => path.join(__dirname, name);
const sourceFiles = ['details-en.png', 'taskbar-tray.png'];
const digest = name => crypto.createHash('sha256').update(fs.readFileSync(input(name))).digest('hex');

async function main() {
  const sources = sourceFiles.map(file => ({ file, sha256: digest(file) }));
  const uiMetadata = await sharp(input('details-en.png')).metadata();
  const trayMetadata = await sharp(input('taskbar-tray.png')).metadata();
  assert.deepEqual([uiMetadata.width, uiMetadata.height], [536, 709]);
  assert.deepEqual([trayMetadata.width, trayMetadata.height], [420, 48]);

  const artwork = Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="640" viewBox="0 0 1280 640">
    <rect width="1280" height="640" fill="#F4FAF7"/>
    <rect x="0" y="0" width="1280" height="8" fill="#23B48F"/>
    <rect x="64" y="54" width="232" height="32" rx="16" fill="#E0F3EA"/>
    <g font-family="Segoe UI, sans-serif">
      <text x="180" y="75" text-anchor="middle" font-size="13" font-weight="600" letter-spacing="1.5" fill="#397361">WINDOWS TRAY UTILITY</text>
      <text x="64" y="171" font-size="70" font-weight="700" letter-spacing="-2.5" fill="#1D3B31">CodexMonitor</text>
      <text x="66" y="228" font-size="29" fill="#244A3C">${tagline}</text>
      <text x="67" y="281" font-size="27" font-weight="600" fill="#244A3C">${supportingMessage}</text>
      <text x="67" y="318" font-size="22" fill="#567367">${supportingDetail}</text>
      <rect x="64" y="348" width="776" height="153" rx="22" fill="#FFFFFF" stroke="#D3E7DE"/>
      <text x="86" y="383" font-size="22" font-weight="600" fill="#244A3C">Remaining quota, beside the clock.</text>
      <text x="814" y="383" text-anchor="end" font-size="13" fill="#567367">Actual Windows tray</text>
      <text x="66" y="535" font-size="13" font-weight="600" letter-spacing="1.6" fill="#397361">OPEN SOURCE · MIT</text>
      <text x="66" y="573" font-size="16" font-weight="600" fill="#3D6253">Unofficial community project.</text>
      <text x="66" y="598" font-size="15" fill="#567367">Not affiliated with, endorsed by, or sponsored by OpenAI.</text>
      <text x="1081" y="105" text-anchor="middle" font-size="17" font-weight="600" fill="#397361">Click for details</text>
      <rect x="927" y="122" width="308" height="397" rx="26" fill="#D1EDE0"/>
      <text x="1081" y="549" text-anchor="middle" font-size="13" fill="#567367">Verified interface · sample data</text>
      <text x="1081" y="574" text-anchor="middle" font-size="14" fill="#397361">Windows x64 · French / English</text>
    </g>
  </svg>`);
  // Existing screenshots are scaled for layout only: no labels, values or UI are redrawn.
  const ui = await sharp(input('details-en.png')).resize({ width: 270 }).png().toBuffer();
  const tray = await sharp(input('taskbar-tray.png')).resize({ width: 728 }).png().toBuffer();
  // This editorial highlight identifies the real numeric badge; it is not app UI.
  const highlight = Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="640">
    <path d="M192 390 V416" fill="none" stroke="#23B48F" stroke-width="3" stroke-linecap="round"/>
    <circle cx="192" cy="444" r="23" fill="none" stroke="#23B48F" stroke-width="3"/>
  </svg>`);
  const output = input('social-preview.png');
  await sharp(artwork)
    .composite([{ input: ui, left: 946, top: 142 }, { input: tray, left: 84, top: 402 }, { input: highlight }])
    .flatten({ background: '#F4FAF7' })
    .png({ compressionLevel: 9, palette: false })
    .toFile(output);
  const metadata = await sharp(output).metadata();
  const bytes = fs.statSync(output).size;
  assert.equal(metadata.format, 'png');
  assert.deepEqual([metadata.width, metadata.height], [1280, 640]);
  assert.ok(bytes < 1_000_000, 'GitHub social preview must remain below 1 MB');
  for (const source of sources) assert.equal(digest(source.file), source.sha256, 'Source screenshot changed');
  console.log(JSON.stringify({ output: 'social-preview.png', width: metadata.width, height: metadata.height, bytes, tagline, supportingMessage, supportingDetail, disclaimer, sources }, null, 2));
}
main().catch(error => { console.error(error); process.exitCode = 1; });
