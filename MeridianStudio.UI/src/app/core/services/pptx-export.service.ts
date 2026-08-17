import { Injectable, inject } from '@angular/core';
import PptxGenJS from 'pptxgenjs';
import { WorkspaceStoreService } from './workspace-store.service';
import { PrioritizedItem, ResearchResponse } from '../models/interfaces';

type Slide = ReturnType<PptxGenJS['addSlide']>;

// ── Layout constants (LAYOUT_WIDE = 13.33" × 7.5") ───────────────────────────
const W  = 13.33;  // slide width
const H  = 7.5;   // slide height
const LM = 0.55;  // left margin
const RM = 0.55;  // right margin
const CW = W - LM - RM;  // usable content width

// Header zone
const HDR_TITLE_Y  = 0.58;
const HDR_SUB_Y    = 1.22;
const HDR_LINE_Y   = 1.55;
const BODY_TOP     = 1.72;
const FOOTER_Y     = 7.1;

// Colours (hex, no #)
const C = {
  bg:      '09091A',
  white:   'FFFFFF',
  g300:    'D1D5DB',
  g400:    '9CA3AF',
  g500:    '6B7280',
  g600:    '4B5563',
  g700:    '374151',
  g800:    '1F2937',
  g900:    '111827',
  indigo:  '6366F1',
  red:     'EF4444',
  blue:    '3B82F6',
  emerald: '10B981',
  violet:  '8B5CF6',
  teal:    '14B8A6',
  amber:   'F59E0B',
  sky:     '0EA5E9',
  green:   '22C55E',
  orange:  'F97316',
  cyan:    '06B6D4',
} as const;

/** Slide accent colour by index (0-based) */
const ACCENT = [
  C.indigo, C.red, C.blue, C.emerald,
  C.violet, C.teal, C.amber, C.sky, C.green,
];

/** Dark background colour by index */
const SLIDE_BG = [
  '07051A', '120404', '031020', '031209',
  '0A0320', '041012', '120900', '030C18', '031109',
];

@Injectable({ providedIn: 'root' })
export class PptxExportService {
  private readonly store = inject(WorkspaceStoreService);

  // ── Public API ────────────────────────────────────────────────────────────

  async exportToPptx(): Promise<void> {
    const sol = this.store.selectedSolution();
    if (!sol) return;

    const pptx = new PptxGenJS();
    pptx.layout  = 'LAYOUT_WIDE';
    pptx.author  = 'Meridian Studio';
    pptx.title   = sol.name;
    pptx.subject = 'Executive Summary Deck';

    const research = this.store.currentResearchData();

    this.s01_summary(pptx, sol, 0);
    this.s02_problem(pptx, sol, research, 1);
    this.s03_solution(pptx, sol, 2);
    this.s04_roi(pptx, sol, 3);
    this.s05_scope(pptx, sol, research, 4);
    this.s06_implementation(pptx, sol, 5);
    this.s07_risks(pptx, sol, 6);
    this.s08_investment(pptx, sol, 7);
    this.s09_nextSteps(pptx, sol, 8);

    const name = sol.name.replace(/[^a-z0-9]+/gi, '-').toLowerCase().slice(0, 50);
    await pptx.writeFile({ fileName: `${name}-executive-deck.pptx` });
  }

  // ── Low-level helpers ─────────────────────────────────────────────────────

  /** Slide header: badge, section number, title, subtitle, accent line */
  private chrome(
    sl: Slide, idx: number,
    label: string, title: string, subtitle: string,
  ): void {
    const accent = ACCENT[idx];

    // Accent badge
    sl.addText(label.toUpperCase(), {
      x: LM, y: 0.18, w: 1.3, h: 0.26,
      fontSize: 7.5, bold: true, color: C.white,
      align: 'center', valign: 'middle', fontFace: 'Segoe UI',
      fill: { color: accent, transparency: 70 },
      line: { color: accent, transparency: 50, pt: 0.8 },
    });

    // Section counter
    sl.addText(`${String(idx + 1).padStart(2, '0')} / 09`, {
      x: LM + 1.38, y: 0.18, w: 1.4, h: 0.26,
      fontSize: 7.5, bold: true, color: accent, fontFace: 'Segoe UI', valign: 'middle',
    });

    // Branding top-right
    sl.addText('MERIDIAN STUDIO', {
      x: W - RM - 3, y: 0.18, w: 3, h: 0.26,
      fontSize: 7.5, bold: true, color: C.g700,
      align: 'right', fontFace: 'Segoe UI', valign: 'middle',
    });

    // Title
    sl.addText(title, {
      x: LM, y: HDR_TITLE_Y, w: 10.5, h: 0.72,
      fontSize: 26, bold: true, color: C.white, fontFace: 'Segoe UI',
    });

    // Subtitle
    sl.addText(subtitle, {
      x: LM, y: HDR_SUB_Y, w: 11, h: 0.28,
      fontSize: 9.5, color: C.g400, fontFace: 'Segoe UI',
    });

    // Accent underline
    sl.addShape('rect', {
      x: LM, y: HDR_LINE_Y, w: 1.2, h: 0.045,
      fill: { color: accent }, line: { color: accent, pt: 0 },
    });

    // Footer
    sl.addText('Meridian Studio  ·  AI Solution Agent & System Architect Hub', {
      x: LM, y: FOOTER_Y + 0.1, w: 8, h: 0.22,
      fontSize: 7, color: C.g700, fontFace: 'Segoe UI',
    });
    sl.addText(`${idx + 1} / 09`, {
      x: W - RM - 1.2, y: FOOTER_Y + 0.1, w: 1.2, h: 0.22,
      fontSize: 7, color: C.g700, fontFace: 'Segoe UI', align: 'right',
    });
  }

  /** Card (rounded rect with coloured border + subtle fill) */
  private card(
    sl: Slide, x: number, y: number, w: number, h: number,
    accent: string, fillTr = 88, borderTr = 55,
  ): void {
    sl.addShape('roundRect', {
      x, y, w, h,
      fill: { color: accent, transparency: fillTr },
      line: { color: accent, transparency: borderTr, pt: 0.8 },
    });
  }

  /** 10-segment score bar */
  private segBar(
    sl: Slide, x: number, y: number, totalW: number,
    value: number, accent: string,
  ): void {
    const gap = 0.025;
    const sw  = (totalW - gap * 9) / 10;
    for (let i = 0; i < 10; i++) {
      sl.addShape('roundRect', {
        x: x + i * (sw + gap), y, w: sw, h: 0.1,
        fill: { color: i < value ? accent : C.g700 },
        line: { color: i < value ? accent : C.g700, pt: 0 },
      });
    }
  }

