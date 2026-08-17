/**
 * Lightweight block-level Markdown parser used for document EXPORT (DOCX / PDF).
 *
 * The on-screen renderer (MarkdownPipe) emits HTML; the exporters instead need a structured
 * block model so they can map each construct to native Word/PDF primitives (headings,
 * paragraphs, tables, lists, images). This parser covers the same subset the pipe supports:
 * ATX headings, fenced code (incl. ```mermaid), tables, ordered/unordered lists, blockquotes,
 * horizontal rules, and inline bold/italic/code/strikethrough/links.
 *
 * It is intentionally small and dependency-free — not a spec-complete CommonMark parser.
 */

export interface MdInline {
  text: string;
  bold?: boolean;
  italic?: boolean;
  code?: boolean;
  strike?: boolean;
  href?: string;
}

export type MdBlock =
  | { kind: 'heading'; level: number; runs: MdInline[] }
  | { kind: 'paragraph'; runs: MdInline[] }
  | { kind: 'list'; ordered: boolean; items: MdInline[][] }
  | { kind: 'table'; headers: MdInline[][]; rows: MdInline[][][] }
  | { kind: 'code'; lang: string; text: string }
  | { kind: 'mermaid'; src: string }
  | { kind: 'blockquote'; runs: MdInline[] }
  | { kind: 'hr' };

