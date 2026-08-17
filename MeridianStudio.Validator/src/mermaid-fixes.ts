/**
 * Canonical, pure Mermaid repair catalog — the single source of truth shared by the validator
 * sidecar and (via parity) the Angular client. NO Angular / mermaid / DOM imports: every rule is a
 * deterministic `string -> string` transform, so the catalog is trivially unit-testable and can run
 * anywhere. Validation (does it parse?) lives outside this module.
 *
 * ─── How to add a future fix (no LLM, deterministic, reused forever) ───────────────────────────
 *   1. A diagram fails and lands in the unresolved corpus (or a bug report).
 *   2. Write a new `MermaidFix` (a detector + a pure transform) and append it to REPAIR_CATALOG.
 *   3. Add an input->output fixture to the spec. `npm test` guards it.
 *   4. The validate/repair loop now fixes that pattern for everyone — no model call.
 *
 * Design principle — repairs PRESERVE information; they do not merely make the diagram parse.
 * Because the loop re-validates against the real parser after each rule, always attempt the richest
 * form first and let validation decide; only fall back to a lossier transform if the parser rejects it.
 */

export type DiagramType = 'flow' | 'sequence' | 'class' | 'state' | 'er' | 'other';

export interface MermaidFix {
  /** Stable id, e.g. 'bare-multiword-nodes'. */
  readonly name: string;
  readonly description: string;
  /** 'flow' rules only run on flowchart/graph diagrams; 'any' runs on all types. */
  readonly appliesTo: 'flow' | 'any';
  apply(src: string): string;
}

// ── Diagram type detection ────────────────────────────────────────────────────

export function diagramType(src: string): DiagramType {
  const header = src.split('\n').map(l => l.trim()).find(l => l && !l.startsWith('%%')) ?? '';
  if (/^(graph|flowchart)\b/.test(header)) return 'flow';
  if (/^sequenceDiagram\b/.test(header)) return 'sequence';
  if (/^classDiagram\b/.test(header)) return 'class';
  if (/^stateDiagram\b/.test(header)) return 'state';
  if (/^erDiagram\b/.test(header)) return 'er';
  return 'other';
}

const isFlow = (src: string) => diagramType(src) === 'flow';

// ── Shared constants (ported from the client sanitizer) ───────────────────────

/** Characters Mermaid treats as shape delimiters — cannot appear unescaped inside a node label. */
const BREAKER = /[()\[\]{}]/;

const PH_OPEN = 'MERMAIDPH';
const PH_CLOSE = 'ENDPH';
const PH_RESTORE = /MERMAIDPH(\d+)ENDPH/g;

/** Arrow operators that separate nodes on a flowchart edge line. */
const FLOW_ARROW = /(<-->|<==>|-->|--[ox]|---|-\.->|-\.-|==+>|===+)/;

/** Structural lines carry diagram syntax (not labels) and must never be rewritten. */
const STRUCTURAL =
  /^\s*(graph|flowchart|sequenceDiagram|classDiagram|stateDiagram|erDiagram|gantt|pie|journey|subgraph|end|%%|classDef|class\s|style\s|linkStyle|click|direction)\b/;

const COMPOUND_SHAPES: ReadonlyArray<readonly [string, string, RegExp]> = [
  ['[[', ']]', /\[\[([\s\S]*?)\]\]/g],
  ['[(', ')]', /\[\(([\s\S]*?)\)\]/g],
  ['([', '])', /\(\[([\s\S]*?)\]\)/g],
  ['((', '))', /\(\(([\s\S]*?)\)\)/g],
  ['{{', '}}', /\{\{([\s\S]*?)\}\}/g],
];

const SIMPLE_SHAPES: ReadonlyArray<readonly [string, string, RegExp]> = [
  ['[', ']', /\[([^\[\]]*?)\]/g],
  ['(', ')', /\(([^()]*?)\)/g],
  ['{', '}', /\{([^{}]*?)\}/g],
];