  /** Bold + muted text pair inside a card */
  private cardText(
    sl: Slide, label: string, value: string,
    x: number, y: number, w: number, accent: string,
  ): void {
    sl.addText(label.toUpperCase(), {
      x, y, w, h: 0.2,
      fontSize: 7, bold: true, color: C.g500, fontFace: 'Segoe UI',
    });
    sl.addText(value, {
      x, y: y + 0.2, w, h: 0.35,
      fontSize: 11, bold: true, color: accent, fontFace: 'Segoe UI',
    });
  }

  // ── Data helpers (mirror component logic) ─────────────────────────────────

  private roiPct(sol: PrioritizedItem): number {
    return Math.round((sol.value / Math.max(sol.difficulty, 1)) * sol.urgency * 12);
  }

  private effortShort(d: number): string {
    return d <= 3 ? 'Low' : d <= 6 ? 'Medium' : 'High';
  }

  private budgetRange(d: number): string {
    return d <= 3 ? '$25K–$75K' : d <= 6 ? '$75K–$250K' : '$250K–$750K';
  }

  private timeline(d: number): string {
    return d <= 3 ? '12–16 wks' : d <= 6 ? '20–26 wks' : '28–36 wks';
  }

  private breakeven(d: number): string {
    return d <= 3 ? '2–3 months' : d <= 6 ? '4–6 months' : '8–12 months';
  }

  private urgencyShort(u: number): string {
    return u >= 8 ? 'act this sprint' : u >= 6 ? 'act this quarter' : 'schedule next cycle';
  }

  private urgencyFull(u: number): string {
    if (u >= 8) return 'Critical window — competitors are moving now. Delay erodes market position exponentially.';
    if (u >= 6) return 'High momentum — acting this quarter secures first-mover advantage.';
    return 'Moderate timing pressure — schedule within the next planning cycle.';
  }

  private painPoints(sol: PrioritizedItem): string[] {
    const raw = [sol.description, sol.rationale].filter(Boolean).join(' ');
    const sentences = raw.match(/[^.!?]+[.!?]+/g) ?? [];
    const pain = sentences
      .filter(s => /challenge|problem|gap|lack|slow|manual|inefficient|costly|risk|complex/i.test(s))
      .map(s => s.trim()).slice(0, 4);
    return pain.length >= 2 ? pain : [
      `Current ${sol.name.toLowerCase()} processes are largely manual, creating bottlenecks.`,
      `Teams lack unified visibility, forcing reactive rather than proactive decisions.`,
      `The absence of this capability creates measurable competitive exposure.`,
      `Peer organisations adopting comparable solutions report significant advantages.`,
    ];
  }

  private execBullets(sol: PrioritizedItem): { label: string; detail: string }[] {
    return [
      { label: sol.name, detail: sol.description.slice(0, 140) },
      { label: 'Business Impact', detail: sol.realLifeValue || `Projected ${sol.value * 10}% operational efficiency uplift.` },
      { label: 'Market Urgency', detail: this.urgencyFull(sol.urgency) },
      { label: 'Implementation Profile', detail: `${this.effortShort(sol.difficulty)} complexity · ${sol.difficulty}/10 difficulty · ${this.timeline(sol.difficulty)} total delivery.` },
      { label: 'Recommended Action', detail: 'Approve Phase 1 this planning cycle to capture first-mover advantage and begin realising value within 8 weeks.' },
    ];
  }

  // ── Slide 01 — Executive Summary ─────────────────────────────────────────

  private s01_summary(pptx: PptxGenJS, sol: PrioritizedItem, idx: number): void {
    const sl = pptx.addSlide();
    sl.background = { color: SLIDE_BG[idx] };
    const ac = ACCENT[idx]; // indigo
    this.chrome(sl, idx, 'Summary', 'Executive Summary',
      'The complete proposal — for decision-makers who read only one slide');

    const bullets = this.execBullets(sol);
    const lw = CW * 0.62;

    // Numbered bullet cards
    bullets.forEach((b, i) => {
      const cy = BODY_TOP + i * 0.975;

      // Number circle background
      sl.addShape('ellipse', {
        x: LM, y: cy + 0.12, w: 0.33, h: 0.33,
        fill: { color: ac, transparency: 70 },
        line: { color: ac, transparency: 50, pt: 0.8 },
      });
      sl.addText(`${i + 1}`, {
        x: LM, y: cy + 0.12, w: 0.33, h: 0.33,
        fontSize: 8, bold: true, color: C.white,
        align: 'center', valign: 'middle', fontFace: 'Segoe UI',
      });

      // Card
      this.card(sl, LM + 0.42, cy, lw - 0.42, 0.88, ac, 88, 60);
      sl.addText(b.label, {
        x: LM + 0.56, y: cy + 0.06, w: lw - 0.7, h: 0.25,
        fontSize: 10, bold: true, color: C.white, fontFace: 'Segoe UI',
      });
      sl.addText(b.detail, {
        x: LM + 0.56, y: cy + 0.31, w: lw - 0.7, h: 0.48,
        fontSize: 8.5, color: C.g400, fontFace: 'Segoe UI', wrap: true,
      });
    });

    // Right column — metrics
    const rx = LM + lw + 0.2;
    const rw = W - rx - RM;

    // Business Value
    sl.addText('BUSINESS VALUE', { x: rx, y: BODY_TOP, w: rw, h: 0.2, fontSize: 7, bold: true, color: C.g500, fontFace: 'Segoe UI' });
    this.card(sl, rx, BODY_TOP + 0.22, rw, 1.5, ac, 90);
    sl.addText(`${sol.value}`, { x: rx + 0.15, y: BODY_TOP + 0.32, w: 1.5, h: 0.9, fontSize: 52, bold: true, color: C.white, fontFace: 'Segoe UI' });
    sl.addText('/10', { x: rx + 1.4, y: BODY_TOP + 0.85, w: 0.9, h: 0.35, fontSize: 14, color: C.g500, fontFace: 'Segoe UI' });
    this.segBar(sl, rx + 0.1, BODY_TOP + 1.45, rw - 0.2, sol.value, ac);

    // Market Urgency
    sl.addText('MARKET URGENCY', { x: rx, y: BODY_TOP + 1.88, w: rw, h: 0.2, fontSize: 7, bold: true, color: C.g500, fontFace: 'Segoe UI' });
    this.card(sl, rx, BODY_TOP + 2.1, rw, 1.5, C.red, 90);
    sl.addText(`${sol.urgency}`, { x: rx + 0.15, y: BODY_TOP + 2.2, w: 1.5, h: 0.9, fontSize: 52, bold: true, color: C.white, fontFace: 'Segoe UI' });
    sl.addText('/10', { x: rx + 1.4, y: BODY_TOP + 2.73, w: 0.9, h: 0.35, fontSize: 14, color: C.g500, fontFace: 'Segoe UI' });
    this.segBar(sl, rx + 0.1, BODY_TOP + 3.33, rw - 0.2, sol.urgency, C.red);

    // ROI + Effort chips
    const cw2 = (rw - 0.1) / 2;
    this.card(sl, rx, BODY_TOP + 3.9, cw2, 0.72, ac, 85);
    this.cardText(sl, 'Est. ROI', `~${this.roiPct(sol)}%`, rx + 0.1, BODY_TOP + 3.95, cw2 - 0.2, ac);
    this.card(sl, rx + cw2 + 0.1, BODY_TOP + 3.9, cw2, 0.72, ac, 85);
    this.cardText(sl, 'Effort', this.effortShort(sol.difficulty), rx + cw2 + 0.2, BODY_TOP + 3.95, cw2 - 0.2, C.white);
  }

