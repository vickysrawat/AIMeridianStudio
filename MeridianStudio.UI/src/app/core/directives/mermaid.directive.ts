import { AfterViewInit, Directive, ElementRef, OnDestroy, effect, inject } from '@angular/core';
import mermaid from 'mermaid';
import { ThemeService, Theme } from '../services/theme.service';

// The diagram deliberately uses the OPPOSITE of the app theme for contrast: a LIGHT
// diagram while the app is dark, and a DARK diagram while the app is light. `null`
// means "not configured yet"; the export rasterizer sets it dirty so the app config
// is re-applied afterwards.
let _diagramTheme: 'dark' | 'light' | null = null;

/** Dark-diagram palette — used when the app is in LIGHT mode. */
const DARK_VARS = {
  clusterBkg: '#0f172a',
  clusterBorder: '#475569',
  lineColor: '#94a3b8',
  titleColor: '#e2e8f0',
  edgeLabelBackground: '#0f172a',
  primaryColor: '#1f2937',
  primaryBorderColor: '#475569',
  primaryTextColor: '#e5e7eb',
  nodeBorder: '#475569',
  textColor: '#e2e8f0',
};

/** Light-diagram palette — used when the app is in DARK mode (and for exports). */
const LIGHT_VARS = {
  clusterBkg: '#f1f5f9',
  clusterBorder: '#cbd5e1',
  lineColor: '#475569',
  titleColor: '#0f172a',
  edgeLabelBackground: '#f8fafc',
  primaryColor: '#e2e8f0',
  primaryBorderColor: '#94a3b8',
  primaryTextColor: '#0f172a',
  nodeBorder: '#94a3b8',
  textColor: '#0f172a',
};

/** True when the on-screen diagram is currently configured for a dark palette. */
export function isDiagramDark(): boolean {
  return _diagramTheme === 'dark';
}

/** Force the next applyMermaidTheme() call to re-initialise even if the app theme is
 *  unchanged — used to restore config after the export rasterizer overrides it. */
export function invalidateMermaidTheme(): void {
  _diagramTheme = null;
}

/**
 * (Re)configure Mermaid for the given APP theme. The diagram is inverted relative to the
 * app so it always contrasts against the page: app dark → light diagram; app light → dark
 * diagram. No-ops when already configured for the requested palette.
 */
export function applyMermaidTheme(appTheme: Theme): void {
  const diagramDark = appTheme === 'light'; // invert
  const wanted = diagramDark ? 'dark' : 'light';
  if (_diagramTheme === wanted) return;
  _diagramTheme = wanted;
  mermaid.initialize({
    startOnLoad: false,
    securityLevel: 'loose',
    fontFamily: 'inherit',
    theme: diagramDark ? 'dark' : 'default',
    themeVariables: diagramDark ? DARK_VARS : LIGHT_VARS,
  });
}

/** Characters that Mermaid treats as shape delimiters and therefore cannot appear
 *  unescaped inside a node label. */
const BREAKER = /[()\[\]{}]/;

// Plain-ASCII placeholder markers used to stash protected compound-shape tokens while
// the simple-shape pass runs. They carry no shape-delimiter chars (so the simple pass
// ignores them) and never occur in real Mermaid source (so the restore can't collide).
const PH_OPEN = 'MERMAIDPH';
const PH_CLOSE = 'ENDPH';
const PH_RESTORE = /MERMAIDPH(\d+)ENDPH/g;

/** Quote a node label's inner text so Mermaid treats it as literal — but only when it
 *  actually contains a breaker char and is not already double-quoted. Literal double
 *  quotes are encoded as Mermaid's `#quot;` entity. */
