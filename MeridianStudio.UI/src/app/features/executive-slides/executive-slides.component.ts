import {
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { animate, style, transition, trigger } from '@angular/animations';
import { LucideAngularModule } from 'lucide-angular';
import { WorkspaceStoreService } from '../../core/services/workspace-store.service';
import { PptxExportService } from '../../core/services/pptx-export.service';

type SlideType =
  | 'executive-summary' | 'problem' | 'solution' | 'roi'
  | 'scope' | 'implementation' | 'risks' | 'investment' | 'next-steps';

interface SlideConfig {
  index: number;
  type: SlideType;
  section: string;
  label: string;
  title: string;
  subtitle: string;
  // Visual identity
  glowA: string;      // top-right glow class
  glowB: string;      // bottom-left glow class
  badgeBg: string;    // label badge bg
  badgeText: string;  // label badge text
  accentBar: string;  // title underline + progress bars
  accentBorder: string;
  accentCardBg: string;
  accentText: string;
  accentIcon: string; // large decorative icon name
}

const SLIDES: Omit<SlideConfig, 'index'>[] = [
  {
    type: 'executive-summary',
    section: '01', label: 'Summary',
    title: 'Executive Summary',
    subtitle: 'The complete proposal — for decision-makers who read only one slide',
    glowA: 'bg-indigo-500', glowB: 'bg-violet-600',
    badgeBg: 'bg-indigo-500/25 border-indigo-400/30', badgeText: 'text-indigo-300',
    accentBar: 'bg-gradient-to-r from-indigo-500 to-violet-400',
    accentBorder: 'border-indigo-500/30', accentCardBg: 'bg-indigo-500/10',
    accentText: 'text-indigo-400', accentIcon: 'sparkles',
  },
  {
    type: 'problem',
    section: '02', label: 'Problem',
    title: 'Problem & Opportunity Statement',
    subtitle: 'The gap between current state and what\'s possible — quantified',
    glowA: 'bg-red-600', glowB: 'bg-orange-500',
    badgeBg: 'bg-red-500/25 border-red-400/30', badgeText: 'text-red-300',
    accentBar: 'bg-gradient-to-r from-red-500 to-orange-400',
    accentBorder: 'border-red-500/30', accentCardBg: 'bg-red-500/10',
    accentText: 'text-red-400', accentIcon: 'alert-circle',
  },
  {
    type: 'solution',
    section: '03', label: 'Solution',
    title: 'Proposed Solution',
    subtitle: 'What we\'re building — at the conceptual level, before and after',
    glowA: 'bg-blue-600', glowB: 'bg-cyan-500',
    badgeBg: 'bg-blue-500/25 border-blue-400/30', badgeText: 'text-blue-300',
    accentBar: 'bg-gradient-to-r from-blue-500 to-cyan-400',
    accentBorder: 'border-blue-500/30', accentCardBg: 'bg-blue-500/10',
    accentText: 'text-blue-400', accentIcon: 'layers',
  },
  {
    type: 'roi',
    section: '04', label: 'ROI',
    title: 'Business Value & Return on Investment',
    subtitle: 'Why now, why this — expected outcomes and financial case',
    glowA: 'bg-emerald-500', glowB: 'bg-teal-600',
    badgeBg: 'bg-emerald-500/25 border-emerald-400/30', badgeText: 'text-emerald-300',
    accentBar: 'bg-gradient-to-r from-emerald-500 to-teal-400',
    accentBorder: 'border-emerald-500/30', accentCardBg: 'bg-emerald-500/10',
    accentText: 'text-emerald-400', accentIcon: 'trending-up',
  },
  {
    type: 'scope',
    section: '05', label: 'Scope & Fit',
    title: 'Use Case Scope & Organisational Fit',
    subtitle: 'Who benefits, which workflows, how broadly this applies',
    glowA: 'bg-violet-600', glowB: 'bg-purple-500',
    badgeBg: 'bg-violet-500/25 border-violet-400/30', badgeText: 'text-violet-300',
    accentBar: 'bg-gradient-to-r from-violet-500 to-purple-400',
    accentBorder: 'border-violet-500/30', accentCardBg: 'bg-violet-500/10',
    accentText: 'text-violet-400', accentIcon: 'target',
  },
  {
    type: 'implementation',
    section: '06', label: 'Rollout',
    title: 'Implementation Approach',
    subtitle: 'Pilot → Validate → Scale · High-level phases and key dependencies',
    glowA: 'bg-teal-500', glowB: 'bg-cyan-600',
    badgeBg: 'bg-teal-500/25 border-teal-400/30', badgeText: 'text-teal-300',
    accentBar: 'bg-gradient-to-r from-teal-500 to-cyan-400',
    accentBorder: 'border-teal-500/30', accentCardBg: 'bg-teal-500/10',
    accentText: 'text-teal-400', accentIcon: 'zap',
  },
  {
    type: 'risks',
    section: '07', label: 'Risks',
    title: 'Risks & Mitigations',
    subtitle: 'What could go wrong — and how we\'ve planned for it',
    glowA: 'bg-amber-500', glowB: 'bg-orange-600',
    badgeBg: 'bg-amber-500/25 border-amber-400/30', badgeText: 'text-amber-300',
    accentBar: 'bg-gradient-to-r from-amber-500 to-orange-400',
    accentBorder: 'border-amber-500/30', accentCardBg: 'bg-amber-500/10',
    accentText: 'text-amber-400', accentIcon: 'shield',
  },
  {
    type: 'investment',
    section: '08', label: 'Investment',
    title: 'Resource & Investment Ask',
    subtitle: 'What we need approved — budget, headcount, stakeholder time',
    glowA: 'bg-sky-500', glowB: 'bg-blue-600',
    badgeBg: 'bg-sky-500/25 border-sky-400/30', badgeText: 'text-sky-300',
    accentBar: 'bg-gradient-to-r from-sky-500 to-blue-400',
    accentBorder: 'border-sky-500/30', accentCardBg: 'bg-sky-500/10',
    accentText: 'text-sky-400', accentIcon: 'bar-chart-3',
  },
  {
    type: 'next-steps',
    section: '09', label: 'Next Steps',
    title: 'Recommended Next Steps',
    subtitle: 'Concrete actions post-approval — owners, timeframes, momentum',
    glowA: 'bg-green-500', glowB: 'bg-emerald-600',
    badgeBg: 'bg-green-500/25 border-green-400/30', badgeText: 'text-green-300',
    accentBar: 'bg-gradient-to-r from-green-500 to-emerald-400',
    accentBorder: 'border-green-500/30', accentCardBg: 'bg-green-500/10',
    accentText: 'text-green-400', accentIcon: 'arrow-right',
  },
];

@Component({
  selector: 'app-executive-slides',
  standalone: true,
  imports: [CommonModule, LucideAngularModule],
  animations: [
    trigger('slideIn', [
      transition('void => forward', [
        style({ opacity: 0, transform: 'translateX(60px) scale(.97)' }),
        animate('380ms cubic-bezier(.22,1,.36,1)', style({ opacity: 1, transform: 'none' })),
      ]),
      transition('void => backward', [
        style({ opacity: 0, transform: 'translateX(-60px) scale(.97)' }),
        animate('380ms cubic-bezier(.22,1,.36,1)', style({ opacity: 1, transform: 'none' })),
      ]),
    ]),
  ],
  template: `
    <div class="flex h-full flex-col overflow-hidden">

      <!-- Shell header -->
      <div class="flex shrink-0 items-center justify-between gap-4
                  border-b border-gray-800/60 bg-gray-950/80 px-6 py-3 backdrop-blur-sm">
        <div class="flex items-center gap-2.5">
          <div class="flex h-7 w-7 items-center justify-center rounded-lg bg-indigo-500/15">
            <lucide-icon name="monitor" [size]="14" class="text-indigo-400" />
          </div>
          <div>
            <p class="text-sm font-semibold text-white">Executive Summary Deck</p>
            @if (store.selectedSolution(); as sol) {
              <p class="text-[10px] text-gray-400 leading-tight">{{ sol.name }}</p>
            }
          </div>
        </div>
        <div class="flex items-center gap-2">
          <span class="text-[10px] tabular-nums text-gray-400">{{ currentIndex() + 1 }} / {{ slides().length }}</span>

          <!-- Download PPTX -->
          <button
            (click)="onDownloadPptx()"
            [disabled]="!store.selectedSolution() || isExporting()"
            class="flex h-8 items-center gap-1.5 rounded-lg border border-gray-700/60
                   bg-gray-800/40 px-3 text-[11px] font-medium text-gray-300
                   transition-all hover:border-indigo-500/30 hover:bg-indigo-500/8 hover:text-indigo-300
                   focus:outline-none disabled:cursor-not-allowed disabled:opacity-40"
          >
            @if (isExporting()) {
              <lucide-icon name="loader-2" [size]="13" class="animate-spin" />
              <span>Exporting…</span>
            } @else {
              <lucide-icon name="download" [size]="13" />
              <span>Download .pptx</span>
            }
          </button>

          <button (click)="toggleFullscreen()"
            class="flex h-8 w-8 items-center justify-center rounded-lg border border-gray-700/60
                   bg-gray-800/40 text-gray-500 hover:border-gray-600 hover:text-gray-300">
            @if (isFullscreen()) { <lucide-icon name="minimize-2" [size]="13" /> }
            @else { <lucide-icon name="maximize-2" [size]="13" /> }
          </button>
        </div>
      </div>

      <!-- Stage -->
      <div class="flex min-h-0 flex-1 flex-col items-center justify-center
                  gap-3 overflow-hidden bg-gray-950 px-5 py-4">

        @if (!store.selectedSolution()) {
          <div class="flex flex-col items-center justify-center gap-5 text-center">
            <div class="flex h-20 w-20 items-center justify-center rounded-2xl
                        border border-gray-800 bg-gray-900">
              <lucide-icon name="monitor" [size]="32" class="text-gray-700" />
            </div>
            <p class="text-sm font-medium text-gray-500">No use case selected</p>
            <p class="max-w-sm text-xs leading-relaxed text-gray-400">
              Research and select a solution priority — the 9-slide executive deck auto-populates.
            </p>
            <button (click)="store.setActiveWorkspace('research')"
              class="flex h-9 items-center gap-1.5 rounded-lg border border-indigo-500/30
                     bg-indigo-500/10 px-4 text-xs font-medium text-indigo-400 hover:bg-indigo-500/20">
              <lucide-icon name="chevron-right" [size]="13" />Go to Research
            </button>
          </div>

        } @else {

          <!-- 16:9 frame -->
          <div #slideFrame
            class="slide-fullscreen-target relative w-full overflow-hidden rounded-2xl
                   border border-gray-700/60 shadow-2xl shadow-black/70"
            style="aspect-ratio:16/9; max-height:calc(100vh - 200px);">

            <!-- ── Left edge nav (full-height strip, no content overlap) ── -->
            <button (click)="prevSlide()" [disabled]="currentIndex() === 0"
              aria-label="Previous slide"
              class="absolute left-0 top-0 bottom-0 z-30 w-14 flex items-center justify-start pl-2
                     bg-gradient-to-r from-black/40 to-transparent opacity-0 hover:opacity-100
                     transition-opacity duration-200 disabled:hidden focus:outline-none group">
              <div class="flex h-10 w-10 items-center justify-center rounded-full
                          border border-white/20 bg-black/50 backdrop-blur-sm
                          group-hover:border-white/40 transition-colors">
                <lucide-icon name="chevron-left" [size]="18" class="text-white/70" />
              </div>
            </button>

            <!-- ── Right edge nav ── -->
            <button (click)="nextSlide()" [disabled]="currentIndex() === slides().length - 1"
              aria-label="Next slide"
              class="absolute right-0 top-0 bottom-0 z-30 w-14 flex items-center justify-end pr-2
                     bg-gradient-to-l from-black/40 to-transparent opacity-0 hover:opacity-100
                     transition-opacity duration-200 disabled:hidden focus:outline-none group">
              <div class="flex h-10 w-10 items-center justify-center rounded-full
                          border border-white/20 bg-black/50 backdrop-blur-sm
                          group-hover:border-white/40 transition-colors">
                <lucide-icon name="chevron-right" [size]="18" class="text-white/70" />
              </div>
            </button>

            <!-- Animated slide -->
            @if (slideVisible()) {
              <div class="absolute inset-0" [@slideIn]="direction()">
                @if (currentSlide(); as slide) {

                  <!-- Slide background + glow elements -->
                  <div class="relative flex h-full w-full flex-col overflow-hidden bg-gray-950">

                    <!-- Glow top-right -->
                    <div class="pointer-events-none absolute -right-20 -top-20 h-96 w-96 rounded-full opacity-35 blur-3xl"
                         [class]="slide.glowA"></div>
                    <!-- Glow bottom-left -->
                    <div class="pointer-events-none absolute -bottom-20 -left-20 h-72 w-72 rounded-full opacity-25 blur-3xl"
                         [class]="slide.glowB"></div>
                    <!-- Subtle mid glow for depth -->
                    <div class="pointer-events-none absolute right-1/3 top-1/2 h-48 w-48 -translate-y-1/2 rounded-full opacity-10 blur-3xl"
                         [class]="slide.glowA"></div>

                    <!-- ── Slide header ─────────────────────────── -->
                    <div class="relative z-10 shrink-0 px-10 pt-6 pb-3">
                      <!-- Meta row -->
                      <div class="mb-3 flex items-center gap-3">
                        <span class="rounded-md border px-2.5 py-0.5 text-[10px] font-black
                                     uppercase tracking-[0.18em]"
                              [class]="slide.badgeBg + ' ' + slide.badgeText">
                          {{ slide.label }}
                        </span>
                        <span class="text-[10px] font-semibold uppercase tracking-widest text-white/25">
                          {{ slide.section }} / 09
                        </span>
                        <span class="h-px flex-1 max-w-[60px]" [class]="slide.accentBar"></span>
                        <span class="text-[9px] font-bold uppercase tracking-[0.2em] text-white/18">
                          Meridian Studio
                        </span>
                      </div>
                      <!-- Title -->
                      <h2 class="text-[1.65rem] font-black leading-tight tracking-tight text-white xl:text-[2rem]">
                        {{ slide.title }}
                      </h2>
                      <p class="mt-1 text-[11px] font-medium tracking-wide text-white/38">
                        {{ slide.subtitle }}
                      </p>
                      <!-- Colored underline -->
                      <div class="mt-3 h-[3px] w-16 rounded-full" [class]="slide.accentBar"></div>
                    </div>

                    <!-- ── Slide body ────────────────────────────── -->
                    <div class="relative z-10 min-h-0 flex-1 overflow-hidden px-10 pb-5 pt-2">

                      @if (store.selectedSolution(); as sol) {
                        @switch (slide.type) {

                          <!-- ═══════════════════════════════════════
                               01  EXECUTIVE SUMMARY
                          ════════════════════════════════════════ -->
                          @case ('executive-summary') {
                            <div class="grid h-full grid-cols-5 gap-5">

                              <!-- Bullets (left 3/5) -->
                              <div class="col-span-3 flex flex-col gap-2">
                                <p class="mb-1 text-[10px] font-black uppercase tracking-[0.22em]"
                                   [class]="slide.accentText">The Proposal at a Glance</p>
                                @for (b of execBullets(); track $index) {
                                  <div class="flex items-start gap-3 rounded-xl border px-4 py-3"
                                       [class]="slide.accentBorder + ' ' + slide.accentCardBg">
                                    <div class="mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center
                                                rounded-lg border text-[10px] font-black"
                                         [class]="slide.accentBorder + ' ' + slide.accentText">
                                      {{ $index + 1 }}
                                    </div>
                                    <div class="min-w-0 flex-1">
                                      <p class="text-sm font-bold text-white leading-snug">{{ b.label }}</p>
                                      <p class="mt-0.5 text-xs leading-relaxed text-white/50">{{ b.detail }}</p>
                                    </div>
                                  </div>
                                }
                              </div>

                              <!-- Metrics (right 2/5) -->
                              <div class="col-span-2 flex flex-col gap-2.5">
                                <p class="mb-1 text-[10px] font-black uppercase tracking-[0.22em]"
                                   [class]="slide.accentText">Decision Metrics</p>

                                <!-- Value -->
                                <div class="flex flex-1 flex-col justify-between rounded-2xl border p-4"
                                     [class]="slide.accentBorder + ' ' + slide.accentCardBg">
                                  <p class="text-[10px] font-bold uppercase tracking-wider text-white/35">
                                    Business Value
                                  </p>
                                  <p class="text-5xl font-black tabular-nums text-white">
                                    {{ sol.value }}<span class="text-xl font-normal text-white/30">/10</span>
                                  </p>
                                  <!-- 10-segment bar -->
                                  <div class="flex gap-0.5 mt-1">
                                    @for (seg of ten; track seg) {
                                      <div class="h-2.5 flex-1 rounded-sm"
                                           [class]="seg <= sol.value ? slide.accentBar : 'bg-white/10'"></div>
                                    }
                                  </div>
                                </div>

                                <!-- Urgency -->
                                <div class="flex flex-1 flex-col justify-between rounded-2xl border p-4"
                                     [class]="slide.accentBorder + ' ' + slide.accentCardBg">
                                  <p class="text-[10px] font-bold uppercase tracking-wider text-white/35">
                                    Market Urgency
                                  </p>
                                  <p class="text-5xl font-black tabular-nums text-white">
                                    {{ sol.urgency }}<span class="text-xl font-normal text-white/30">/10</span>
                                  </p>
                                  <div class="flex gap-0.5 mt-1">
                                    @for (seg of ten; track seg) {
                                      <div class="h-2.5 flex-1 rounded-sm"
                                           [class]="seg <= sol.urgency ? 'bg-gradient-to-r from-red-500 to-orange-400' : 'bg-white/10'"></div>
                                    }
                                  </div>
                                </div>

                                <!-- ROI + Effort row -->
                                <div class="grid grid-cols-2 gap-2">
                                  <div class="rounded-xl border p-3 text-center"
                                       [class]="slide.accentBorder + ' bg-white/5'">
                                    <p class="text-[9px] font-bold uppercase tracking-wider text-white/30">Est. ROI</p>
                                    <p class="text-xl font-black tabular-nums" [class]="slide.accentText">
                                      ~{{ roiPercent(sol) }}%
                                    </p>
                                  </div>
                                  <div class="rounded-xl border p-3 text-center"
                                       [class]="slide.accentBorder + ' bg-white/5'">
                                    <p class="text-[9px] font-bold uppercase tracking-wider text-white/30">Effort</p>
                                    <p class="text-base font-black text-white/80">{{ effortShort(sol.difficulty) }}</p>
                                  </div>
                                </div>
                              </div>
                            </div>
                          }

                          <!-- ═══════════════════════════════════════
                               02  PROBLEM
                          ════════════════════════════════════════ -->
                          @case ('problem') {
                            <div class="grid h-full grid-cols-2 gap-5">
                              <!-- Pain points -->
                              <div class="flex flex-col gap-2">
                                <p class="text-[10px] font-black uppercase tracking-[0.2em]" [class]="slide.accentText">
                                  Current State — What's Broken
                                </p>
                                @for (p of painPoints(); track $index) {
                                  <div class="flex items-start gap-3 rounded-xl border px-4 py-2.5"
                                       [class]="slide.accentBorder + ' ' + slide.accentCardBg">
                                    <lucide-icon name="x-circle" [size]="14" class="mt-0.5 shrink-0 text-red-400" />
                                    <p class="text-sm leading-snug text-white/75">{{ p }}</p>
                                  </div>
                                }
                                <div class="mt-1 rounded-xl border border-orange-500/25 bg-orange-500/10 px-4 py-3">
                                  <p class="text-xs font-black uppercase tracking-wider text-orange-400">Cost of Inaction</p>
                                  <p class="mt-1 text-sm text-white/65">
                                    Urgency <span class="font-bold text-orange-300">{{ sol.urgency }}/10</span>
                                    — {{ urgencyShort(sol.urgency) }}
                                  </p>
                                </div>
                              </div>

                              <!-- Opportunity -->
                              <div class="flex flex-col gap-2.5">
                                <p class="text-[10px] font-black uppercase tracking-[0.2em]" [class]="slide.accentText">
                                  The Opportunity Gap
                                </p>
                                <div class="flex-1 rounded-xl border p-4" [class]="slide.accentBorder + ' bg-white/5'">
                                  <p class="text-xs font-bold uppercase tracking-wider text-white/30 mb-2">
                                    Desired Future State
                                  </p>
                                  <p class="text-sm leading-relaxed text-white/70">
                                    {{ sol.realLifeValue || sol.description }}
                                  </p>
                                  @if (sol.rationale) {
                                    <p class="mt-3 border-t border-white/8 pt-3 text-xs italic text-white/40">
                                      "{{ sol.rationale }}"
                                    </p>
                                  }
                                </div>
                                @if (store.currentResearchData()?.competitorInsights?.length) {
                                  <p class="text-[10px] font-black uppercase tracking-[0.2em]" [class]="slide.accentText">
                                    Peer Adoption Pressure
                                  </p>
                                  @for (ci of store.currentResearchData()!.competitorInsights.slice(0,2); track $index) {
                                    <div class="flex items-center gap-3 rounded-xl border px-4 py-2.5"
                                         [class]="slide.accentBorder + ' bg-white/5'">
                                      <div class="min-w-0 flex-1">
                                        <p class="text-sm font-bold text-white/80">{{ ci.competitorName }}</p>
                                        <p class="text-xs text-white/40 truncate">{{ ci.featureGap }}</p>
                                      </div>
                                      <span class="shrink-0 rounded-lg border border-red-500/30 bg-red-500/15
                                                   px-2.5 py-1 text-sm font-black text-red-300">
                                        {{ ci.impactScore }}
                                      </span>
                                    </div>
                                  }
                                }
                              </div>
                            </div>
                          }

                          <!-- ═══════════════════════════════════════
                               03  SOLUTION
                          ════════════════════════════════════════ -->
                          @case ('solution') {
                            <div class="flex h-full flex-col gap-3">
                              <!-- Before / After -->
                              <div class="grid grid-cols-2 gap-4 shrink-0">
                                <div class="rounded-xl border border-red-500/20 bg-red-500/8 p-4">
                                  <p class="mb-2.5 flex items-center gap-1.5 text-xs font-black uppercase tracking-wider text-red-400">
                                    <lucide-icon name="x-circle" [size]="13" /> Before — Today
                                  </p>
                                  @for (b of beforeState(); track $index) {
                                    <p class="flex items-center gap-2 py-0.5 text-sm text-white/55">
                                      <span class="text-red-500">✕</span>{{ b }}
                                    </p>
                                  }
                                </div>
                                <div class="rounded-xl border p-4" [class]="slide.accentBorder + ' ' + slide.accentCardBg">
                                  <p class="mb-2.5 flex items-center gap-1.5 text-xs font-black uppercase tracking-wider" [class]="slide.accentText">
                                    <lucide-icon name="check" [size]="13" /> After — With This Solution
                                  </p>
                                  @for (a of afterState(); track $index) {
                                    <p class="flex items-center gap-2 py-0.5 text-sm text-white/80">
                                      <span [class]="slide.accentText">✓</span>{{ a }}
                                    </p>
                                  }
                                </div>
                              </div>
                              <!-- Capabilities -->
                              <p class="text-[10px] font-black uppercase tracking-[0.2em]" [class]="slide.accentText">
                                What This Solution Delivers
                              </p>
                              <div class="grid grid-cols-3 gap-2 min-h-0 flex-1">
                                @for (cap of capabilities(sol); track $index) {
                                  <div class="flex flex-col gap-1 rounded-xl border p-3.5"
                                       [class]="slide.accentBorder + ' bg-white/5'">
                                    <p class="text-sm font-bold text-white/85 leading-snug">{{ cap.label }}</p>
                                    <p class="text-xs leading-relaxed text-white/45">{{ cap.detail }}</p>
                                  </div>
                                }
                              </div>
                            </div>
                          }

                          <!-- ═══════════════════════════════════════
                               04  ROI
                          ════════════════════════════════════════ -->
                          @case ('roi') {
                            <div class="flex h-full flex-col gap-3">
                              <!-- 3 big KPI boxes -->
                              <div class="grid grid-cols-3 gap-3 shrink-0">
                                @for (kpi of roiKpis(sol); track $index) {
                                  <div class="flex flex-col gap-1.5 rounded-2xl border p-4"
                                       [class]="slide.accentBorder + ' ' + slide.accentCardBg">
                                    <p class="text-[10px] font-bold uppercase tracking-wider text-white/35">{{ kpi.label }}</p>
                                    <p class="text-4xl font-black tabular-nums text-white leading-none" [class]="slide.accentText">
                                      {{ kpi.value }}
                                    </p>
                                    <p class="text-xs text-white/40">{{ kpi.note }}</p>
                                  </div>
                                }
                              </div>
                              <!-- Outcomes + Priority -->
                              <div class="grid grid-cols-2 gap-3 min-h-0 flex-1">
                                <div class="flex flex-col gap-2">
                                  <p class="text-[10px] font-black uppercase tracking-[0.2em]" [class]="slide.accentText">
                                    Expected Outcomes
                                  </p>
                                  @for (o of outcomes(sol); track $index) {
                                    <div class="flex items-start gap-3 rounded-xl border px-4 py-2.5 flex-1"
                                         [class]="slide.accentBorder + ' bg-white/5'">
                                      <span class="mt-1 h-2 w-2 shrink-0 rounded-full" [class]="slide.accentBar"></span>
                                      <div>
                                        <p class="text-sm font-bold text-white/85">{{ o.category }}</p>
                                        <p class="text-xs text-white/45 leading-snug">{{ o.detail }}</p>
                                      </div>
                                    </div>
                                  }
                                </div>
                                <div class="flex flex-col gap-2">
                                  <p class="text-[10px] font-black uppercase tracking-[0.2em]" [class]="slide.accentText">
                                    Why Prioritise Now
                                  </p>
                                  <div class="flex-1 rounded-xl border p-4" [class]="slide.accentBorder + ' bg-white/5'">
                                    <div class="flex items-center justify-between mb-2">
                                      <span class="text-xs font-bold uppercase tracking-wider text-white/30">Urgency</span>
                                      <span class="text-2xl font-black text-white">{{ sol.urgency }}/10</span>
                                    </div>
                                    <div class="mb-3 flex gap-0.5">
                                      @for (seg of ten; track seg) {
                                        <div class="h-2.5 flex-1 rounded-sm"
                                             [class]="seg <= sol.urgency ? 'bg-gradient-to-r from-red-500 to-orange-400' : 'bg-white/10'"></div>
                                      }
                                    </div>
                                    <p class="text-xs leading-relaxed text-white/50">{{ urgencyContext(sol.urgency) }}</p>
                                  </div>
                                  <div class="rounded-xl border px-4 py-3" [class]="slide.accentBorder + ' bg-white/5'">
                                    <p class="text-xs text-white/50">
                                      Value/complexity ratio <span class="font-black text-white/80">{{ (sol.value / sol.difficulty).toFixed(1) }}x</span>
                                      — {{ valueEfficiency(sol.value, sol.difficulty) }}
                                    </p>
                                  </div>
                                </div>
                              </div>
                            </div>
                          }

                          <!-- ═══════════════════════════════════════
                               05  SCOPE & FIT
                          ════════════════════════════════════════ -->
                          @case ('scope') {
                            <div class="grid h-full grid-cols-2 gap-5">
                              <div class="flex flex-col gap-2">
                                <p class="text-[10px] font-black uppercase tracking-[0.2em]" [class]="slide.accentText">
                                  Who Benefits &amp; How
                                </p>
                                @for (area of scopeAreas(sol); track $index) {
                                  <div class="flex items-start gap-3 rounded-xl border px-4 py-2.5 flex-1"
                                       [class]="slide.accentBorder + ' bg-white/5'">
                                    <span class="mt-1 h-2 w-2 shrink-0 rounded-full" [class]="slide.accentBar"></span>
                                    <div>
                                      <p class="text-sm font-bold text-white/85">{{ area.team }}</p>
                                      <p class="text-xs text-white/45 leading-snug">{{ area.benefit }}</p>
                                    </div>
                                  </div>
                                }
                              </div>
                              <div class="flex flex-col gap-2.5">
                                <p class="text-[10px] font-black uppercase tracking-[0.2em]" [class]="slide.accentText">
                                  Applicability &amp; Reach
                                </p>
                                @if (store.currentResearchData()?.domain) {
                                  <div class="rounded-xl border px-4 py-3" [class]="slide.accentBorder + ' ' + slide.accentCardBg">
                                    <p class="text-xs font-bold uppercase tracking-wider text-white/30 mb-1">Domain</p>
                                    <p class="text-lg font-black text-white/85">{{ store.currentResearchData()!.domain }}</p>
                                  </div>
                                }
                                @for (wf of workflows(sol); track $index) {
                                  <div class="flex items-center gap-3 rounded-xl border px-4 py-2.5"
                                       [class]="slide.accentBorder + ' bg-white/5'">
                                    <span class="h-2 w-2 shrink-0 rounded-full" [class]="slide.accentBar"></span>
                                    <p class="text-sm text-white/65">{{ wf }}</p>
                                  </div>
                                }
                                <div class="rounded-xl border p-3.5" [class]="slide.accentBorder + ' ' + slide.accentCardBg">
                                  <p class="text-xs font-bold text-white/30 uppercase tracking-wider mb-1">Adoption Breadth</p>
                                  <p class="text-xs text-white/60">{{ adoptionBreadth(sol.value, sol.urgency) }}</p>
                                </div>
                              </div>
                            </div>
                          }

                          <!-- ═══════════════════════════════════════
                               06  IMPLEMENTATION
                          ════════════════════════════════════════ -->
                          @case ('implementation') {
                            <div class="flex h-full flex-col gap-3">
                              <!-- 3 phases -->
                              <div class="grid grid-cols-3 gap-3 shrink-0">
                                @for (phase of implPhases(); track $index) {
                                  <div class="flex flex-col gap-2 rounded-2xl border p-4"
                                       [class]="phase.borderClass + ' ' + phase.bgClass">
                                    <div>
                                      <p class="text-[10px] font-black uppercase tracking-[0.18em]" [class]="phase.textClass">
                                        {{ phase.label }}
                                      </p>
                                      <p class="text-xs text-white/35">{{ phase.timeline }}</p>
                                    </div>
                                    @for (s of phase.steps; track $index) {
                                      <p class="flex items-start gap-1.5 text-xs text-white/60">
                                        <span class="mt-1.5 h-1 w-1 shrink-0 rounded-full" [class]="phase.dotClass"></span>{{ s }}
                                      </p>
                                    }
                                  </div>
                                }
                              </div>
                              <!-- Dependencies + Timeline -->
                              <div class="grid grid-cols-2 gap-3 min-h-0 flex-1">
                                <div class="flex flex-col gap-2">
                                  <p class="text-[10px] font-black uppercase tracking-[0.2em]" [class]="slide.accentText">
                                    Key Dependencies
                                  </p>
                                  @for (dep of dependencies(); track $index) {
                                    <div class="flex items-center gap-3 rounded-xl border px-4 py-2.5"
                                         [class]="slide.accentBorder + ' bg-white/5'">
                                      <span class="h-2 w-2 shrink-0 rounded-full" [class]="slide.accentBar"></span>
                                      <p class="text-sm text-white/65">{{ dep }}</p>
                                    </div>
                                  }
                                </div>
                                <div class="flex flex-col gap-2">
                                  <p class="text-[10px] font-black uppercase tracking-[0.2em]" [class]="slide.accentText">
                                    Timeline Summary
                                  </p>
                                  <div class="flex flex-1 flex-col gap-2">
                                    <div class="flex flex-col gap-1 rounded-xl border p-4 flex-1"
                                         [class]="slide.accentBorder + ' ' + slide.accentCardBg">
                                      <p class="text-sm font-bold text-white/70">Total Delivery</p>
                                      <p class="text-3xl font-black text-white">{{ totalTimeline(sol.difficulty) }}</p>
                                      <p class="text-xs text-white/35">Pilot through full scale</p>
                                    </div>
                                    <div class="rounded-xl border px-4 py-2.5" [class]="slide.accentBorder + ' bg-white/5'">
                                      <p class="text-xs text-white/50">
                                        <span class="font-semibold text-white/70">{{ effortLabel(sol.difficulty) }}</span>
                                        · Complexity {{ sol.difficulty }}/10
                                      </p>
                                    </div>
                                  </div>
                                </div>
                              </div>
                            </div>
                          }

                          <!-- ═══════════════════════════════════════
                               07  RISKS
                          ════════════════════════════════════════ -->
                          @case ('risks') {
                            <div class="flex h-full flex-col gap-2.5">
                              <p class="text-[10px] font-black uppercase tracking-[0.2em]" [class]="slide.accentText">
                                Top Risks — Identified &amp; Mitigated
                              </p>
                              @for (risk of riskItems(sol); track $index) {
                                <div class="grid grid-cols-5 gap-0 overflow-hidden rounded-xl border flex-1"
                                     [class]="slide.accentBorder">
                                  <div class="col-span-2 flex flex-col gap-1.5 border-r px-4 py-3"
                                       [class]="slide.accentBorder + ' ' + slide.accentCardBg">
                                    <div class="flex items-start justify-between gap-2">
                                      <p class="text-sm font-bold text-white/85 leading-snug">{{ risk.risk }}</p>
                                      <span class="shrink-0 rounded border px-2 py-0.5 text-[10px] font-black"
                                            [class]="risk.impactClass">{{ risk.impact }}</span>
                                    </div>
                                    <p class="text-xs text-white/40 leading-snug">{{ risk.impactDetail }}</p>
                                  </div>
                                  <div class="col-span-3 flex flex-col gap-1 px-4 py-3 bg-white/3">
                                    <p class="text-[10px] font-bold uppercase tracking-wider text-white/28">Mitigation</p>
                                    <p class="text-sm leading-relaxed text-white/65">{{ risk.mitigation }}</p>
                                  </div>
                                </div>
                              }
                              <div class="rounded-xl border px-4 py-2.5" [class]="slide.accentBorder + ' ' + slide.accentCardBg">
                                <p class="text-xs text-white/55">
                                  Risk profile is <span class="font-bold" [class]="slide.accentText">manageable</span>
                                  with standard programme governance and experienced delivery leadership.
                                </p>
                              </div>
                            </div>
                          }

                          <!-- ═══════════════════════════════════════
                               08  INVESTMENT ASK
                          ════════════════════════════════════════ -->
                          @case ('investment') {
                            <div class="grid h-full grid-cols-2 gap-5">
                              <div class="flex flex-col gap-2.5">
                                <p class="text-[10px] font-black uppercase tracking-[0.2em]" [class]="slide.accentText">
                                  What We Need Approved
                                </p>
                                @for (item of investmentItems(sol); track $index) {
                                  <div class="flex items-center gap-4 rounded-xl border p-4 flex-1"
                                       [class]="slide.accentBorder + ' ' + slide.accentCardBg">
                                    <div class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border"
                                         [class]="slide.accentBorder + ' bg-white/5'">
                                      <lucide-icon [name]="item.icon" [size]="18" [class]="slide.accentText" />
                                    </div>
                                    <div>
                                      <p class="text-[10px] font-bold uppercase tracking-wider text-white/30">{{ item.label }}</p>
                                      <p class="text-xl font-black text-white leading-tight">{{ item.value }}</p>
                                      <p class="text-xs text-white/40">{{ item.note }}</p>
                                    </div>
                                  </div>
                                }
                              </div>
                              <div class="flex flex-col gap-2.5">
                                <p class="text-[10px] font-black uppercase tracking-[0.2em]" [class]="slide.accentText">
                                  Investment Justification
                                </p>
                                <div class="rounded-xl border p-4 flex-1" [class]="slide.accentBorder + ' ' + slide.accentCardBg">
                                  <p class="text-xs font-bold uppercase tracking-wider text-white/30 mb-3">Why This Is Worth It</p>
                                  @for (row of roiTable(sol); track $index) {
                                    <div class="flex items-center justify-between py-1.5 border-b border-white/6 last:border-0">
                                      <span class="text-sm text-white/50">{{ row.label }}</span>
                                      <span class="text-sm font-bold" [class]="row.color">{{ row.value }}</span>
                                    </div>
                                  }
                                </div>
                                <div class="rounded-xl border px-4 py-3" [class]="slide.accentBorder + ' bg-white/5'">
                                  <p class="text-xs leading-relaxed text-white/55">
                                    Investment proportionate to
                                    <span class="font-bold text-white/80">{{ sol.value * 10 }}%</span> potential impact
                                    at <span class="font-bold text-white/80">{{ effortShort(sol.difficulty) }}</span> complexity.
                                  </p>
                                </div>
                              </div>
                            </div>
                          }

                          <!-- ═══════════════════════════════════════
                               09  NEXT STEPS
                          ════════════════════════════════════════ -->
                          @case ('next-steps') {
                            <div class="grid h-full grid-cols-2 gap-5">
                              <div class="flex flex-col gap-2.5">
                                <p class="text-[10px] font-black uppercase tracking-[0.2em]" [class]="slide.accentText">
                                  Immediate Actions Post-Approval
                                </p>
                                @for (step of nextSteps(); track $index; let last = $last) {
                                  <div class="flex gap-3 rounded-xl border p-3.5 flex-1"
                                       [class]="slide.accentBorder + ' bg-white/5'">
                                    <div class="flex h-7 w-7 shrink-0 items-center justify-center rounded-full
                                                border text-[10px] font-black"
                                         [class]="slide.accentBorder + ' ' + slide.accentText">
                                      {{ $index + 1 }}
                                    </div>
                                    <div class="min-w-0 flex-1">
                                      <p class="text-sm font-bold text-white/85 leading-snug">{{ step.action }}</p>
                                      <div class="mt-1 flex items-center gap-2 text-xs text-white/35">
                                        <span>{{ step.owner }}</span>
                                        <span class="h-3 w-px bg-white/15"></span>
                                        <span [class]="slide.accentText">{{ step.timeline }}</span>
                                      </div>
                                    </div>
                                  </div>
                                }
                              </div>
                              <!-- CTA -->
                              <div class="flex flex-col gap-2.5">
                                <p class="text-[10px] font-black uppercase tracking-[0.2em]" [class]="slide.accentText">
                                  Decision Required
                                </p>
                                <div class="rounded-2xl border border-green-500/30 bg-green-500/10 p-5 flex-1">
                                  <p class="text-[10px] font-black uppercase tracking-[0.2em] text-green-400/60 mb-3">
                                    Approve to Proceed
                                  </p>
                                  <p class="text-lg font-black text-white leading-tight mb-4">{{ sol.name }}</p>
                                  @for (row of roiTable(sol); track $index) {
                                    <div class="flex justify-between py-1 border-b border-white/6 last:border-0 text-sm">
                                      <span class="text-white/45">{{ row.label }}</span>
                                      <span class="font-bold" [class]="row.color">{{ row.value }}</span>
                                    </div>
                                  }
                                  <div class="mt-4 rounded-xl border border-green-400/25 bg-green-400/10 px-3 py-2.5 text-center">
                                    <p class="text-sm font-bold text-green-300">
                                      Urgency {{ sol.urgency }}/10 — {{ urgencyShort(sol.urgency) }}
                                    </p>
                                  </div>
                                </div>
                              </div>
                            </div>
                          }

                        }
                      }
                    </div>

                    <!-- Slide footer -->
                    <div class="relative z-10 flex shrink-0 items-center justify-between px-10 pb-4">
                      <span class="text-[9px] font-medium uppercase tracking-[0.18em] text-white/18">
                        Meridian Studio · AI Solution Agent & System Architect Hub
                      </span>
                      <span class="tabular-nums text-[9px] text-white/18">{{ currentIndex() + 1 }} / 09</span>
                    </div>

                  </div><!-- /slide bg -->
                }
              </div>
            }
          </div><!-- /frame -->

          <!-- Dot indicators -->
          <div class="flex shrink-0 flex-col items-center gap-2">
            <div class="flex items-center gap-1.5" role="tablist">
              @for (slide of slides(); track slide.index; let i = $index) {
                <button (click)="goToSlide(i)" role="tab"
                  [attr.aria-selected]="i === currentIndex()"
                  [attr.title]="slide.label"
                  [class]="dotClass(i, slide)"></button>
              }
            </div>
            <p class="text-[9px] text-gray-400">← → navigate &nbsp;·&nbsp; hover edges for arrows &nbsp;·&nbsp; F fullscreen</p>
          </div>
        }
      </div>
    </div>
  `,
})
export class ExecutiveSlidesComponent implements OnInit, OnDestroy {
  @ViewChild('slideFrame') private slideFrame!: ElementRef<HTMLDivElement>;

  protected readonly store  = inject(WorkspaceStoreService);
  protected readonly pptxSvc = inject(PptxExportService);

  protected readonly currentIndex = signal(0);
  protected readonly direction = signal<'forward' | 'backward'>('forward');
  protected readonly slideVisible = signal(true);
  protected readonly isFullscreen = signal(false);
  protected readonly isExporting = signal(false);
  protected readonly ten = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

  protected readonly slides = computed<SlideConfig[]>(() =>
    SLIDES.map((cfg, i) => ({ ...cfg, index: i })),
  );
  protected readonly currentSlide = computed(() => this.slides()[this.currentIndex()] ?? null);

  protected readonly integrationStepsArr = computed(() => {
    const sol = this.store.selectedSolution();
    if (!sol?.integrationSteps) return [];
    return sol.integrationSteps
      .split('\n')
      .filter(l => /^\d+[.)]\s/.test(l.trim()))
      .map(l => l.replace(/^\d+[.)]\s*/, '').trim())
      .filter(Boolean).slice(0, 6);
  });

  // ── Slide data helpers ─────────────────────────────────────────────────

  protected execBullets(): { label: string; detail: string }[] {
    const sol = this.store.selectedSolution();
    if (!sol) return [];
    return [
      { label: sol.name, detail: sol.description.slice(0, 130) + (sol.description.length > 130 ? '…' : '') },
      { label: 'Business Impact', detail: sol.realLifeValue || `Projected ${sol.value * 10}% uplift in operational efficiency.` },
      { label: 'Market Urgency', detail: this.urgencyContext(sol.urgency) },
      { label: 'Implementation Profile', detail: `${this.effortLabel(sol.difficulty)} · ${sol.difficulty}/10 difficulty · ${this.totalTimeline(sol.difficulty)} delivery` },
      { label: 'Recommended Action', detail: 'Approve Phase 1 this planning cycle to capture first-mover advantage and begin realising value within 8 weeks.' },
    ];
  }

  protected readonly painPoints = computed((): string[] => {
    const sol = this.store.selectedSolution();
    if (!sol) return [];
    const raw = [sol.description, sol.rationale].filter(Boolean).join(' ');
    const sentences = raw.match(/[^.!?]+[.!?]+/g) ?? [];
    const pain = sentences
      .filter(s => /challenge|problem|gap|lack|slow|manual|inefficient|costly|risk|complex/i.test(s))
      .map(s => s.trim()).slice(0, 4);
    return pain.length >= 2 ? pain : [
      `Current ${sol.name.toLowerCase()} processes are largely manual, creating bottlenecks and inconsistent outcomes.`,
      `Teams lack unified visibility, forcing reactive rather than proactive decision-making.`,
      `The absence of this capability creates measurable competitive and operational exposure.`,
      `Peer organisations that have adopted comparable solutions report significant productivity advantages.`,
    ];
  });

  protected beforeState(): string[] {
    const n = this.store.selectedSolution()?.name.toLowerCase() ?? 'this process';
    return [
      `Manual, fragmented ${n} workflows`,
      'Siloed data requiring cross-system reconciliation',
      'Slow reporting cycles with limited real-time insight',
      'Reactive decisions, higher error rate',
      'Staff time consumed by low-value tasks',
    ];
  }

  protected afterState(): string[] {
    const n = this.store.selectedSolution()?.name.toLowerCase() ?? 'this process';
    return [
      `Automated, end-to-end ${n} workflows`,
      'Unified data platform — single source of truth',
      'Real-time dashboards and proactive alerts',
      'Data-driven decisions with AI-assisted insights',
      'Staff focused on high-value strategic work',
    ];
  }

  protected capabilities(sol: { name: string }): { label: string; detail: string }[] {
    return [
      { label: 'Workflow Automation', detail: `Eliminates manual steps in ${sol.name.toLowerCase()}, reducing processing time by 60–80%.` },
      { label: 'Unified Intelligence', detail: 'Centralised data with AI-powered insights surfaced in real time.' },
      { label: 'Seamless Integration', detail: 'Connects to existing systems via standard APIs — no infrastructure replacement.' },
      { label: 'Audit & Compliance', detail: 'Full activity logging, RBAC, and configurable retention policies.' },
      { label: 'Scalable Architecture', detail: 'Designed to grow from focused pilot to org-wide adoption without rearchitecting.' },
      { label: 'Rapid Time to Value', detail: 'Phase 1 go-live delivers measurable outcomes within 8 weeks of kickoff.' },
    ];
  }

  protected roiKpis(sol: { value: number; difficulty: number; urgency: number }): { label: string; value: string; note: string }[] {
    return [
      { label: 'Business Impact Score', value: `${sol.value * 10}%`, note: 'Potential value uplift' },
      { label: 'Estimated Year-1 ROI', value: `~${this.roiPercent(sol)}%`, note: 'Value/effort projection' },
      { label: 'Break-even Timeline', value: this.breakeven(sol.difficulty), note: 'Post go-live' },
    ];
  }

  protected outcomes(sol: { value: number; urgency: number }): { category: string; detail: string }[] {
    return [
      { category: 'Time Savings', detail: `Est. 40–70% reduction in manual processing hours per week.` },
      { category: 'Cost Reduction', detail: `Savings proportionate to ${sol.value}/10 value score.` },
      { category: 'Risk Mitigation', detail: 'Reduced human error, improved audit trail, stronger compliance posture.' },
      { category: 'Competitive Advantage', detail: `Urgency ${sol.urgency}/10 — acting now captures first-mover positioning.` },
    ];
  }

  protected scopeAreas(sol: { description: string }): { team: string; benefit: string }[] {
    const text = sol.description.toLowerCase();
    const all = [
      { team: 'Operations & Delivery', benefit: 'Streamlined workflows reduce cycle times significantly.', k: ['operation','process','workflow','deliver'] },
      { team: 'Data & Analytics', benefit: 'Unified data access enables accurate, timely reporting.', k: ['data','analytic','report','metric','insight'] },
      { team: 'Technology & Engineering', benefit: 'API-first architecture simplifies stack integration.', k: ['api','system','software','develop','engineer'] },
      { team: 'Finance & Risk', benefit: 'Audit trail and controls strengthen compliance and reduce cost.', k: ['cost','finance','risk','budget','compliance'] },
      { team: 'Executive Leadership', benefit: 'Real-time visibility supports strategic decisions.', k: ['strategy','decision','leadership','executive'] },
      { team: 'Customer & Client-Facing', benefit: 'Faster turnaround and consistency improve client experience.', k: ['client','customer','service','experience'] },
    ];
    const matched = all.filter(a => a.k.some(k => text.includes(k)));
    return (matched.length >= 3 ? matched : all).slice(0, 5).map(({ team, benefit }) => ({ team, benefit }));
  }

  protected workflows(sol: { name: string }): string[] {
    return [
      `End-to-end ${sol.name.toLowerCase()} process automation`,
      'Cross-functional data sharing and reconciliation',
      'Audit, reporting and compliance workflows',
      'Exception handling and escalation management',
    ];
  }

  protected adoptionBreadth(value: number, urgency: number): string {
    const s = value + urgency;
    if (s >= 16) return 'Organisation-wide applicability — broad adoption recommended from the outset.';
    if (s >= 12) return 'Multi-team rollout — start with 2–3 priority groups, expand in Phase 3.';
    return 'Targeted adoption — pilot with a focused team before scaling broadly.';
  }

  protected implPhases(): { label: string; timeline: string; steps: string[]; borderClass: string; bgClass: string; textClass: string; dotClass: string }[] {
    const steps = this.integrationStepsArr();
    const chunk = Math.ceil(steps.length / 3) || 1;
    const def = [
      ['Define scope and success criteria', 'Select pilot team', 'Configure core components'],
      ['Measure pilot against KPIs', 'Gather feedback and refine', 'Validate integrations'],
      ['Full organisational rollout', 'Change management + training', 'Continuous improvement'],
    ];
    return [
      { label: 'Phase 1 — Pilot', timeline: 'Weeks 1–8', steps: steps.length ? steps.slice(0, chunk) : def[0], borderClass: 'border-teal-500/25', bgClass: 'bg-teal-500/8', textClass: 'text-teal-400', dotClass: 'bg-teal-400' },
      { label: 'Phase 2 — Validate', timeline: 'Weeks 9–16', steps: steps.length ? steps.slice(chunk, chunk * 2) : def[1], borderClass: 'border-blue-500/25', bgClass: 'bg-blue-500/8', textClass: 'text-blue-400', dotClass: 'bg-blue-400' },
      { label: 'Phase 3 — Scale', timeline: 'Weeks 17–24+', steps: steps.length ? steps.slice(chunk * 2) : def[2], borderClass: 'border-violet-500/25', bgClass: 'bg-violet-500/8', textClass: 'text-violet-400', dotClass: 'bg-violet-400' },
    ];
  }

  protected dependencies(): string[] {
    return [
      'Executive sponsor assigned and steering committee formed',
      'Access to source systems and data for integration',
      'Dedicated project manager and engineering resource',
      'Legal/compliance sign-off on data handling approach',
    ];
  }

  protected riskItems(sol: { name: string; difficulty: number }): { risk: string; impact: string; impactClass: string; impactDetail: string; mitigation: string }[] {
    return [
      { risk: 'Stakeholder adoption resistance', impact: 'Medium', impactClass: 'border-amber-500/30 bg-amber-500/15 text-amber-300', impactDetail: 'Slow adoption delays value realisation and reduces measured ROI.', mitigation: 'Executive sponsorship, early champion network, role-specific training, and phased rollout with clear quick wins in Phase 1.' },
      { risk: `Implementation complexity (${sol.difficulty}/10)`, impact: sol.difficulty >= 7 ? 'High' : 'Medium', impactClass: sol.difficulty >= 7 ? 'border-red-500/30 bg-red-500/15 text-red-300' : 'border-amber-500/30 bg-amber-500/15 text-amber-300', impactDetail: 'Technical scope creep or integration challenges could extend the delivery timeline.', mitigation: 'Agile delivery with 2-week sprints, bi-weekly steering reviews, and a clearly scoped Phase 1 MVP to de-risk early.' },
      { risk: 'Data quality and integration gaps', impact: 'Medium', impactClass: 'border-amber-500/30 bg-amber-500/15 text-amber-300', impactDetail: 'Poor source data degrades solution quality and undermines stakeholder trust.', mitigation: 'Mandatory data audit in Week 1, dedicated data engineering resource, and data governance framework before go-live.' },
    ];
  }

  protected investmentItems(sol: { difficulty: number }): { label: string; value: string; note: string; icon: string }[] {
    return [
      { label: 'Budget Range', value: this.budgetRange(sol.difficulty), note: 'Software, delivery, change management', icon: 'dollar-sign' },
      { label: 'Delivery Timeline', value: this.totalTimeline(sol.difficulty), note: 'Pilot through full-scale rollout', icon: 'clock' },
      { label: 'Team Commitment', value: this.headcount(sol.difficulty), note: 'Delivery team + client-side sponsor time', icon: 'users' },
    ];
  }

  protected roiTable(sol: { value: number; difficulty: number; urgency: number }): { label: string; value: string; color: string }[] {
    return [
      { label: 'Investment', value: this.budgetRange(sol.difficulty), color: 'text-white/80' },
      { label: 'Estimated ROI (Yr 1)', value: `~${this.roiPercent(sol)}%`, color: 'text-emerald-300' },
      { label: 'Break-even', value: this.breakeven(sol.difficulty), color: 'text-white/80' },
      { label: 'Business Value Score', value: `${sol.value}/10`, color: 'text-white/80' },
    ];
  }

  protected nextSteps(): { action: string; owner: string; timeline: string }[] {
    return [
      { action: 'Approve proposal and designate executive sponsor', owner: 'Leadership', timeline: 'This week' },
      { action: 'Convene steering committee and confirm stakeholder group', owner: 'Sponsor / PMO', timeline: 'Week 1' },
      { action: 'Commission technical discovery and scoping workshop', owner: 'Engineering Lead', timeline: 'Weeks 1–2' },
      { action: 'Confirm Phase 1 budget and resource allocation', owner: 'Finance / Leadership', timeline: 'Week 2' },
    ];
  }

  // ── Shared helpers ─────────────────────────────────────────────────────

  protected roiPercent(sol: { value: number; difficulty: number; urgency: number }): number {
    return Math.round((sol.value / Math.max(sol.difficulty, 1)) * sol.urgency * 12);
  }

  protected breakeven(difficulty: number): string {
    if (difficulty <= 3) return '2–3 months';
    if (difficulty <= 6) return '4–6 months';
    return '8–12 months';
  }

  protected totalTimeline(difficulty: number): string {
    if (difficulty <= 3) return '12–16 wks';
    if (difficulty <= 6) return '20–26 wks';
    return '28–36 wks';
  }

  protected effortLabel(difficulty: number): string {
    if (difficulty <= 3) return 'Low complexity';
    if (difficulty <= 6) return 'Medium complexity';
    return 'High complexity';
  }

  protected effortShort(difficulty: number): string {
    if (difficulty <= 3) return 'Low';
    if (difficulty <= 6) return 'Medium';
    return 'High';
  }

  protected budgetRange(difficulty: number): string {
    if (difficulty <= 3) return '$25K – $75K';
    if (difficulty <= 6) return '$75K – $250K';
    return '$250K – $750K';
  }

  protected headcount(difficulty: number): string {
    if (difficulty <= 3) return '2–3 FTEs';
    if (difficulty <= 6) return '4–6 FTEs';
    return '6–10 FTEs';
  }

  protected urgencyContext(urgency: number): string {
    if (urgency >= 8) return 'Critical window — competitors are moving now. Delay erodes market position and increases catch-up cost exponentially.';
    if (urgency >= 6) return 'High market momentum — acting this quarter captures first-mover advantage before the window narrows.';
    if (urgency >= 4) return 'Moderate timing pressure — best value is captured within the next planning cycle.';
    return 'Deliberate pacing is acceptable; schedule within 2 quarters to avoid technical debt accumulation.';
  }

  protected urgencyShort(urgency: number): string {
    if (urgency >= 8) return 'act this sprint';
    if (urgency >= 6) return 'act this quarter';
    return 'schedule next cycle';
  }

  protected valueEfficiency(value: number, difficulty: number): string {
    const r = value / Math.max(difficulty, 1);
    if (r >= 1.5) return 'high-efficiency return';
    if (r >= 1.0) return 'well-balanced investment';
    return 'long-horizon strategic play';
  }

  // ── Lifecycle & navigation ─────────────────────────────────────────────

  private fullscreenHandler?: () => void;

  ngOnInit(): void {
    this.fullscreenHandler = () => this.isFullscreen.set(!!document.fullscreenElement);
    document.addEventListener('fullscreenchange', this.fullscreenHandler);

    // Demo mode: ?demo in URL seeds a mock solution so the deck renders without a live backend.
    if (new URLSearchParams(location.search).has('demo') && !this.store.selectedSolution()) {
      this.store.selectedSolution.set({
        id: 'demo-001',
        name: 'AI-Powered Dynamic Sales Playbook Generator',
        description: 'This AI analyses historical deal data, market trends, and prospect interactions to dynamically generate customised sales playbooks for specific accounts, industries, and deal stages — enabling reps to engage with precision-targeted messaging and proven tactics.',
        urgency: 9,
        difficulty: 7,
        value: 9,
        rationale: 'Sales teams waste 30–40% of their time on generic outreach that fails to address prospect-specific pain points. A dynamic playbook system closes this gap by synthesising institutional knowledge at scale.',
        realLifeValue: 'Increase win rates by 7–10%, reduce sales cycle length by 25%, and enable new reps to perform at senior-rep level within 60 days of onboarding.',
        integrationSteps: '1. Audit CRM data and historical deal outcomes\n2. Train AI model on win/loss patterns and messaging data\n3. Build playbook generation engine with LLM integration\n4. Create rep-facing UI with real-time context delivery\n5. Integrate with email, calendar, and meeting tools\n6. Pilot with 10 reps, measure outcomes, and iterate',
        feasibilityScore: 0,
        feasibilityAnalysis: '',
      });
      this.store.currentResearchData.set({
        domain: 'B2B Sales Technology',
        domainsList: ['SaaS', 'Enterprise Sales', 'RevOps'],
        competitorInsights: [
          { competitorName: 'Salesforce Einstein', featureGap: 'Proactive deal interventions are often generic and reactive', impactScore: '7.8/10', strategicPlaybook: 'Focus on precision personalisation vs broad AI features' },
          { competitorName: 'Gong.io', featureGap: 'Strong call analysis but weak forward-looking playbook generation', impactScore: '7.5/10', strategicPlaybook: 'Lead with synthesis and next-action recommendations' },
        ],
        items: [],
        modelUsed: 'claude-sonnet-4-6',
      });
    }
  }

  ngOnDestroy(): void {
    if (this.fullscreenHandler)
      document.removeEventListener('fullscreenchange', this.fullscreenHandler);
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(e: KeyboardEvent): void {
    if (this.store.activeWorkspace() !== 'presentation') return;
    if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) return;
    switch (e.key) {
      case 'ArrowRight': case ' ': e.preventDefault(); this.nextSlide(); break;
      case 'ArrowLeft':  e.preventDefault(); this.prevSlide(); break;
      case 'f': case 'F': this.toggleFullscreen(); break;
      case 'Escape': if (this.isFullscreen()) void document.exitFullscreen(); break;
    }
  }

  protected nextSlide(): void {
    if (this.currentIndex() >= this.slides().length - 1) return;
    this.direction.set('forward');
    this.currentIndex.update(i => i + 1);
    this.triggerAnim();
  }

  protected prevSlide(): void {
    if (this.currentIndex() <= 0) return;
    this.direction.set('backward');
    this.currentIndex.update(i => i - 1);
    this.triggerAnim();
  }

  protected goToSlide(index: number): void {
    if (index === this.currentIndex()) return;
    this.direction.set(index > this.currentIndex() ? 'forward' : 'backward');
    this.currentIndex.set(index);
    this.triggerAnim();
  }

  private triggerAnim(): void {
    this.slideVisible.set(false);
    queueMicrotask(() => this.slideVisible.set(true));
  }

  protected async toggleFullscreen(): Promise<void> {
    try {
      if (!document.fullscreenElement)
        await (this.slideFrame?.nativeElement?.requestFullscreen() ?? Promise.resolve());
      else
        await document.exitFullscreen();
    } catch { /* ignore */ }
  }

  protected async onDownloadPptx(): Promise<void> {
    if (this.isExporting()) return;
    this.isExporting.set(true);
    try {
      await this.pptxSvc.exportToPptx();
    } catch (e) {
      console.error('PPTX export failed:', e);
    } finally {
      this.isExporting.set(false);
    }
  }

  protected dotClass(index: number, slide: SlideConfig): string {
    const base = 'h-2 rounded-full transition-all duration-300 focus:outline-none';
    return index === this.currentIndex()
      ? `${base} w-5 ${slide.accentBar}`
      : `${base} w-2 bg-gray-700 hover:bg-gray-500`;
  }
}