  // ── Slide 02 — Problem ───────────────────────────────────────────────────

  private s02_problem(pptx: PptxGenJS, sol: PrioritizedItem, research: ResearchResponse | null, idx: number): void {
    const sl = pptx.addSlide();
    sl.background = { color: SLIDE_BG[idx] };
    const ac = ACCENT[idx]; // red
    this.chrome(sl, idx, 'Problem', 'Problem & Opportunity Statement',
      'The gap between current state and what\'s possible — quantified');

    const lw = CW * 0.5 - 0.15;
    const rx = LM + lw + 0.3;
    const rw = CW - lw - 0.3;

    // Left: pain points
    sl.addText('CURRENT STATE — WHAT\'S BROKEN', { x: LM, y: BODY_TOP, w: lw, h: 0.2, fontSize: 7, bold: true, color: ac, fontFace: 'Segoe UI' });
    const pts = this.painPoints(sol);
    pts.forEach((p, i) => {
      const cy = BODY_TOP + 0.28 + i * 0.9;
      this.card(sl, LM, cy, lw, 0.78, ac, 90, 60);
      sl.addShape('ellipse', { x: LM + 0.12, y: cy + 0.23, w: 0.18, h: 0.18, fill: { color: ac, transparency: 40 }, line: { color: ac, pt: 0 } });
      sl.addText(p, { x: LM + 0.38, y: cy + 0.08, w: lw - 0.5, h: 0.6, fontSize: 9, color: C.g300, fontFace: 'Segoe UI', wrap: true });
    });

    // Cost of inaction
    const ciY = BODY_TOP + 0.28 + pts.length * 0.9;
    this.card(sl, LM, ciY, lw, 0.72, C.orange, 88, 55);
    sl.addText('COST OF INACTION', { x: LM + 0.15, y: ciY + 0.08, w: lw - 0.3, h: 0.2, fontSize: 7.5, bold: true, color: C.orange, fontFace: 'Segoe UI' });
    sl.addText(`Urgency ${sol.urgency}/10 — ${this.urgencyShort(sol.urgency)}`, { x: LM + 0.15, y: ciY + 0.3, w: lw - 0.3, h: 0.3, fontSize: 10, color: C.g300, fontFace: 'Segoe UI' });

    // Right: opportunity gap
    sl.addText('THE OPPORTUNITY GAP', { x: rx, y: BODY_TOP, w: rw, h: 0.2, fontSize: 7, bold: true, color: ac, fontFace: 'Segoe UI' });
    this.card(sl, rx, BODY_TOP + 0.28, rw, 2.4, ac, 92, 60);
    sl.addText('DESIRED FUTURE STATE', { x: rx + 0.2, y: BODY_TOP + 0.38, w: rw - 0.4, h: 0.22, fontSize: 7.5, bold: true, color: C.g400, fontFace: 'Segoe UI' });
    sl.addText(sol.realLifeValue || sol.description, { x: rx + 0.2, y: BODY_TOP + 0.62, w: rw - 0.4, h: 1.1, fontSize: 9.5, color: C.g300, fontFace: 'Segoe UI', wrap: true });
    if (sol.rationale) {
      sl.addText(`"${sol.rationale}"`, { x: rx + 0.2, y: BODY_TOP + 1.78, w: rw - 0.4, h: 0.75, fontSize: 8.5, color: C.g500, italic: true, fontFace: 'Segoe UI', wrap: true });
    }

    // Competitors
    const comps = research?.competitorInsights?.slice(0, 3) ?? [];
    if (comps.length) {
      sl.addText('PEER ADOPTION PRESSURE', { x: rx, y: BODY_TOP + 2.82, w: rw, h: 0.2, fontSize: 7, bold: true, color: ac, fontFace: 'Segoe UI' });
      comps.forEach((ci, i) => {
        const cy = BODY_TOP + 3.08 + i * 0.82;
        this.card(sl, rx, cy, rw, 0.7, ac, 92, 60);
        sl.addText(ci.competitorName, { x: rx + 0.15, y: cy + 0.07, w: rw - 1.3, h: 0.25, fontSize: 10, bold: true, color: C.g300, fontFace: 'Segoe UI' });
        sl.addText(ci.featureGap, { x: rx + 0.15, y: cy + 0.33, w: rw - 1.3, h: 0.28, fontSize: 8.5, color: C.g500, fontFace: 'Segoe UI', wrap: true });
        this.card(sl, rx + rw - 0.9, cy + 0.15, 0.78, 0.38, ac, 78);
        sl.addText(ci.impactScore, { x: rx + rw - 0.9, y: cy + 0.15, w: 0.78, h: 0.38, fontSize: 9.5, bold: true, color: C.red, align: 'center', valign: 'middle', fontFace: 'Segoe UI' });
      });
    }
  }

  // ── Slide 03 — Proposed Solution ────────────────────────────────────────