function quoteInner(inner: string): string {
  if (inner.length >= 2 && inner.startsWith('"') && inner.endsWith('"')) return inner;
  if (!BREAKER.test(inner)) return inner;
  return '"' + inner.replace(/"/g, '#quot;') + '"';
}

// Compound node shapes — matched (and protected) before the simple shapes so a cylinder
// `[(text)]` or circle `((text))` is never mis-parsed as a rectangle/round shape.
const COMPOUND_SHAPES: ReadonlyArray<readonly [string, string, RegExp]> = [
  ['[[', ']]', /\[\[([\s\S]*?)\]\]/g], // subroutine
  ['[(', ')]', /\[\(([\s\S]*?)\)\]/g], // cylinder / database
  ['([', '])', /\(\[([\s\S]*?)\]\)/g], // stadium
  ['((', '))', /\(\(([\s\S]*?)\)\)/g], // circle
  ['{{', '}}', /\{\{([\s\S]*?)\}\}/g], // hexagon
];

const SIMPLE_SHAPES: ReadonlyArray<readonly [string, string, RegExp]> = [
  ['[', ']', /\[([^\[\]]*?)\]/g], // rectangle
  ['(', ')', /\(([^()]*?)\)/g],   // round
  ['{', '}', /\{([^{}]*?)\}/g],   // rhombus
];

// Structural lines carry diagram syntax (not labels) and must never be rewritten.
const STRUCTURAL =
  /^\s*(graph|flowchart|sequenceDiagram|classDiagram|stateDiagram|erDiagram|gantt|pie|journey|subgraph|end|%%|classDef|class\s|style\s|linkStyle|click|direction)\b/;

/**
 * Always-on readability tidy applied BEFORE the first render (unlike the repair pass, which only
 * runs on failure). It is strictly semantics-preserving — it never changes node/edge structure or
 * label *text*, only removes rendering noise the LLM + JSON single-quote substitution introduce:
 *   • De-quotes edge labels — the model quotes them (`-->|"X"|`) and the document pipeline converts
 *     every double-quote to a single-quote, so Mermaid renders the literal quotes (`|'X'|`).
 *   • Normalises custom node fills/colours to a readable dark-fill + light-text, so light-filled
 *     "highlight" nodes aren't light-text-on-light-fill. Directives are rewritten in place (never
 *     dropped) so a `class` line never ends up referencing a removed `classDef`.
 */
export function tidyMermaid(src: string, diagramDark = true): string {
  const fill = diagramDark ? '#1f2937' : '#e2e8f0';
  const color = diagramDark ? '#e5e7eb' : '#0f172a';
  return src
    // De-quote edge labels — BUT keep (and normalise to a double-quote) any label whose text contains
    // a shape-delimiter char: `|"PR Webhook (JSON)"|` MUST stay quoted or the flow parse aborts on the
    // `(`. Only breaker-free labels are safe to unquote (the common `-->|"X"|` / doc-pipeline `|'X'|`).
    .replace(/\|(\s*)(['"])([^|]*?)\2(\s*)\|/g, (_m, s1: string, _q: string, inner: string, s2: string) =>
      BREAKER.test(inner) ? `|${s1}"${inner.replace(/"/g, '#quot;')}"${s2}|` : `|${s1}${inner}${s2}|`)
    .replace(/(--+|-\.-*|==+)(\s*)(['"])([^'"]*?)\3(\s*)(--+>|-\.->|==+>|--+|===+)/g,
      (_m, a: string, s1: string, _q: string, inner: string, s2: string, b: string) =>
        BREAKER.test(inner) ? `${a}${s1}"${inner.replace(/"/g, '#quot;')}"${s2}${b}` : `${a}${s1}${inner}${s2}${b}`)
    .split('\n')
    .map((line) => {
      if (!/^\s*(style|classDef)\s/.test(line)) return line;
      return line
        .replace(/fill\s*:\s*[^,;]+/g, `fill:${fill}`)
        .replace(/color\s*:\s*[^,;]+/g, `color:${color}`);
    })
    .join('\n');
}

/**
 * Repairs an edge that carries a trailing ` : label` annotation — a PlantUML/sequence-style
 * suffix the LLM sometimes appends (`A -- X --> B : Y`), which is a hard parse error in a flowchart.
 * If the edge already has a label (inline `-- X -->` or pipe `|X|`), the redundant trailing label is
 * dropped; otherwise it is moved onto the arrow as `|label|` so nothing is lost. Only the top-level
 * trailing colon is touched — colons inside node shapes (`B[x: y]`) are left alone.
 */
function repairTrailingEdgeLabel(line: string): string {
  const m = line.match(
    /^(\s*)(\S.*?(?:-->|--[ox]|---|-\.->|-\.-|==+>|===+)\s*(?:\|[^|]*\|\s*)?[A-Za-z0-9_-]+(?:\[[^\]]*\]|\([^)]*\)|\{[^}]*\})?)\s*:\s*(\S.*?)\s*$/,
  );
  if (!m) return line;
  const [, indent, edge, trailingRaw] = m;
  // MERGE (do not drop): Y is the mechanism/protocol ("SAML/OAuth", "ExpressRoute") — the most useful
  // detail on an architecture edge. Escape any pipe so it can't terminate the label.
  const y = trailingRaw.replace(/\|/g, '#124;').trim();

  // Pipe-labelled edge → merge into the existing pipe: |X| -> |X — Y|
  if (/\|[^|]*\|/.test(edge)) {
    return indent + edge.replace(/\|([^|]*)\|/, (_full, x: string) => `|${x.trim()} — ${y}|`);
  }

  // Inline-labelled edge (`A -- X --> B`, `== X ==>`, `-. X .->`) → normalize to pipe form and merge.
  const inline = edge.match(/^(.*?)(--|==|-\.)\s+(\S.*?)\s*(?:--+>|--[ox]|==+>|===+|-?\.->|-\.-|---)\s*(.*)$/);
  if (inline) {
    const [, pre, opener, x, rest] = inline;
    const arrow = opener === '==' ? '==>' : opener === '-.' ? '-.->' : '-->';
    return `${indent}${pre.trimEnd()} ${arrow}|${x.trim()} — ${y}| ${rest.trim()}`.replace(/\s+$/, '');
  }

  // Bare arrow → attach the trailing text as the edge's label (nothing was lost before).
  return indent + edge.replace(
    /(-->|--[ox]|---|-\.->|-\.-|==+>|===+)(\s*)([A-Za-z0-9_-]+(?:\[[^\]]*\]|\([^)]*\)|\{[^}]*\})?)\s*$/,
    (_m, arrow: string, _sp: string, target: string) => `${arrow}|${y}| ${target}`,
  );
}

