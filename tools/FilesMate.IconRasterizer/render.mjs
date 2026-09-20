import { readFileSync, readdirSync, mkdirSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { Resvg } from '@resvg/resvg-js';

// Checked-in raster exports let WPF share the exact main-app SVG artwork
// without shipping an SVG renderer or requiring Node on users' machines.
const source = new URL('../../src/FilesMate.App/Assets/FileIcons/', import.meta.url);
const output = new URL('../../src/FilesMate.SearchHost/Assets/FileIcons/', import.meta.url);
mkdirSync(output, { recursive: true });
for (const name of readdirSync(source).filter(name => name.endsWith('.svg'))) {
  const png = new Resvg(readFileSync(new URL(name, source)), {
    fitTo: { mode: 'width', value: 128 }, font: { loadSystemFonts: false }
  }).render().asPng();
  const destination = new URL(name.replace('.svg', '.png'), output);
  if (process.argv.includes('--check')) {
    if (!readFileSync(destination).equals(png)) throw new Error(`Stale export: ${fileURLToPath(destination)}`);
  } else writeFileSync(destination, png);
}
console.log('FilesMate icon exports are synchronized.');