  private s03_solution(pptx: PptxGenJS, sol: PrioritizedItem, idx: number): void {
    const sl = pptx.addSlide();
    sl.background = { color: SLIDE_BG[idx] };
    const ac = ACCENT[idx]; // blue
    this.chrome(sl, idx, 'Solution', 'Proposed Solution',
      'What we\'re building — at the conceptual level, before and after');

    const colW = CW / 2 - 0.1;

    // Before box
    this.card(sl, LM, BODY_TOP, colW, 2.1, C.red, 91, 60);
    sl.addText('BEFORE — TODAY', { x: LM + 0.15, y: BODY_TOP + 0.1, w: colW - 0.3, h: 0.22, fontSize: 8, bold: true, color: C.red, fontFace: 'Segoe UI' });
    const befores = [
      `Manual, fragmented ${sol.name.toLowerCase()} workflows`,
      'Siloed data requiring cross-system reconciliation',
      'Slow reporting with limited real-time insight',
      'Reactive decisions, higher error rate',
      'Staff consumed by low-value tasks',
    ];
    befores.forEach((b, i) => {
      sl.addText(`✕  ${b}`, { x: LM + 0.15, y: BODY_TOP + 0.38 + i * 0.3, w: colW - 0.3, h: 0.28, fontSize: 9, color: C.g400, fontFace: 'Segoe UI' });
    });

    // After box
    const rx = LM + colW + 0.2;
    this.card(sl, rx, BODY_TOP, colW, 2.1, ac, 91, 60);
    sl.addText('AFTER — WITH THIS SOLUTION', { x: rx + 0.15, y: BODY_TOP + 0.1, w: colW - 0.3, h: 0.22, fontSize: 8, bold: true, color: ac, fontFace: 'Segoe UI' });
    const afters = [
      `Automated, end-to-end ${sol.name.toLowerCase()} workflows`,
      'Unified data platform — single source of truth',
      'Real-time dashboards and proactive alerts',
      'Data-driven decisions with AI-assisted insights',
      'Staff focused on high-value strategic work',
    ];
    afters.forEach((a, i) => {
      sl.addText(`✓  ${a}`, { x: rx + 0.15, y: BODY_TOP + 0.38 + i * 0.3, w: colW - 0.3, h: 0.28, fontSize: 9, color: C.g300, fontFace: 'Segoe UI' });
    });

    // Capabilities (2×3 grid)
    sl.addText('WHAT THIS SOLUTION DELIVERS', { x: LM, y: BODY_TOP + 2.25, w: CW, h: 0.2, fontSize: 7, bold: true, color: ac, fontFace: 'Segoe UI' });
    const caps = [
      { l: 'Workflow Automation', d: `Eliminates manual steps in ${sol.name.toLowerCase()}, reducing processing time by 60–80%.` },
      { l: 'Unified Intelligence', d: 'Centralised data with AI-powered insights surfaced in real time.' },
      { l: 'Seamless Integration', d: 'Connects to existing systems via standard APIs — no infrastructure replacement.' },
      { l: 'Audit & Compliance', d: 'Full activity logging, RBAC, and configurable retention policies.' },
      { l: 'Scalable Architecture', d: 'Grows from a focused pilot to org-wide adoption without rearchitecting.' },
      { l: 'Rapid Time to Value', d: 'Phase 1 go-live delivers measurable outcomes within 8 weeks.' },
    ];
    const capW = (CW - 0.2) / 3;
    caps.forEach((cap, i) => {
      const col = i % 3;
      const row = Math.floor(i / 3);
      const cx = LM + col * (capW + 0.1);
      const cy = BODY_TOP + 2.52 + row * 1.1;
      this.card(sl, cx, cy, capW, 0.98, ac, 91, 62);
      sl.addText(cap.l, { x: cx + 0.15, y: cy + 0.1, w: capW - 0.3, h: 0.25, fontSize: 9.5, bold: true, color: C.white, fontFace: 'Segoe UI' });
      sl.addText(cap.d, { x: cx + 0.15, y: cy + 0.36, w: capW - 0.3, h: 0.52, fontSize: 8, color: C.g400, fontFace: 'Segoe UI', wrap: true });
    });
  }

  // ── Slide 04 — ROI ───────────────────────────────────────────────────────

  private s04_roi(pptx: PptxGenJS, sol: PrioritizedItem, idx: number): void {
    const sl = pptx.addSlide();
    sl.background = { color: SLIDE_BG[idx] };
    const ac = ACCENT[idx]; // emerald
    this.chrome(sl, idx, 'ROI', 'Business Value & Return on Investment',
      'Why now, why this — expected outcomes and financial case');

    // 3 KPI boxes top
    const kpis = [
      { label: 'Business Impact Score', value: `${sol.value * 10}%`,  note: 'Potential value uplift' },
      { label: 'Estimated Year-1 ROI',  value: `~${this.roiPct(sol)}%`, note: 'Value/effort projection' },
      { label: 'Break-even Timeline',   value: this.breakeven(sol.difficulty), note: 'Post go-live' },
    ];
    const kw = (CW - 0.2) / 3;
    kpis.forEach((k, i) => {
      const kx = LM + i * (kw + 0.1);
      this.card(sl, kx, BODY_TOP, kw, 1.25, ac, 88, 60);
      sl.addText(k.label.toUpperCase(), { x: kx + 0.15, y: BODY_TOP + 0.1, w: kw - 0.3, h: 0.2, fontSize: 7, bold: true, color: C.g500, fontFace: 'Segoe UI' });
      sl.addText(k.value, { x: kx + 0.15, y: BODY_TOP + 0.3, w: kw - 0.3, h: 0.55, fontSize: 28, bold: true, color: ac, fontFace: 'Segoe UI' });
      sl.addText(k.note, { x: kx + 0.15, y: BODY_TOP + 0.9, w: kw - 0.3, h: 0.22, fontSize: 8.5, color: C.g500, fontFace: 'Segoe UI' });
    });

    // Left: outcomes
    const lw = CW * 0.5 - 0.15;
    sl.addText('EXPECTED OUTCOMES', { x: LM, y: BODY_TOP + 1.4, w: lw, h: 0.2, fontSize: 7, bold: true, color: ac, fontFace: 'Segoe UI' });
    const outcomes = [
      { cat: 'Time Savings', d: `Est. 40–70% reduction in manual processing hours per week.` },
      { cat: 'Cost Reduction', d: `Savings proportionate to ${sol.value}/10 value score.` },
      { cat: 'Risk Mitigation', d: 'Reduced error rate, improved audit trail, stronger compliance.' },
      { cat: 'Competitive Advantage', d: `Urgency ${sol.urgency}/10 — acting now captures first-mover positioning.` },
    ];
    outcomes.forEach((o, i) => {
      const oy = BODY_TOP + 1.66 + i * 1.0;
      this.card(sl, LM, oy, lw, 0.87, ac, 91, 62);
      sl.addShape('ellipse', { x: LM + 0.12, y: oy + 0.3, w: 0.15, h: 0.15, fill: { color: ac }, line: { color: ac, pt: 0 } });
      sl.addText(o.cat, { x: LM + 0.35, y: oy + 0.08, w: lw - 0.5, h: 0.25, fontSize: 10, bold: true, color: C.g300, fontFace: 'Segoe UI' });
      sl.addText(o.d, { x: LM + 0.35, y: oy + 0.33, w: lw - 0.5, h: 0.45, fontSize: 8.5, color: C.g500, fontFace: 'Segoe UI', wrap: true });
    });

    // Right: urgency + ratio
    const rx = LM + lw + 0.3;
    const rw = CW - lw - 0.3;
    sl.addText('WHY PRIORITISE NOW', { x: rx, y: BODY_TOP + 1.4, w: rw, h: 0.2, fontSize: 7, bold: true, color: ac, fontFace: 'Segoe UI' });

    this.card(sl, rx, BODY_TOP + 1.66, rw, 2.0, ac, 91, 62);
    sl.addText('URGENCY', { x: rx + 0.15, y: BODY_TOP + 1.76, w: rw - 1.5, h: 0.2, fontSize: 7, bold: true, color: C.g500, fontFace: 'Segoe UI' });
    sl.addText(`${sol.urgency}/10`, { x: rx + rw - 1.3, y: BODY_TOP + 1.7, w: 1.2, h: 0.34, fontSize: 22, bold: true, color: C.white, fontFace: 'Segoe UI', align: 'right' });
    this.segBar(sl, rx + 0.15, BODY_TOP + 2.1, rw - 0.3, sol.urgency, C.red);
    sl.addText(this.urgencyFull(sol.urgency), { x: rx + 0.15, y: BODY_TOP + 2.28, w: rw - 0.3, h: 1.2, fontSize: 9, color: C.g400, fontFace: 'Segoe UI', wrap: true });

    this.card(sl, rx, BODY_TOP + 3.78, rw, 0.72, ac, 91, 62);
    sl.addText(`Value/complexity ratio  ${(sol.value / sol.difficulty).toFixed(1)}× — well-balanced investment`, {
      x: rx + 0.2, y: BODY_TOP + 3.95, w: rw - 0.4, h: 0.4,
      fontSize: 9.5, color: C.g300, fontFace: 'Segoe UI',
    });
  }