// Arrow operators that separate nodes on a flowchart edge line. No `g` flag — String.split
// handles all occurrences regardless, and this avoids lastIndex state bugs across calls.
const FLOW_ARROW = /(<-->|<==>|-->|--[ox]|---|-\.->|-\.-|==+>|===+)/;

/** A bare node reference that is invalid as an id — contains a space or slash and is not
 *  already shaped (`id[...]`, `id(...)`, `id{...}`) or quoted. */
function isBareComplexNode(ref: string): boolean {
  return /^[A-Za-z0-9][A-Za-z0-9 _/-]*$/.test(ref) && /[ /]/.test(ref);
}

/** Stable single-token id for a bare label (slug of its alphanumerics, alpha-leading, deduped). */
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

/** Quote a pipe-edge label whose text contains a shape-delimiter char, e.g.
 *  `|PR Webhook (JSON)|` → `|"PR Webhook (JSON)"|`. Unquoted parens/brackets abort the flow parse
 *  exactly as they do inside a node label. No-op for already-quoted or breaker-free labels (those
 *  render fine unquoted, and quoting them would add visible quote glyphs). */
function quoteEdgeLabels(line: string): string {
  return line.replace(/\|([^|]*)\|/g, (_m, inner: string) => {
    const t = inner.trim();
    if (!BREAKER.test(t)) return `|${inner}|`;
    if (t.length >= 2 && t.startsWith('"') && t.endsWith('"')) return `|${t}|`;
    return `|"${t.replace(/"/g, '#quot;')}"|`;
  });
}

/** Rewrite one bare node slot to `id["Label"]`; leave shaped/quoted/single-token refs untouched. */
function rewriteNodeSlot(node: string, map: Map<string, string>, used: Set<string>): string {
  const t = node.trim();
  if (!isBareComplexNode(t)) return node;
  const id = bareNodeId(t, map, used);
  return `${id}["${t.replace(/"/g, '#quot;')}"]`;
}