function quoteInner(inner: string): string {
  if (inner.length >= 2 && inner.startsWith('"') && inner.endsWith('"')) return inner;
  if (!BREAKER.test(inner)) return inner;
  return '"' + inner.replace(/"/g, '#quot;') + '"';
}

// ── ALWAYS_ON: semantics-preserving, applied before the first validate ────────

/** De-quote edge labels: the model quotes them (`-->|"X"|`) and the doc pipeline single-quotes them;
 *  Mermaid then renders the literal quotes. Strip the wrapping quotes only. */
const dequoteEdgeLabels: MermaidFix = {
  name: 'dequote-edge-labels',
  description: 'Remove wrapping quotes the model/doc-pipeline leaves inside |edge labels| — except when the label contains a breaker char, where quotes are REQUIRED (normalised to double).',
  appliesTo: 'any',
  apply: (src) =>
    src
      .replace(/\|(\s*)(['"])([^|]*?)\2(\s*)\|/g, (_m, s1: string, _q: string, inner: string, s2: string) =>
        BREAKER.test(inner) ? `|${s1}"${inner.replace(/"/g, '#quot;')}"${s2}|` : `|${s1}${inner}${s2}|`)
      .replace(/(--+|-\.-*|==+)(\s*)(['"])([^'"]*?)\3(\s*)(--+>|-\.->|==+>|--+|===+)/g,
        (_m, a: string, s1: string, _q: string, inner: string, s2: string, b: string) =>
          BREAKER.test(inner) ? `${a}${s1}"${inner.replace(/"/g, '#quot;')}"${s2}${b}` : `${a}${s1}${inner}${s2}${b}`),
};

export const ALWAYS_ON: readonly MermaidFix[] = [dequoteEdgeLabels];

// ── REPAIR_CATALOG rule 1: subgraph titles with spaces → id["Title"] ──────────

const subgraphTitleIds: MermaidFix = {
  name: 'subgraph-title-ids',
  description: 'Give a spaced/breaker subgraph title a referenceable id AND quote the label of an explicit-id subgraph (`subgraph X[Cloud (e.g. Azure)]`) — a `(` in a subgraph label aborts the parse.',
  appliesTo: 'flow',
  apply(src) {
    const lines = src.split('\n');
    const sgIds = new Map<string, string>();
    lines.forEach((line, idx) => {
      const m = line.match(/^(\s*subgraph\s+)(\S.*?)\s*$/);
      if (!m) return;
      const title = m[2];
      if (title.startsWith('"') || /^[\w-]+\s*\[/.test(title)) return; // already id[..]/quoted
      if (!/\s/.test(title) && !BREAKER.test(title)) return;           // clean single token
      sgIds.set(title, `sg${idx}`);
    });

    return lines
      .map((line) => {
        const sg = line.match(/^(\s*subgraph\s+)(\S.*?)\s*$/);
        if (sg) {
          const id = sgIds.get(sg[2]);
          if (id) return `${sg[1]}${id}["${sg[2].replace(/"/g, '#quot;')}"]`;
          // Explicit-id form `subgraph id[Label]` — the shape passes never touch subgraph lines, so
          // quote the bracketed label here when it holds a breaker char.
          const idLabel = sg[2].match(/^([A-Za-z0-9_-]+)\s*\[([\s\S]*)\]$/);
          if (idLabel) return `${sg[1]}${idLabel[1]}[${quoteInner(idLabel[2])}]`;
          return line;
        }
        let out = line;
        for (const [title, id] of sgIds) if (out.includes(title)) out = out.split(title).join(id);
        return out;
      })
      .join('\n');
  },
};

// ── REPAIR_CATALOG rule 2: bare multi-word node ids → id["Label"] ─────────────

function isBareComplexNode(ref: string): boolean {
  // A space or slash makes a bare id invalid; slashes are preserved inside the resulting label.
  return /^[A-Za-z0-9][A-Za-z0-9 _/-]*$/.test(ref) && /[ /]/.test(ref);
}

function bareNodeId(label: string, map: Map<string, string>, used: Set<string>): string {
  const existing = map.get(label);
  if (existing) return existing;
  let base = label.replace(/[^A-Za-z0-9]/g, '');
  if (!base || !/^[A-Za-z]/.test(base)) base = 'n' + base;
  let id = base;
  let i = 2;
  while (used.has(id)) id = base + i++;
  used.add(id);
  map.set(label, id);
  return id;
}

function rewriteNodeSlot(node: string, map: Map<string, string>, used: Set<string>): string {
  const t = node.trim();
  if (!isBareComplexNode(t)) return node;
  const id = bareNodeId(t, map, used);
  return `${id}["${t.replace(/"/g, '#quot;')}"]`;
}

const bareMultiwordNodes: MermaidFix = {
  name: 'bare-multiword-nodes',
  description: 'Rewrite multi-word phrases used as bare node ids (edges + subgraph members) to id["Label"]. Preserves slashes in labels.',
  appliesTo: 'flow',
  apply(src) {
    const map = new Map<string, string>();
    const used = new Set<string>();
    return src
      .split('\n')
      .map((line) => {
        if (STRUCTURAL.test(line)) return line;
        const indent = line.match(/^\s*/)?.[0] ?? '';
        const body = line.trim();
        if (!body) return line;

        const parts = body.split(FLOW_ARROW);
        if (parts.length === 1) return indent + rewriteNodeSlot(body, map, used);

        const rebuilt = parts.map((part, i) => {
          if (i % 2 === 1) return part; // arrow operator
          // A segment may lead with an edge-label pipe (`|label| target`) after the arrow.
          const m = part.match(/^\s*(\|[^|]*\|)?\s*([\s\S]*?)\s*$/);
          const pipe = m?.[1] ?? '';
          const node = m?.[2] ?? '';
          // Slashes in pipe labels are valid Mermaid — preserved (only escape the terminator).
          const normPipe = pipe ? `${pipe.slice(0, -1).replace(/\|/g, (c, idx) => (idx === 0 ? c : '#124;'))}| ` : '';
          return normPipe + rewriteNodeSlot(node, map, used);
        });
        return indent + rebuilt.join(' ');
      })
      .join('\n');
  },
};

// ── REPAIR_CATALOG rule 3: merge trailing ` : Y` edge annotations (don't drop) ─

function mergeTrailingEdgeLabel(line: string): string {
  const m = line.match(
    /^(\s*)(\S.*?(?:-->|--[ox]|---|-\.->|-\.-|==+>|===+)\s*(?:\|[^|]*\|\s*)?[A-Za-z0-9_-]+(?:\[[^\]]*\]|\([^)]*\)|\{[^}]*\})?)\s*:\s*(\S.*?)\s*$/,
  );
  if (!m) return line;
  const [, indent, edge, trailingRaw] = m;
  const y = trailingRaw.replace(/\|/g, '#124;').trim(); // escape pipe so it can't terminate the label

  // Pipe-labelled edge → merge into the existing pipe: |X| -> |X — Y|
  if (/\|[^|]*\|/.test(edge)) {
    return indent + edge.replace(/\|([^|]*)\|/, (_full, x: string) => `|${x.trim()} — ${y}|`);
  }

  // Inline-labelled edge (`A -- X --> B`, `== X ==>`, `-. X .->`) → normalize to pipe form and merge.
  const inline = edge.match(
    /^(.*?)(--|==|-\.)\s+(\S.*?)\s*(?:--+>|--[ox]|==+>|===+|-?\.->|-\.-|---)\s*(.*)$/,
  );
  if (inline) {
    const [, pre, opener, x, rest] = inline;
    const arrow = opener === '==' ? '==>' : opener === '-.' ? '-.->' : '-->';
    return `${indent}${pre.trimEnd()} ${arrow}|${x.trim()} — ${y}| ${rest.trim()}`.replace(/\s+$/, '');
  }

  // Bare edge (no existing label) → move Y onto the arrow as its label (nothing was lost before).
  return indent + edge.replace(
    /(-->|--[ox]|---|-\.->|-\.-|==+>|===+)(\s*)([A-Za-z0-9_-]+(?:\[[^\]]*\]|\([^)]*\)|\{[^}]*\})?)\s*$/,
    (_full, arrow: string, _sp: string, target: string) => `${arrow}|${y}| ${target}`,
  );
}

