/**
 * Browserless Mermaid validation via `mermaid` + `jsdom` (no Chromium). One warm jsdom + mermaid
 * instance is initialised lazily and reused. `validateDiagram` returns ok/error/errorSignature;
 * `validateDocument` finds fenced ```mermaid blocks in Markdown and validates each.
 */
import { JSDOM } from 'jsdom';
import MarkdownIt from 'markdown-it';

type MermaidApi = { initialize: (c: unknown) => void; parse: (src: string, opts?: { suppressErrors?: boolean }) => Promise<unknown> };

let mermaidPromise: Promise<MermaidApi> | null = null;

async function getMermaid(): Promise<MermaidApi> {
  if (mermaidPromise) return mermaidPromise;
  mermaidPromise = (async () => {
    const dom = new JSDOM('<!DOCTYPE html><body></body>', { pretendToBeVisual: true, url: 'http://localhost' });
    (globalThis as any).window = dom.window;
    (globalThis as any).document = dom.window.document;
    try {
      Object.defineProperty(globalThis, 'navigator', { value: dom.window.navigator, configurable: true });
    } catch {
      /* some runtimes already expose navigator — ignore */
    }
    const mermaid = (await import('mermaid')).default as unknown as MermaidApi;
    mermaid.initialize({ startOnLoad: false, securityLevel: 'loose' });
    return mermaid;
  })();
  return mermaidPromise;
}

export interface DiagramValidation {
  ok: boolean;
  error?: string;
  errorSignature?: string;
}

/** Normalises a parser error into a stable cache key (drops line/column numbers + positions). */
export function errorSignature(message: string): string {
  const firstLine = message.split('\n')[0];
  const expecting = firstLine.match(/Expecting\s+(.+?)(?:,\s*got|$)/i)?.[1] ?? '';
  const got = firstLine.match(/got\s+'([^']+)'/i)?.[1] ?? '';
  const norm = (s: string) => s.replace(/\d+/g, '#').replace(/\s+/g, ' ').trim();
  return got || expecting ? `exp:${norm(expecting)}|got:${norm(got)}` : norm(firstLine);
}

export async function validateDiagram(source: string): Promise<DiagramValidation> {
  const mermaid = await getMermaid();
  try {
    // Without suppressErrors, parse throws on invalid — we want the message for the signature.
    await mermaid.parse(source);
    return { ok: true };
  } catch (e: unknown) {
    const msg = (e as { message?: string })?.message ?? String(e);
    return { ok: false, error: msg.split('\n').slice(0, 3).join(' ').slice(0, 300), errorSignature: errorSignature(msg) };
  }
}

const md = new MarkdownIt();

export interface DocumentDiagram { index: number; ok: boolean; error?: string; }
export interface DocumentValidation {
  ok: boolean;
  diagrams: DocumentDiagram[];
  issues: string[];
}

/** Extracts fenced ```mermaid blocks from Markdown (in document order). */
export function extractMermaidBlocks(markdown: string): string[] {
  const blocks: string[] = [];
  for (const tok of md.parse(markdown, {})) {
    if (tok.type === 'fence' && tok.info.trim().toLowerCase().startsWith('mermaid')) {
      blocks.push(tok.content.replace(/\n$/, ''));
    }
  }
  return blocks;
}

export async function validateDocument(markdown: string): Promise<DocumentValidation> {
  const issues: string[] = [];
  // markdown-it never throws on lenient input, but a render exercises the token stream.
  try { md.render(markdown); } catch (e) { issues.push(`markdown render failed: ${(e as Error).message}`); }

  const blocks = extractMermaidBlocks(markdown);
  const diagrams: DocumentDiagram[] = [];
  for (let i = 0; i < blocks.length; i++) {
    const r = await validateDiagram(blocks[i]);
    diagrams.push({ index: i, ok: r.ok, error: r.error });
    if (!r.ok) issues.push(`diagram #${i} does not parse: ${r.error}`);
  }

  // Cheap structural warnings (advisory, don't fail the gate).
  const required = (markdown.match(/\[REQUIRED:/g) ?? []).length;
  if (required > 0) issues.push(`${required} unresolved [REQUIRED:] placeholder(s)`);

  return { ok: diagrams.every(d => d.ok), diagrams, issues };
}