/**
 * Repairs the most common hard failure: multi-word phrases used as BARE node identifiers on edge
 * lines and as subgraph members (`User --> Azure Front Door`, or `Azure Front Door` on its own line).
 * Mermaid ids must be single tokens, so each distinct phrase is assigned a stable id and rewritten to
 * `id["Phrase"]` at every occurrence (a repeated `id["Phrase"]` is valid Mermaid). Splitting on arrow
 * operators means whole node slots are replaced — no substring/longest-prefix collisions. Slashes in
 * edge labels are PRESERVED (valid Mermaid; e.g. "SAML/OAuth") — only the terminating `|` is escaped.
 */
function rewriteBareFlowNodes(line: string, map: Map<string, string>, used: Set<string>): string {
  const indent = line.match(/^\s*/)?.[0] ?? '';
  const body = line.trim();
  if (!body) return line;

  const parts = body.split(FLOW_ARROW);
  if (parts.length === 1) {
    // No arrow → a standalone / subgraph-member node line.
    return indent + rewriteNodeSlot(body, map, used);
  }

  // Even indices are node segments; odd indices are the arrow operators (preserved verbatim).
  const rebuilt = parts.map((part, i) => {
    if (i % 2 === 1) return part;
    // A segment may lead with an edge label pipe (`|label| target`) after the preceding arrow.
    const m = part.match(/^\s*(\|[^|]*\|)?\s*([\s\S]*?)\s*$/);
    const pipe = m?.[1] ?? '';
    const node = m?.[2] ?? '';
    const normPipe = pipe ? `|${pipe.slice(1, -1).trim()}| ` : '';
    return normPipe + rewriteNodeSlot(node, map, used);
  });
  return indent + rebuilt.join(' ');
}

/**
 * Best-effort repair for Mermaid source that failed to parse because a node label
 * contains a shape-delimiter char (the LLM frequently emits e.g. `J[Compute (GPU/TPU)]`,
 * and a `(` inside `[...]` aborts the whole render). Only ever runs on already-broken
 * input, so it can be aggressive: it wraps offending label text in double quotes, which
 * Mermaid honours as a literal. Valid diagrams are never passed through here.
 */