const trailingEdgeLabel: MermaidFix = {
  name: 'trailing-edge-label',
  description: 'Fix `A -- X --> B : Y` edges. Merge X and Y (|X — Y|) so the mechanism/protocol Y is never dropped.',
  appliesTo: 'flow',
  apply: (src) => src.split('\n').map(mergeTrailingEdgeLabel).join('\n'),
};

// ── REPAIR_CATALOG rule 4: quote shape-inner text containing breakers ─────────

const quoteShapeInner: MermaidFix = {
  name: 'quote-shape-inner',
  description: 'Quote node-label text that contains shape-delimiter chars (e.g. J[Compute (GPU/TPU)]).',
  appliesTo: 'flow',
  apply(src) {
    return src
      .split('\n')
      .map((line) => {
        if (STRUCTURAL.test(line)) return line;
        const stash: string[] = [];
        let work = line;
        for (const [open, close, re] of COMPOUND_SHAPES) {
          work = work.replace(re, (_full, inner: string) => {
            const ph = PH_OPEN + stash.length + PH_CLOSE;
            stash.push(open + quoteInner(inner) + close);
            return ph;
          });
        }
        for (const [open, close, re] of SIMPLE_SHAPES) {
          work = work.replace(re, (_full, inner: string) => open + quoteInner(inner) + close);
        }
        return work.replace(PH_RESTORE, (_full, i: string) => stash[Number(i)]);
      })
      .join('\n');
  },
};

// ── REPAIR_CATALOG rule 5: quote edge-label text containing breakers ──────────

const quoteEdgeLabelInner: MermaidFix = {
  name: 'quote-edge-labels',
  description: 'Quote pipe-edge-label text with shape-delimiter chars: `|PR Webhook (JSON)|` → `|"PR Webhook (JSON)"|` (unquoted parens/brackets abort the flow parse). Runs before quote-shape-inner.',
  appliesTo: 'flow',
  apply: (src) =>
    src
      .split('\n')
      .map((line) => {
        if (STRUCTURAL.test(line)) return line;
        return line.replace(/\|([^|]*)\|/g, (_full, inner: string) => {
          const t = inner.trim();
          if (!BREAKER.test(t)) return `|${inner}|`;
          if (t.length >= 2 && t.startsWith('"') && t.endsWith('"')) return `|${t}|`;
          return `|"${t.replace(/"/g, '#quot;')}"|`;
        });
      })
      .join('\n'),
};

export const REPAIR_CATALOG: readonly MermaidFix[] = [
  subgraphTitleIds,
  bareMultiwordNodes,
  trailingEdgeLabel,
  quoteEdgeLabelInner,
  quoteShapeInner,
];

// ── Convenience: apply a set of fixes once (used by ALWAYS_ON pre-pass) ────────

export function applyFixes(src: string, fixes: readonly MermaidFix[]): string {
  const type = diagramType(src);
  let out = src;
  for (const fix of fixes) {
    if (fix.appliesTo === 'flow' && type !== 'flow') continue;
    out = fix.apply(out);
  }
  return out;
}