const HR = /^\s*([-*_])(\s*\1){2,}\s*$/;
const HEADING = /^(#{1,6})\s+(.*)$/;
const FENCE = /^\s*```\s*([\w-]*)\s*$/;
const UL_ITEM = /^\s*[-*+]\s+(.*)$/;
const OL_ITEM = /^\s*\d+[.)]\s+(.*)$/;
const BLOCKQUOTE = /^\s*>\s?(.*)$/;
const TABLE_SEP = /^\s*\|?\s*:?-{1,}:?\s*(\|\s*:?-{1,}:?\s*)+\|?\s*$/;

/** Split a Markdown table row into trimmed cell strings (leading/trailing pipes ignored). */
function splitRow(line: string): string[] {
  let s = line.trim();
  if (s.startsWith('|')) s = s.slice(1);
  if (s.endsWith('|')) s = s.slice(0, -1);
  // Split on unescaped pipes.
  return s.split(/(?<!\\)\|/).map((c) => c.replace(/\\\|/g, '|').trim());
}

/** Parse inline Markdown (bold/italic/code/strike/link) into styled runs. */
export function parseInline(input: string): MdInline[] {
  const runs: MdInline[] = [];
  let i = 0;
  let plain = '';
  const flush = () => {
    if (plain) {
      runs.push({ text: plain });
      plain = '';
    }
  };

  // Ordered so multi-char markers (**, __, ~~) win over single-char ones.
  const rules: ReadonlyArray<readonly [RegExp, (m: RegExpExecArray) => MdInline]> = [
    [/^`([^`]+)`/, (m) => ({ text: m[1], code: true })],
    [/^\*\*([^*]+)\*\*/, (m) => ({ text: m[1], bold: true })],
    [/^__([^_]+)__/, (m) => ({ text: m[1], bold: true })],
    [/^~~([^~]+)~~/, (m) => ({ text: m[1], strike: true })],
    [/^\*([^*]+)\*/, (m) => ({ text: m[1], italic: true })],
    [/^_([^_]+)_/, (m) => ({ text: m[1], italic: true })],
    [/^\[([^\]]+)\]\(([^)]+)\)/, (m) => ({ text: m[1], href: m[2] })],
  ];

  while (i < input.length) {
    const rest = input.slice(i);
    let matched = false;
    // Only attempt marker rules at a marker char to keep the scan cheap.
    if (/[`*_~[]/.test(rest[0])) {
      for (const [re, make] of rules) {
        const m = re.exec(rest);
        if (m) {
          flush();
          runs.push(make(m));
          i += m[0].length;
          matched = true;
          break;
        }
      }
    }
    if (!matched) {
      plain += input[i];
      i += 1;
    }
  }
  flush();
  return runs.length ? runs : [{ text: '' }];
}

/** Parse a full Markdown document into a flat list of blocks. */
export function parseMarkdownBlocks(md: string): MdBlock[] {
  // Mirror MarkdownPipe: normalise literal `\n` escape sequences to real newlines BEFORE
  // splitting. Some LLMs double-escape long documents (e.g. Detailed Design / Technical Spec),
  // so their `content` arrives with literal backslash-n instead of line breaks. Without this the
  // whole document collapses into one paragraph and ```mermaid fences are never recognised —
  // the on-screen render (which does normalise) looks fine while the export is unreadable.
  // Applied globally (incl. inside code/mermaid, whose lines are separated by the same `\n`).
  const lines = md
    .replace(/\\r\\n/g, '\n') // literal "\r\n"
    .replace(/\\n/g, '\n')    // literal backslash-n → newline
    .replace(/\r\n?/g, '\n')  // real CRLF / CR      → newline
    .split('\n');
  const blocks: MdBlock[] = [];
  let i = 0;

  while (i < lines.length) {
    const line = lines[i];

    if (!line.trim()) { i++; continue; }

    // Fenced code / mermaid
    const fence = FENCE.exec(line);
    if (fence) {
      const lang = (fence[1] || '').toLowerCase();
      const body: string[] = [];
      i++;
      while (i < lines.length && !/^\s*```\s*$/.test(lines[i])) { body.push(lines[i]); i++; }
      i++; // closing fence
      const text = body.join('\n');
      blocks.push(lang === 'mermaid' ? { kind: 'mermaid', src: text } : { kind: 'code', lang, text });
      continue;
    }

    // Horizontal rule
    if (HR.test(line)) { blocks.push({ kind: 'hr' }); i++; continue; }

    // Heading
    const h = HEADING.exec(line);
    if (h) {
      blocks.push({ kind: 'heading', level: h[1].length, runs: parseInline(h[2].trim()) });
      i++;
      continue;
    }

    // Table: a header row followed by a separator row.
    if (line.includes('|') && i + 1 < lines.length && TABLE_SEP.test(lines[i + 1])) {
      const headers = splitRow(line).map(parseInline);
      i += 2; // header + separator
      const rows: MdInline[][][] = [];
      while (i < lines.length && lines[i].includes('|') && lines[i].trim()) {
        rows.push(splitRow(lines[i]).map(parseInline));
        i++;
      }
      blocks.push({ kind: 'table', headers, rows });
      continue;
    }

    // Lists (consecutive item lines of the same ordering)
    if (UL_ITEM.test(line) || OL_ITEM.test(line)) {
      const ordered = OL_ITEM.test(line) && !UL_ITEM.test(line);
      const items: MdInline[][] = [];
      while (i < lines.length && (UL_ITEM.test(lines[i]) || OL_ITEM.test(lines[i]))) {
        const m = ordered ? OL_ITEM.exec(lines[i]) : UL_ITEM.exec(lines[i]);
        items.push(parseInline((m?.[1] ?? '').trim()));
        i++;
      }
      blocks.push({ kind: 'list', ordered, items });
      continue;
    }

    // Blockquote (collapse consecutive lines)
    if (BLOCKQUOTE.test(line)) {
      const parts: string[] = [];
      while (i < lines.length && BLOCKQUOTE.test(lines[i])) {
        parts.push(BLOCKQUOTE.exec(lines[i])![1]);
        i++;
      }
      blocks.push({ kind: 'blockquote', runs: parseInline(parts.join(' ').trim()) });
      continue;
    }

    // Paragraph (gather until a blank line or a block-starting line)
    const para: string[] = [];
    while (
      i < lines.length &&
      lines[i].trim() &&
      !FENCE.test(lines[i]) &&
      !HEADING.test(lines[i]) &&
      !HR.test(lines[i]) &&
      !UL_ITEM.test(lines[i]) &&
      !OL_ITEM.test(lines[i]) &&
      !BLOCKQUOTE.test(lines[i])
    ) {
      para.push(lines[i].trim());
      i++;
    }
    if (para.length) blocks.push({ kind: 'paragraph', runs: parseInline(para.join(' ')) });
  }

  return blocks;
}

/** Flatten styled runs to their plain text (used for link-suffixing and code blocks). */
export function runsToPlain(runs: MdInline[]): string {
  return runs.map((r) => (r.href ? `${r.text} (${r.href})` : r.text)).join('');
}