export function sanitizeMermaidLabels(src: string): string {
  const lines = src.split('\n');

  // The diagram type decides what is safe to rewrite: ( ) [ ] { } are node-shape delimiters in
  // flowcharts/graphs, but ORDINARY TEXT in sequenceDiagram/classDiagram/stateDiagram/erDiagram
  // (message bodies, method signatures, etc.). Quoting them everywhere would corrupt those diagram
  // types — so the node-label pass runs only for flow-style diagrams. (Subgraph-title repair below
  // is safe for any type because a `(` in a subgraph title is a hard parse error regardless.)
  const header = lines.find((l) => l.trim() && !l.trim().startsWith('%%'))?.trim() ?? '';
  const isFlow = /^(graph|flowchart)\b/.test(header);

  // Pass 0 — a subgraph title with a space or a breaker char cannot be safely referenced by an edge
  // (`C --> Secondary Hyperscaler AWS GCP` is a parse error: ids can't contain spaces). Give each
  // such title a short id (`sgN`) so the declaration becomes `subgraph sgN["Title"]` and every edge
  // that referenced the bare title is rewritten to the id — which points into the subgraph. Clean
  // single-word titles and explicit-id/quoted forms are left as-is.
  const sgIds = new Map<string, string>();
  lines.forEach((line, idx) => {
    const m = line.match(/^(\s*subgraph\s+)(\S.*?)\s*$/);
    if (!m) return;
    const title = m[2];
    if (title.startsWith('"') || /^[\w-]+\s*\[/.test(title)) return; // already id[..] / quoted
    if (!/\s/.test(title) && !BREAKER.test(title)) return;           // clean single token — referenceable
    sgIds.set(title, `sg${idx}`);
  });

  // Bare multi-word NODE ids (edge endpoints + subgraph members) get their own stable ids, seeded
  // from the subgraph map so a member that matches a title resolves to the same id.
  const nodeIds = new Map<string, string>(sgIds);
  const usedIds = new Set<string>(sgIds.values());

  return lines
    .map((line) => {
      // Subgraph declaration → bracketed-id form so the title is a literal and the id is referenceable.
      const sg = line.match(/^(\s*subgraph\s+)(\S.*?)\s*$/);
      if (sg) {
        const id = sgIds.get(sg[2]);
        if (id) return `${sg[1]}${id}["${sg[2].replace(/"/g, '#quot;')}"]`;
        // Explicit-id form `subgraph id[Label]`: the shape passes below skip subgraph lines, so the
        // bracketed label must be quoted HERE if it holds a breaker char — otherwise a `(` in the
        // label (`subgraph X[Cloud Provider (e.g., Azure)]`) aborts the whole parse.
        const idLabel = sg[2].match(/^([A-Za-z0-9_-]+)\s*\[([\s\S]*)\]$/);
        if (idLabel) return `${sg[1]}${idLabel[1]}[${quoteInner(idLabel[2])}]`;
        return line;
      }

      // Rewrite any bare reference to a mapped subgraph title (e.g. an edge endpoint) to its id.
      let out = line;
      for (const [title, id] of sgIds) if (out.includes(title)) out = out.split(title).join(id);

      // Only flow-style diagrams get node-label quoting; other diagram types are left untouched so
      // their parentheses/brackets (which are not node shapes there) are never mangled.
      if (!isFlow || STRUCTURAL.test(out)) return out;

      // Move any trailing ` : label` edge annotation onto the arrow (or drop it if redundant).
      out = repairTrailingEdgeLabel(out);

      // Rewrite bare multi-word node identifiers (the #1 hard parse error) to `id["Label"]`.
      out = rewriteBareFlowNodes(out, nodeIds, usedIds);

      // Quote edge-label text containing shape-delimiter chars (`|PR Webhook (JSON)|`) — must run
      // before the shape passes so the quoted `(…)` inside the label isn't re-treated as a node shape.
      out = quoteEdgeLabels(out);

      // 1. Protect compound shapes — repair their inner text, then swap the whole token
      //    for a placeholder so the simple-shape pass can't corrupt them.
      const stash: string[] = [];
      let work = out;
      for (const [open, close, re] of COMPOUND_SHAPES) {
        work = work.replace(re, (_m, inner: string) => {
          const ph = PH_OPEN + stash.length + PH_CLOSE;
          stash.push(open + quoteInner(inner) + close);
          return ph;
        });
      }

      // 2. Repair simple shapes on the remaining text.
      for (const [open, close, re] of SIMPLE_SHAPES) {
        work = work.replace(re, (_m, inner: string) => open + quoteInner(inner) + close);
      }

      // 3. Restore the protected compound tokens.
      return work.replace(PH_RESTORE, (_m, i: string) => stash[Number(i)]);
    })
    .join('\n');
}

/** Remove any error node Mermaid injects into the DOM when a render call throws, so a
 *  retry (or a following render) doesn't leave a stray "Syntax error" bomb behind. Mermaid
 *  appends the failed render under both `#<id>` and `#d<id>`. Exported so the export
 *  rasterizer can clean up after its own off-screen renders too. */
export function removeOrphanNode(id: string): void {
  document.getElementById(id)?.remove();
  document.getElementById('d' + id)?.remove();
}

/**
 * Attach to any element that renders markdown via [innerHTML] to automatically
 * convert `mermaid-pending` placeholder divs (emitted by MarkdownPipe for
 * ```mermaid fences) into rendered SVG diagrams.
 *
 * Usage:  <div [innerHTML]="content | markdown" appMermaid></div>
 */
@Directive({ selector: '[appMermaid]', standalone: true })
export class MermaidDirective implements AfterViewInit, OnDestroy {
  private observer?: MutationObserver;
  private _seq = 0;
  private _viewReady = false;
  private readonly theme = inject(ThemeService);

  constructor(private el: ElementRef<HTMLElement>) {
    // React to app theme changes: re-configure Mermaid (inverted vs the app) and
    // re-render any already-rendered diagrams so they flip palette live.
    effect(() => {
      applyMermaidTheme(this.theme.theme());
      if (this._viewReady) this.rerenderAll();
    });
  }

  ngAfterViewInit(): void {
    this._viewReady = true;
    // Process anything already in the DOM when the view first renders.
    this.render();

    // Watch for subsequent [innerHTML] updates — use subtree:true so that
    // mermaid blocks nested inside lists or blockquotes are also detected.
    this.observer = new MutationObserver(() => this.render());
    this.observer.observe(this.el.nativeElement, { childList: true, subtree: true });
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
  }

  /** Inject rendered SVG into the placeholder and normalise its sizing. */
  private applySvg(node: HTMLElement, svg: string): void {
    node.innerHTML = svg;
    node.classList.replace('mermaid-rendering', 'mermaid-rendered');
    const svgEl = node.querySelector('svg');
    if (svgEl) {
      svgEl.removeAttribute('height');
      svgEl.setAttribute('width', '100%');
    }
  }

  /** Last resort: show the raw source as a code block instead of a blank space. */
  private fallbackToCode(node: HTMLElement, src: string): void {
    const pre = document.createElement('pre');
    pre.className = 'md-pre';
    const code = document.createElement('code');
    code.className = 'md-code';
    code.textContent = src;
    pre.appendChild(code);
    node.replaceWith(pre);
  }

  /** Reset already-rendered diagrams back to pending (from their retained data-src) and
   *  re-render them — used when the app theme flips so diagrams adopt the new palette. */
  private rerenderAll(): void {
    const done = Array.from(
      this.el.nativeElement.querySelectorAll<HTMLElement>('.mermaid-rendered, .mermaid-rendering'),
    );
    for (const node of done) {
      if (!node.dataset['src']) continue; // fell back to a code block — nothing to re-render
      node.classList.remove('mermaid-rendered', 'mermaid-rendering');
      node.classList.add('mermaid-pending');
      node.innerHTML = '';
    }
    this.render();
  }

  private render(): void {
    const pending = Array.from(
      this.el.nativeElement.querySelectorAll<HTMLElement>('.mermaid-pending'),
    );
    if (!pending.length) return;

    // Mark all nodes immediately — prevents double-processing if the
    // observer fires again before the async renders complete.
    for (const node of pending) {
      node.classList.replace('mermaid-pending', 'mermaid-rendering');
    }

    for (const node of pending) {
      const raw = decodeURIComponent(node.dataset['src'] ?? '');
      if (!raw) { node.remove(); continue; }
      // Always-on readability tidy (de-quote edge labels, normalise unreadable custom colours).
      const src = tidyMermaid(raw, isDiagramDark());

      // Id must start with a letter and be unique within the page.
      const id = `mermaid${++this._seq}`;

      // First attempt: render the source untouched so working diagrams are never altered.
      mermaid
        .render(id, src)
        .then(({ svg }) => this.applySvg(node, svg))
        .catch((err: unknown) => {
          // Clean up the error node Mermaid injected before any retry.
          removeOrphanNode(id);

          // Second attempt: repair break-prone labels, but only if sanitizing changed
          // anything — otherwise there's nothing new to try.
          const repaired = sanitizeMermaidLabels(src);
          if (repaired === src) {
            console.warn('[MermaidDirective] render failed — showing source as code block.\nError:', err, '\nSource:', src);
            this.fallbackToCode(node, src);
            return;
          }

          const retryId = `mermaid${++this._seq}`;
          mermaid
            .render(retryId, repaired)
            .then(({ svg }) => this.applySvg(node, svg))
            .catch((retryErr: unknown) => {
              removeOrphanNode(retryId);
              console.warn('[MermaidDirective] render failed after sanitize — showing source as code block.\nError:', retryErr, '\nSource:', src);
              this.fallbackToCode(node, src);
            });
        });
    }
  }
}