  // ── Slide 05 — Scope & Fit ───────────────────────────────────────────────

  private s05_scope(pptx: PptxGenJS, sol: PrioritizedItem, research: ResearchResponse | null, idx: number): void {
    const sl = pptx.addSlide();
    sl.background = { color: SLIDE_BG[idx] };
    const ac = ACCENT[idx]; // violet
    this.chrome(sl, idx, 'Scope & Fit', 'Use Case Scope & Organisational Fit',
      'Who benefits, which workflows, how broadly this applies');

    const lw = CW * 0.52 - 0.1;
    const rx = LM + lw + 0.2;
    const rw = CW - lw - 0.2;

    // Left: teams
    sl.addText('WHO BENEFITS & HOW', { x: LM, y: BODY_TOP, w: lw, h: 0.2, fontSize: 7, bold: true, color: ac, fontFace: 'Segoe UI' });
    const teams = [
      { t: 'Operations & Delivery', b: 'Streamlined workflows reduce cycle times and bottlenecks significantly.' },
      { t: 'Data & Analytics', b: 'Unified data access enables accurate, timely reporting and insight.' },
      { t: 'Technology & Engineering', b: 'API-first architecture simplifies integration with the existing stack.' },
      { t: 'Finance & Risk', b: 'Audit trail and controls strengthen compliance and reduce cost.' },
      { t: 'Executive Leadership', b: 'Real-time operational visibility supports faster strategic decisions.' },
    ];
    teams.forEach((tm, i) => {
      const ty = BODY_TOP + 0.28 + i * 0.97;
      this.card(sl, LM, ty, lw, 0.84, ac, 91, 62);
      sl.addShape('roundRect', { x: LM + 0.1, y: ty + 0.3, w: 0.12, h: 0.12, fill: { color: ac }, line: { color: ac, pt: 0 } });
      sl.addText(tm.t, { x: LM + 0.3, y: ty + 0.08, w: lw - 0.45, h: 0.25, fontSize: 10, bold: true, color: C.g300, fontFace: 'Segoe UI' });
      sl.addText(tm.b, { x: LM + 0.3, y: ty + 0.34, w: lw - 0.45, h: 0.42, fontSize: 8.5, color: C.g500, fontFace: 'Segoe UI', wrap: true });
    });

    // Right: applicability
    sl.addText('APPLICABILITY & REACH', { x: rx, y: BODY_TOP, w: rw, h: 0.2, fontSize: 7, bold: true, color: ac, fontFace: 'Segoe UI' });
    if (research?.domain) {
      this.card(sl, rx, BODY_TOP + 0.28, rw, 0.58, ac, 88, 58);
      sl.addText('DOMAIN', { x: rx + 0.15, y: BODY_TOP + 0.35, w: rw - 0.3, h: 0.18, fontSize: 7, bold: true, color: C.g500, fontFace: 'Segoe UI' });
      sl.addText(research.domain, { x: rx + 0.15, y: BODY_TOP + 0.53, w: rw - 0.3, h: 0.26, fontSize: 14, bold: true, color: C.g300, fontFace: 'Segoe UI' });
    }

    const wfs = [
      `End-to-end ${sol.name.toLowerCase()} process automation`,
      'Cross-functional data sharing and reconciliation',
      'Audit, reporting and compliance workflows',
      'Exception handling and escalation management',
    ];
    const wfTop = BODY_TOP + (research?.domain ? 1.0 : 0.28);
    wfs.forEach((wf, i) => {
      const wy = wfTop + i * 0.75;
      this.card(sl, rx, wy, rw, 0.62, ac, 92, 62);
      sl.addShape('roundRect', { x: rx + 0.12, y: wy + 0.23, w: 0.12, h: 0.12, fill: { color: ac }, line: { color: ac, pt: 0 } });
      sl.addText(wf, { x: rx + 0.32, y: wy + 0.12, w: rw - 0.45, h: 0.38, fontSize: 9.5, color: C.g300, fontFace: 'Segoe UI', wrap: true });
    });

    // Adoption breadth
    this.card(sl, rx, wfTop + 4 * 0.75, rw, 0.66, ac, 88, 58);
    sl.addText('ADOPTION BREADTH', { x: rx + 0.15, y: wfTop + 4 * 0.75 + 0.08, w: rw - 0.3, h: 0.2, fontSize: 7, bold: true, color: C.g500, fontFace: 'Segoe UI' });
    const breadth = sol.value + sol.urgency >= 16
      ? 'Organisation-wide applicability — broad adoption from the outset.'
      : sol.value + sol.urgency >= 12
        ? 'Multi-team rollout — start with 2–3 priority groups, expand in Phase 3.'
        : 'Targeted adoption — pilot with a focused team before scaling.';
    sl.addText(breadth, { x: rx + 0.15, y: wfTop + 4 * 0.75 + 0.3, w: rw - 0.3, h: 0.28, fontSize: 9, color: C.g400, fontFace: 'Segoe UI', wrap: true });
  }

