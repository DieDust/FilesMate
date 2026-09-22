import { getDocument, GlobalWorkerOptions, TextLayer } from './build/pdf.mjs';
const asset = path => new URL(path, import.meta.url).href;
GlobalWorkerOptions.workerSrc = asset('build/pdf.worker.mjs');
const $ = id => document.getElementById(id), viewport = $('viewport'), paper = $('paper');
let pdf, pageNumber = 1, zoom = 1, fit = true, hand = true, epoch = 0, pointer, resizeTimer, frame;
let working = false, disposed = false;
const mounted = new Set();
const pages = [], wanted = new Set(), cache = new Map();
const status = text => $('status').textContent = text;
function clearPointer() { pointer = null; viewport.classList.remove('dragging'); }
viewport.addEventListener('pointerdown', e => {
  const bounds = viewport.getBoundingClientRect();
  if (!hand || e.button !== 0 || e.clientX - bounds.left >= viewport.clientWidth || e.clientY - bounds.top >= viewport.clientHeight) return;
  pointer = { id: e.pointerId, x: e.clientX, y: e.clientY, left: viewport.scrollLeft, top: viewport.scrollTop };
  viewport.setPointerCapture(e.pointerId); viewport.classList.add('dragging'); e.preventDefault();
});
viewport.addEventListener('pointermove', e => { if (pointer?.id === e.pointerId) { viewport.scrollLeft = pointer.left - (e.clientX - pointer.x); viewport.scrollTop = pointer.top - (e.clientY - pointer.y); } });
viewport.addEventListener('pointerup', e => { if (pointer?.id === e.pointerId) { viewport.releasePointerCapture(e.pointerId); clearPointer(); } });
viewport.addEventListener('lostpointercapture', clearPointer); window.addEventListener('blur', clearPointer);
function updateToolbar() {
  if (document.activeElement !== $('page')) $('page').value = pageNumber;
  $('previous').disabled = pageNumber === 1; $('next').disabled = pageNumber === pdf.numPages;
  $('fit').textContent = Math.round(zoom * 100) + '%';
  document.body.dataset.ready = cache.get(pageNumber)?.ready ? 'true' : '';
}
function drop(number) {
  const entry = cache.get(number); if (!entry) return;
  cache.delete(number); entry.cancelled = true; entry.render?.cancel(); entry.text?.cancel();
  pages[number - 1].element?.replaceChildren();
  Promise.resolve(entry.render?.promise).catch(() => {}).then(() => {
    if (entry.canvas) entry.canvas.width = entry.canvas.height = 0;
    entry.page?.cleanup();
  });
}
function candidates() {
  const top = viewport.getBoundingClientRect().top + 16;
  return [...wanted].sort((a, b) => Math.abs(pages[a - 1].element.getBoundingClientRect().top - top) - Math.abs(pages[b - 1].element.getBoundingClientRect().top - top)).slice(0, 3);
}
async function pump() {
  if (working || disposed || !pdf) return;
  working = true;
  try {
    for (;;) {
      const selected = candidates();
      for (const number of cache.keys()) if (!selected.includes(number)) drop(number);
      const number = selected.find(n => !cache.has(n)); if (!number || disposed) break;
      const mine = epoch, slot = pages[number - 1], entry = { cancelled: false, ready: false };
      cache.set(number, entry);
      try {
        entry.page = await pdf.getPage(number);
        if (entry.cancelled || mine !== epoch) { entry.page.cleanup(); continue; }
        const natural = entry.page.getViewport({ scale: 1 });
        if (slot.width !== natural.width || slot.height !== natural.height) {
          const anchor = pages[pageNumber - 1], offset = viewport.scrollTop - anchor.top;
          slot.width = natural.width; slot.height = natural.height; geometry(); viewport.scrollTop = anchor.top + offset;
        }
        const view = entry.page.getViewport({ scale: zoom });
        slot.element.style.width = view.width + 'px'; slot.element.style.height = view.height + 'px';
        const canvas = entry.canvas = document.createElement('canvas'), layer = document.createElement('div'); layer.className = 'textLayer';
        slot.element.style.setProperty('--scale-factor', zoom); slot.element.style.setProperty('--total-scale-factor', zoom);
        slot.element.replaceChildren(canvas, layer);
        const ratio = Math.min(devicePixelRatio || 1, 2, Math.sqrt(4_000_000 / (view.width * view.height)));
        canvas.width = Math.max(1, Math.floor(view.width * ratio)); canvas.height = Math.max(1, Math.floor(view.height * ratio));
        canvas.style.width = view.width + 'px'; canvas.style.height = view.height + 'px';
        entry.render = entry.page.render({ canvasContext: canvas.getContext('2d'), viewport: view, transform: [ratio, 0, 0, ratio, 0, 0] });
        await entry.render.promise; if (entry.cancelled || mine !== epoch) continue;
        entry.text = new TextLayer({ textContentSource: entry.page.streamTextContent(), container: layer, viewport: view });
        await entry.text.render(); if (entry.cancelled || mine !== epoch) continue;
        entry.ready = true; status(''); updateToolbar();
      } catch (error) {
        if (!entry.cancelled && mine === epoch) { slot.element.textContent = '此页暂时无法预览，请打开原文件查看。'; document.body.dataset.error = String(error); }
      }
    }
  } finally { working = false; }
}
function pageAt(top) {
  let lo = 0, hi = pages.length - 1;
  while (lo < hi) { const mid = (lo + hi + 1) >>> 1; if (pages[mid].top <= top) lo = mid; else hi = mid - 1; }
  return lo + 1;
}
function position(slot) {
  if (!slot.element) return;
  Object.assign(slot.element.style, { top: slot.top + 'px', width: slot.width * zoom + 'px', height: slot.height * zoom + 'px', marginLeft: -slot.width * zoom / 2 + 'px' });
}
function geometry() {
  let top = 0, width = 0;
  for (const slot of pages) { slot.top = top; top += slot.height * zoom + 16; width = Math.max(width, slot.width * zoom); }
  paper.style.height = Math.max(0, top - 16) + 'px'; paper.style.width = width + 'px';
  for (const number of mounted) position(pages[number - 1]);
}
function mountVisible() {
  if (!pdf || disposed) return;
  const first = pageAt(Math.max(0, viewport.scrollTop - 120));
  const last = Math.min(first + 8, pageAt(viewport.scrollTop + viewport.clientHeight + 120));
  wanted.clear();
  for (let number = first; number <= last; number++) {
    wanted.add(number);
    const slot = pages[number - 1];
    if (!slot.element) {
      slot.element = document.createElement('div'); slot.element.className = 'pdf-page'; slot.element.dataset.page = number;
      slot.element.setAttribute('aria-label', '第 ' + number + ' 页'); paper.append(slot.element); mounted.add(number); position(slot);
    }
  }
  for (const number of [...mounted]) if (!wanted.has(number)) {
    drop(number); pages[number - 1].element.remove(); pages[number - 1].element = null; mounted.delete(number);
  }
}
function onScroll() {
  cancelAnimationFrame(frame);
  frame = requestAnimationFrame(() => {
    if (!pdf || disposed) return;
    mountVisible(); pageNumber = pageAt(viewport.scrollTop + 24); updateToolbar(); pump();
  });
}
viewport.addEventListener('scroll', onScroll, { passive: true });
function layout() {
  if (!pdf || !pages.length) return;
  const anchor = pages[pageNumber - 1], offset = viewport.scrollTop - anchor.top;
  const leftFraction = viewport.scrollLeft / Math.max(1, anchor.width * zoom);
  epoch++; for (const number of [...cache.keys()]) drop(number);
  if (fit) zoom = Math.min(3, Math.max(.25, (viewport.clientWidth - 32) / pages[0].width));
  geometry(); viewport.scrollTop = anchor.top + offset; viewport.scrollLeft = leftFraction * anchor.width * zoom;
  mountVisible(); updateToolbar(); pump();
}
function go(number) {
  if (!pdf) return;
  pageNumber = Math.max(1, Math.min(pdf.numPages, Math.floor(number) || 1));
  viewport.scrollTop = pages[pageNumber - 1].top;
  mountVisible(); updateToolbar(); pump();
}
function changeZoom(factor) { fit = false; zoom = Math.max(.25, Math.min(5, zoom * factor)); layout(); }
$('previous').onclick = () => go(pageNumber - 1); $('next').onclick = () => go(pageNumber + 1);
$('page').onchange = () => go(Number($('page').value));
$('plus').onclick = () => changeZoom(1.25); $('minus').onclick = () => changeZoom(.8); $('fit').onclick = () => { fit = true; layout(); };
$('hand').onclick = () => { hand = !hand; clearPointer(); viewport.classList.toggle('select', !hand); $('hand').setAttribute('aria-pressed', String(hand)); $('hand').textContent = hand ? '拖动' : '选字'; };
viewport.addEventListener('wheel', e => { if (e.ctrlKey) { e.preventDefault(); changeZoom(e.deltaY < 0 ? 1.1 : 1 / 1.1); } }, { passive: false });
document.addEventListener('keydown', e => {
  if (e.target instanceof HTMLInputElement) return;
  if (e.code === 'Space' || e.key === 'Escape') { e.preventDefault(); window.chrome?.webview?.postMessage('close-preview'); }
  else if (e.key === 'PageDown') { e.preventDefault(); viewport.scrollTop += viewport.clientHeight * .9; }
  else if (e.key === 'PageUp') { e.preventDefault(); viewport.scrollTop -= viewport.clientHeight * .9; }
});
const resizeObserver = new ResizeObserver(() => { if (fit) { clearTimeout(resizeTimer); resizeTimer = setTimeout(layout, 120); } });
resizeObserver.observe(viewport);
try {
  pdf = await getDocument({ url: 'https://filesmate-document.local/document.pdf',
    // Absolute asset URLs are required inside the build/ worker (JBIG2/CJK/fonts).
    cMapUrl: asset('cmaps/'), cMapPacked: true, standardFontDataUrl: asset('standard_fonts/'), wasmUrl: asset('wasm/'),
    isEvalSupported: false, disableAutoFetch: true, disableStream: true, rangeChunkSize: 65536, canvasMaxAreaInBytes: 16_000_000 }).promise;
  const first = await pdf.getPage(1), natural = first.getViewport({ scale: 1 }); first.cleanup();
  // Only lightweight geometry is stored for distant pages. DOM, observers, canvases
  // and text layers are bounded by the viewport, regardless of document length.
  for (let number = 1; number <= pdf.numPages; number++) pages.push({ element: null, top: 0, width: natural.width, height: natural.height });
  paper.replaceChildren(); $('count').textContent = '/ ' + pdf.numPages; $('page').max = pdf.numPages;
  layout();
} catch (error) { document.body.dataset.error = String(error); status('无法预览此 PDF，文件可能已加密或损坏，请打开文件查看。'); }
window.addEventListener('pagehide', () => { disposed = true; resizeObserver.disconnect(); clearTimeout(resizeTimer); cancelAnimationFrame(frame); for (const n of [...cache.keys()]) drop(n); pdf?.destroy(); });
