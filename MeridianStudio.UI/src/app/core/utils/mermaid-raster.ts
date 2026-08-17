/**
 * Renders Mermaid diagram source to a PNG data URL for embedding in exported documents.
 *
 * Exports always use a LIGHT diagram (documents are white-page) and, critically,
 * `htmlLabels:false` so Mermaid emits SVG <text> instead of <foreignObject> HTML — the latter
 * does not rasterize to <canvas> in most browsers, which is the usual reason "SVG → PNG" fails.
 *
 * Call sequence: beginExportRender() once, renderMermaidPng() per diagram, then
 * restoreAppMermaidTheme(currentTheme) to put the on-screen config back.
 */
import mermaid from 'mermaid';
import {
  tidyMermaid,
  sanitizeMermaidLabels,
  applyMermaidTheme,
  invalidateMermaidTheme,
  removeOrphanNode,
} from '../directives/mermaid.directive';
import { Theme } from '../services/theme.service';

export interface RasterImage {
  /** PNG data URL. */
  dataUrl: string;
  /** Natural width in px (aspect-ratio source for layout). */
  width: number;
  /** Natural height in px. */
  height: number;
}

let _seq = 0;

/** Switch Mermaid into the export configuration (light, rasterizable labels). */
export function beginExportRender(): void {
  invalidateMermaidTheme(); // force the app theme to be re-applied on restore
  mermaid.initialize({
    startOnLoad: false,
    securityLevel: 'loose',
    fontFamily: 'inherit',
    theme: 'default',
    htmlLabels: false,
    // useMaxWidth:false on every diagram type we emit → Mermaid writes an absolute px width
    // (not width="100%"), so the rasterized PNG carries the diagram's true size and never
    // collapses to a sliver in the exported PDF/DOCX.
    flowchart: { htmlLabels: false, useMaxWidth: false },
    sequence: { useMaxWidth: false },
    class: { useMaxWidth: false },
    state: { useMaxWidth: false },
    er: { useMaxWidth: false },
    themeVariables: {
      clusterBkg: '#f1f5f9',
      clusterBorder: '#cbd5e1',
      lineColor: '#475569',
      titleColor: '#0f172a',
      edgeLabelBackground: '#ffffff',
      primaryColor: '#e2e8f0',
      primaryBorderColor: '#94a3b8',
      primaryTextColor: '#0f172a',
      nodeBorder: '#94a3b8',
      textColor: '#0f172a',
    },
  });
}

/** Restore the on-screen Mermaid configuration for the current app theme. */
export function restoreAppMermaidTheme(appTheme: Theme): void {
  invalidateMermaidTheme();
  applyMermaidTheme(appTheme);
}

/** Render one diagram to a PNG. Returns null if it can't be rendered/rasterized. On a parse
 *  failure Mermaid leaks its "Syntax error" bomb into the DOM (appended to <body>); we remove
 *  it after every failed attempt so a broken diagram never leaves a stray graphic on screen. */
export async function renderMermaidPng(src: string): Promise<RasterImage | null> {
  const clean = tidyMermaid(src, /* diagramDark */ false);
  let svg: string;
  const id1 = `export-mmd-${++_seq}`;
  try {
    ({ svg } = await mermaid.render(id1, clean));
  } catch {
    removeOrphanNode(id1);
    const id2 = `export-mmd-${++_seq}`;
    try {
      ({ svg } = await mermaid.render(id2, sanitizeMermaidLabels(clean)));
    } catch {
      removeOrphanNode(id2);
      return null;
    }
  }
  try {
    return await svgToPng(svg);
  } catch {
    return null;
  }
}

/** Pull intrinsic pixel dimensions from an SVG string (width/height attrs, else viewBox). */
function svgSize(svg: string): { w: number; h: number } {
  let w = 800;
  let h = 600;
  const vb = /viewBox="([\d.\-\s]+)"/.exec(svg);
  if (vb) {
    const p = vb[1].trim().split(/\s+/).map(Number);
    if (p.length === 4 && p[2] > 0 && p[3] > 0) {
      w = p[2];
      h = p[3];
    }
  }
  // Only trust an explicit width/height when it is an ABSOLUTE px value. Mermaid emits
  // width="100%" (+ a max-width style) for any diagram type whose useMaxWidth isn't off, and
  // the old regex read the "100" out of "100%" as 100px — collapsing the embedded image to a
  // sliver. Requiring the closing quote (or a `px` suffix) right after the number means a
  // trailing "%" fails the match, so we fall back to the viewBox's natural pixel size.
  const wm = /<svg[^>]*\bwidth="([\d.]+)(?:px)?"/.exec(svg);
  const hm = /<svg[^>]*\bheight="([\d.]+)(?:px)?"/.exec(svg);
  if (wm) w = parseFloat(wm[1]);
  if (hm) h = parseFloat(hm[1]);
  return { w, h };
}

function loadImage(url: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => resolve(img);
    img.onerror = () => reject(new Error('svg image load failed'));
    img.src = url;
  });
}

async function svgToPng(svg: string): Promise<RasterImage> {
  const { w, h } = svgSize(svg);
  const scale = 2; // render at 2x for crisp text in the document
  const prepared = svg.includes('xmlns')
    ? svg
    : svg.replace('<svg', '<svg xmlns="http://www.w3.org/2000/svg"');

  const url = URL.createObjectURL(new Blob([prepared], { type: 'image/svg+xml;charset=utf-8' }));
  try {
    const img = await loadImage(url);
    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(w * scale));
    canvas.height = Math.max(1, Math.round(h * scale));
    const ctx = canvas.getContext('2d');
    if (!ctx) throw new Error('no 2d context');
    ctx.fillStyle = '#ffffff'; // opaque white so the diagram reads on a white page
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
    return { dataUrl: canvas.toDataURL('image/png'), width: w, height: h };
  } finally {
    URL.revokeObjectURL(url);
  }
}