  // ── Slide 06 — Implementation ────────────────────────────────────────────

  private s06_implementation(pptx: PptxGenJS, sol: PrioritizedItem, idx: number): void {
    const sl = pptx.addSlide();
    sl.background = { color: SLIDE_BG[idx] };
    const ac = ACCENT[idx]; // teal
    this.chrome(sl, idx, 'Rollout', 'Implementation Approach',
      'Pilot → Validate → Scale · High-level phases and key dependencies');

    const phases = [
      { l: 'Phase 1 — Pilot', t: 'Weeks 1–8', c: C.teal,   steps: ['Define scope and success criteria', 'Select pilot team and use cases', 'Configure core system components'] },
      { l: 'Phase 2 — Validate', t: 'Weeks 9–16', c: C.blue,  steps: ['Measure pilot outcomes against KPIs', 'Gather stakeholder feedback and refine', 'Validate integrations and data quality'] },
      { l: 'Phase 3 — Scale', t: 'Weeks 17–24+', c: C.violet, steps: ['Full organisational rollout', 'Change management and training', 'Continuous improvement programme'] },
    ];
    const pw = (CW - 0.2) / 3;
    phases.forEach((ph, i) => {
      const px = LM + i * (pw + 0.1);
      this.card(sl, px, BODY_TOP, pw, 2.15, ph.c, 88, 60);
      sl.addText(ph.l.toUpperCase(), { x: px + 0.15, y: BODY_TOP + 0.1, w: pw - 0.3, h: 0.22, fontSize: 8, bold: true, color: ph.c, fontFace: 'Segoe UI' });
      sl.addText(ph.t, { x: px + 0.15, y: BODY_TOP + 0.32, w: pw - 0.3, h: 0.2, fontSize: 8.5, color: C.g500, fontFace: 'Segoe UI' });
      ph.steps.forEach((s, j) => {
        sl.addShape('roundRect', { x: px + 0.15, y: BODY_TOP + 0.65 + j * 0.44 + 0.12, w: 0.08, h: 0.08, fill: { color: ph.c }, line: { color: ph.c, pt: 0 } });
        sl.addText(s, { x: px + 0.3, y: BODY_TOP + 0.65 + j * 0.44, w: pw - 0.48, h: 0.38, fontSize: 8.5, color: C.g400, fontFace: 'Segoe UI', wrap: true });
      });
    });

    // Dependencies + timeline
    const lw = CW * 0.52 - 0.1;
    const rx = LM + lw + 0.2;
    const rw = CW - lw - 0.2;
    sl.addText('KEY DEPENDENCIES', { x: LM, y: BODY_TOP + 2.3, w: lw, h: 0.2, fontSize: 7, bold: true, color: ac, fontFace: 'Segoe UI' });
    const deps = [
      'Executive sponsor assigned and steering committee formed',
      'Access to source systems and data for integration',
      'Dedicated project manager and engineering resource',
      'Legal/compliance sign-off on data handling approach',
    ];
    deps.forEach((d, i) => {
      const dy = BODY_TOP + 2.56 + i * 0.82;
      this.card(sl, LM, dy, lw, 0.69, ac, 92, 62);
      sl.addShape('roundRect', { x: LM + 0.12, y: dy + 0.27, w: 0.12, h: 0.12, fill: { color: ac }, line: { color: ac, pt: 0 } });
      sl.addText(d, { x: LM + 0.32, y: dy + 0.12, w: lw - 0.45, h: 0.44, fontSize: 9, color: C.g300, fontFace: 'Segoe UI', wrap: true });
    });

    sl.addText('TIMELINE SUMMARY', { x: rx, y: BODY_TOP + 2.3, w: rw, h: 0.2, fontSize: 7, bold: true, color: ac, fontFace: 'Segoe UI' });
    this.card(sl, rx, BODY_TOP + 2.56, rw, 1.6, ac, 88, 60);
    sl.addText('Total Delivery', { x: rx + 0.2, y: BODY_TOP + 2.68, w: rw - 0.4, h: 0.25, fontSize: 9, color: C.g500, fontFace: 'Segoe UI' });
    sl.addText(this.timeline(sol.difficulty), { x: rx + 0.2, y: BODY_TOP + 2.92, w: rw - 0.4, h: 0.5, fontSize: 24, bold: true, color: C.white, fontFace: 'Segoe UI' });
    sl.addText('Pilot through full scale', { x: rx + 0.2, y: BODY_TOP + 3.45, w: rw - 0.4, h: 0.2, fontSize: 8.5, color: C.g500, fontFace: 'Segoe UI' });

    this.card(sl, rx, BODY_TOP + 4.28, rw, 0.56, ac, 92, 62);
    sl.addText(`${this.effortShort(sol.difficulty)} complexity  ·  Difficulty ${sol.difficulty}/10`, { x: rx + 0.2, y: BODY_TOP + 4.4, w: rw - 0.4, h: 0.3, fontSize: 9.5, color: C.g300, fontFace: 'Segoe UI' });
  }

  // ── Slide 07 — Risks ─────────────────────────────────────────────────────

