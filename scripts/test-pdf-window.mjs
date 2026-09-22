import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

// Exercise the shipped viewer's scrolling/layout logic with a synthetic PDF provider.
// Real PDF parsing and WebView rendering are covered separately by the app smoke test.
const elements = new Map(), listeners = new Map();
class Element {
  constructor() { this.style = { setProperty() {} }; this.dataset = {}; this.children = []; this.classList = { add() {}, remove() {}, toggle() {} }; this.scrollTop = 0; this.scrollLeft = 0; this.clientWidth = 1200; this.clientHeight = 900; }
  append(element) { this.children.push(element); element.parent = this; }
  replaceChildren(...children) { this.children = []; for (const child of children) this.append(child); }
  remove() { this.parent.children = this.parent.children.filter(child => child !== this); }
  setAttribute() {}
  addEventListener() {}
  getBoundingClientRect() { const top = (parseFloat(this.style.top) || 0) - (elements.get('viewport')?.scrollTop || 0); return { top, bottom: top + (parseFloat(this.style.height) || 900) }; }
  getContext() { return {}; }
}
const document = { body: new Element(), activeElement: null, addEventListener() {}, createElement: () => new Element(),
  getElementById(id) { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); } };
const pdf = { numPages: 10000, async getPage(number) { return { cleanup() {}, getViewport: ({ scale }) => ({ width: 600 * scale, height: (number === 10000 ? 950 : 800) * scale }),
  render() { return { promise: Promise.resolve(), cancel() {} }; }, streamTextContent() {} }; }, destroy() {} };
const source = (await readFile(new URL('../src/FilesMate.App/Assets/PdfPreview/preview.mjs', import.meta.url), 'utf8'))
  .replace(/^import .*\r?\n/, '').replaceAll('import.meta.url', JSON.stringify('https://filesmate-pdf.local/preview.mjs'));
const AsyncFunction = Object.getPrototypeOf(async function() {}).constructor;
const run = new AsyncFunction('document', 'window', 'getDocument', 'GlobalWorkerOptions', 'TextLayer', 'ResizeObserver',
  'devicePixelRatio', 'requestAnimationFrame', 'cancelAnimationFrame',
  source + '\nreturn { go, layout, changeZoom, mounted, cache, pages };');
const viewer = await run(document, { addEventListener: (name, callback) => listeners.set(name, callback) }, () => ({ promise: Promise.resolve(pdf) }), {},
  class { render() { return Promise.resolve(); } cancel() {} }, class { observe() {} disconnect() {} }, 1, callback => { callback(); return 1; }, () => {});
const flush = () => new Promise(resolve => setTimeout(resolve, 0));
await flush();
assert.ok(elements.get('paper').children.length <= 9);
assert.ok(viewer.cache.size <= 3);
for (const page of [5000, 10000, 1, 8765]) {
  viewer.go(page); await flush();
  assert.equal(Number(elements.get('page').value), page);
  assert.ok(viewer.cache.get(page)?.ready);
  assert.ok(viewer.cache.size <= 3);
  assert.ok(elements.get('paper').children.length <= 9);
  viewer.changeZoom(1.1); await flush();
  assert.ok(viewer.cache.get(page)?.ready);
}
listeners.get('pagehide')();
assert.equal(viewer.cache.size, 0);
console.log('PDF window: 10,000 pages; <=9 page elements; <=3 render entries; jump/zoom/cleanup passed.');