  private s07_risks(pptx: PptxGenJS, sol: PrioritizedItem, idx: number): void {
    const sl = pptx.addSlide();
    sl.background = { color: SLIDE_BG[idx] };
    const ac = ACCENT[idx]; // amber
    this.chrome(sl, idx, 'Risks', 'Risks & Mitigations',
      'What could go wrong — and how we\'ve planned for it');

    const risks = [
      { risk: 'Stakeholder adoption resistance', impact: 'Medium', impactC: C.amber, detail: 'Slow adoption delays value realisation and reduces measured ROI in Year 1.', mit: 'Executive sponsorship, early champion network, role-specific training, and phased rollout with clear quick wins in Phase 1.' },
      { risk: `Implementation complexity (${sol.difficulty}/10 difficulty)`, impact: sol.difficulty >= 7 ? 'High' : 'Medium', impactC: sol.difficulty >= 7 ? C.red : C.amber, detail: 'Technical scope creep or integration challenges could extend delivery timeline.', mit: 'Agile delivery with 2-week sprints, bi-weekly steering reviews, and a clearly scoped Phase 1 MVP to de-risk early.' },
      { risk: 'Data quality and integration gaps', impact: 'Medium', impactC: C.amber, detail: 'Poor source data degrades solution quality and undermines stakeholder trust.', mit: 'Mandatory data audit in Week 1, dedicated data engineering resource, and data governance framework before go-live.' },
    ];
    const riskH = 1.4;
    risks.forEach((r, i) => {
      const ry = BODY_TOP + i * (riskH + 0.1);
      const splitX = LM + CW * 0.38;

      // Left: risk
      this.card(sl, LM, ry, CW * 0.38 - 0.05, riskH, ac, 90, 60);
      sl.addText(r.risk, { x: LM + 0.15, y: ry + 0.1, w: CW * 0.38 - 0.4, h: 0.35, fontSize: 10, bold: true, color: C.g300, fontFace: 'Segoe UI', wrap: true });
      this.card(sl, splitX - 0.85 - 0.05, ry + 0.5, 0.8, 0.3, r.impactC, 78);
      sl.addText(r.impact, { x: splitX - 0.85 - 0.05, y: ry + 0.5, w: 0.8, h: 0.3, fontSize: 8, bold: true, color: r.impactC, align: 'center', valign: 'middle', fontFace: 'Segoe UI' });
      sl.addText(r.detail, { x: LM + 0.15, y: ry + 0.88, w: CW * 0.38 - 0.4, h: 0.44, fontSize: 8, color: C.g500, fontFace: 'Segoe UI', wrap: true });

      // Right: mitigation
      this.card(sl, splitX + 0.05, ry, W - splitX - RM - 0.1, riskH, ac, 94, 60);
      sl.addText('MITIGATION', { x: splitX + 0.2, y: ry + 0.1, w: W - splitX - RM - 0.35, h: 0.2, fontSize: 7, bold: true, color: C.g500, fontFace: 'Segoe UI' });
      sl.addText(r.mit, { x: splitX + 0.2, y: ry + 0.33, w: W - splitX - RM - 0.35, h: 0.98, fontSize: 9.5, color: C.g300, fontFace: 'Segoe UI', wrap: true });
    });

    // Footer note
    this.card(sl, LM, BODY_TOP + 3 * (riskH + 0.1), CW, 0.5, ac, 91, 62);
    sl.addText('Risk profile is manageable with standard programme governance and an experienced delivery team.', {
      x: LM + 0.2, y: BODY_TOP + 3 * (riskH + 0.1) + 0.12, w: CW - 0.4, h: 0.28,
      fontSize: 9.5, color: C.g400, fontFace: 'Segoe UI',
    });
  }

  // ── Slide 08 — Investment ────────────────────────────────────────────────

  private s08_investment(pptx: PptxGenJS, sol: PrioritizedItem, idx: number): void {
    const sl = pptx.addSlide();
    sl.background = { color: SLIDE_BG[idx] };
    const ac = ACCENT[idx]; // sky
    this.chrome(sl, idx, 'Investment', 'Resource & Investment Ask',
      'What we need approved — budget, headcount, stakeholder time');

    const lw = CW * 0.48 - 0.1;
    const rx = LM + lw + 0.2;
    const rw = CW - lw - 0.2;

    // Left: 3 investment cards
    sl.addText('WHAT WE NEED APPROVED', { x: LM, y: BODY_TOP, w: lw, h: 0.2, fontSize: 7, bold: true, color: ac, fontFace: 'Segoe UI' });
    const items = [
      { l: 'Budget Range', v: this.budgetRange(sol.difficulty), n: 'Software, delivery, change management' },
      { l: 'Delivery Timeline', v: this.timeline(sol.difficulty), n: 'Pilot through full-scale rollout' },
      { l: 'Team Commitment', v: sol.difficulty <= 3 ? '2–3 FTEs' : sol.difficulty <= 6 ? '4–6 FTEs' : '6–10 FTEs', n: 'Delivery team + client-side sponsor time' },
    ];
    items.forEach((item, i) => {
      const iy = BODY_TOP + 0.28 + i * 1.65;
      this.card(sl, LM, iy, lw, 1.52, ac, 88, 60);
      sl.addText(item.l.toUpperCase(), { x: LM + 0.2, y: iy + 0.12, w: lw - 0.4, h: 0.2, fontSize: 7, bold: true, color: C.g500, fontFace: 'Segoe UI' });
      sl.addText(item.v, { x: LM + 0.2, y: iy + 0.35, w: lw - 0.4, h: 0.65, fontSize: 22, bold: true, color: C.white, fontFace: 'Segoe UI' });
      sl.addText(item.n, { x: LM + 0.2, y: iy + 1.04, w: lw - 0.4, h: 0.38, fontSize: 8.5, color: C.g500, fontFace: 'Segoe UI', wrap: true });
    });

    // Right: justification
    sl.addText('INVESTMENT JUSTIFICATION', { x: rx, y: BODY_TOP, w: rw, h: 0.2, fontSize: 7, bold: true, color: ac, fontFace: 'Segoe UI' });
    this.card(sl, rx, BODY_TOP + 0.28, rw, 3.22, ac, 88, 60);
    sl.addText('WHY THIS IS WORTH IT', { x: rx + 0.2, y: BODY_TOP + 0.38, w: rw - 0.4, h: 0.2, fontSize: 7, bold: true, color: C.g500, fontFace: 'Segoe UI' });

    const rows = [
      { l: 'Investment', v: this.budgetRange(sol.difficulty), c: C.g300 },
      { l: 'Estimated ROI (Yr 1)', v: `~${this.roiPct(sol)}%`, c: C.emerald },
      { l: 'Break-even', v: this.breakeven(sol.difficulty), c: C.g300 },
      { l: 'Business Value Score', v: `${sol.value}/10`, c: C.g300 },
      { l: 'Market Urgency Score', v: `${sol.urgency}/10`, c: C.g300 },
    ];
    rows.forEach((row, i) => {
      const rowy = BODY_TOP + 0.68 + i * 0.46;
      sl.addText(row.l, { x: rx + 0.2, y: rowy, w: rw * 0.58, h: 0.38, fontSize: 9.5, color: C.g500, fontFace: 'Segoe UI' });
      sl.addText(row.v, { x: rx + 0.2 + rw * 0.58, y: rowy, w: rw * 0.35, h: 0.38, fontSize: 9.5, bold: true, color: row.c, fontFace: 'Segoe UI', align: 'right' });
      if (i < rows.length - 1) {
        sl.addShape('rect', { x: rx + 0.2, y: rowy + 0.38, w: rw - 0.4, h: 0.01, fill: { color: C.g800 }, line: { color: C.g800, pt: 0 } });
      }
    });

    this.card(sl, rx, BODY_TOP + 3.62, rw, 0.72, ac, 92, 62);
    sl.addText(`Investment proportionate to ${sol.value * 10}% potential impact at ${this.effortShort(sol.difficulty)} complexity.`, {
      x: rx + 0.2, y: BODY_TOP + 3.78, w: rw - 0.4, h: 0.42,
      fontSize: 9.5, color: C.g400, fontFace: 'Segoe UI', wrap: true,
    });
  }

  // ── Slide 09 — Next Steps ────────────────────────────────────────────────

  private s09_nextSteps(pptx: PptxGenJS, sol: PrioritizedItem, idx: number): void {
    const sl = pptx.addSlide();
    sl.background = { color: SLIDE_BG[idx] };
    const ac = ACCENT[idx]; // green
    this.chrome(sl, idx, 'Next Steps', 'Recommended Next Steps',
      'Concrete actions post-approval — owners, timeframes, momentum');

    const lw = CW * 0.52 - 0.1;
    const rx = LM + lw + 0.2;
    const rw = CW - lw - 0.2;

    // Left: 4 action cards
    sl.addText('IMMEDIATE ACTIONS POST-APPROVAL', { x: LM, y: BODY_TOP, w: lw, h: 0.2, fontSize: 7, bold: true, color: ac, fontFace: 'Segoe UI' });
    const steps = [
      { a: 'Approve proposal and designate executive sponsor', o: 'Leadership', t: 'This week' },
      { a: 'Convene steering committee and confirm stakeholder group', o: 'Sponsor / PMO', t: 'Week 1' },
      { a: 'Commission technical discovery and scoping workshop', o: 'Engineering Lead', t: 'Weeks 1–2' },
      { a: 'Confirm Phase 1 budget and resource allocation', o: 'Finance / Leadership', t: 'Week 2' },
    ];
    steps.forEach((step, i) => {
      const sy = BODY_TOP + 0.28 + i * 1.18;
      this.card(sl, LM, sy, lw, 1.04, ac, 91, 62);

      // Number circle
      sl.addShape('ellipse', { x: LM + 0.12, y: sy + 0.14, w: 0.35, h: 0.35, fill: { color: ac, transparency: 72 }, line: { color: ac, transparency: 50, pt: 0.8 } });
      sl.addText(`${i + 1}`, { x: LM + 0.12, y: sy + 0.14, w: 0.35, h: 0.35, fontSize: 8.5, bold: true, color: C.white, align: 'center', valign: 'middle', fontFace: 'Segoe UI' });

      sl.addText(step.a, { x: LM + 0.58, y: sy + 0.08, w: lw - 0.72, h: 0.42, fontSize: 10, bold: true, color: C.g300, fontFace: 'Segoe UI', wrap: true });
      sl.addText(step.o, { x: LM + 0.58, y: sy + 0.54, w: lw * 0.55, h: 0.22, fontSize: 8.5, color: C.g500, fontFace: 'Segoe UI' });
      sl.addText(step.t, { x: LM + lw - 1.3, y: sy + 0.54, w: 1.2, h: 0.22, fontSize: 8.5, bold: true, color: ac, fontFace: 'Segoe UI', align: 'right' });
    });

    // Right: CTA box
    sl.addText('DECISION REQUIRED', { x: rx, y: BODY_TOP, w: rw, h: 0.2, fontSize: 7, bold: true, color: ac, fontFace: 'Segoe UI' });
    this.card(sl, rx, BODY_TOP + 0.28, rw, 5.12, ac, 86, 55);

    sl.addText('APPROVE TO PROCEED', { x: rx + 0.2, y: BODY_TOP + 0.38, w: rw - 0.4, h: 0.22, fontSize: 7.5, bold: true, color: C.g500, fontFace: 'Segoe UI' });
    sl.addText(sol.name, { x: rx + 0.2, y: BODY_TOP + 0.64, w: rw - 0.4, h: 0.7, fontSize: 15, bold: true, color: C.white, fontFace: 'Segoe UI', wrap: true });

    const tableRows: [string, string, string][] = [
      ['Investment', this.budgetRange(sol.difficulty), C.g300],
      ['Est. ROI (Yr 1)', `~${this.roiPct(sol)}%`, C.emerald],
      ['Break-even', this.breakeven(sol.difficulty), C.g300],
      ['Business Value', `${sol.value}/10`, C.g300],
    ];
    tableRows.forEach(([label, value, valueC], i) => {
      const tr = BODY_TOP + 1.5 + i * 0.48;
      sl.addText(label, { x: rx + 0.2, y: tr, w: rw * 0.55, h: 0.38, fontSize: 9.5, color: C.g500, fontFace: 'Segoe UI' });
      sl.addText(value, { x: rx + 0.2 + rw * 0.55, y: tr, w: rw * 0.38, h: 0.38, fontSize: 9.5, bold: true, color: valueC, fontFace: 'Segoe UI', align: 'right' });
      if (i < tableRows.length - 1) {
        sl.addShape('rect', { x: rx + 0.2, y: tr + 0.38, w: rw - 0.4, h: 0.012, fill: { color: C.g800 }, line: { color: C.g800, pt: 0 } });
      }
    });

    // Urgency CTA
    this.card(sl, rx + 0.15, BODY_TOP + 3.58, rw - 0.3, 0.68, ac, 78, 45);
    sl.addText(`Urgency ${sol.urgency}/10 — ${this.urgencyShort(sol.urgency)}`, {
      x: rx + 0.15, y: BODY_TOP + 3.58, w: rw - 0.3, h: 0.68,
      fontSize: 12, bold: true, color: ac, align: 'center', valign: 'middle', fontFace: 'Segoe UI',
    });

    // Meridian tag
    sl.addText('Generated by Meridian Studio  ·  AI Solution Agent & System Architect Hub', {
      x: rx + 0.15, y: BODY_TOP + 4.44, w: rw - 0.3, h: 0.28,
      fontSize: 7.5, color: C.g700, fontFace: 'Segoe UI', align: 'center',
    });
  }
}
