import { Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { WorkspaceStoreService } from '../../core/services/workspace-store.service';
import {
  CorporateDocument,
  CriteriaOption,
  DocumentTemplateType,
  GenerateDocumentRequest,
  GoalOption,
  MissionSuggestions,
  MissionSuggestionsRequest,
  ToneOption,
} from '../../core/models/interfaces';
import { MarkdownPipe } from '../../core/pipes/markdown.pipe';
import { MermaidDirective } from '../../core/directives/mermaid.directive';
import { DocumentExportService } from '../../core/services/document-export.service';

interface TemplateOption {
  type: DocumentTemplateType;
  label: string;
  shortLabel: string;
  description: string;
  accent: string;
  accentLight: string;
}

const TEMPLATES: TemplateOption[] = [
  {
    type: 'executive-summary',
    label: 'Executive Summary',
    shortLabel: 'Exec Summary',
    description: 'High-level narrative for leadership and stakeholders.',
    accent: 'text-indigo-400',
    accentLight: 'bg-indigo-500/10 border-indigo-500/30',
  },
  {
    type: 'market-analysis',
    label: 'Market Analysis',
    shortLabel: 'Market Analysis',
    description: 'Competitive landscape, opportunity sizing, and strategic positioning.',
    accent: 'text-blue-400',
    accentLight: 'bg-blue-500/10 border-blue-500/30',
  },
  {
    type: 'technical-specification',
    label: 'Technical Specification',
    shortLabel: 'Tech Spec',
    description: 'Full technical spec with architecture decisions and implementation guides.',
    accent: 'text-violet-400',
    accentLight: 'bg-violet-500/10 border-violet-500/30',
  },
  {
    type: 'proposal',
    label: 'Proposal Document',
    shortLabel: 'Proposal',
    description: 'Client-facing proposal with scope, deliverables, and investment overview.',
    accent: 'text-emerald-400',
    accentLight: 'bg-emerald-500/10 border-emerald-500/30',
  },
  {
    type: 'governance-adr',
    label: 'Governance & ADR',
    shortLabel: 'Governance',
    description: 'Architecture Decision Record with failure modes and alternatives analysis.',
    accent: 'text-amber-400',
    accentLight: 'bg-amber-500/10 border-amber-500/30',
  },
  {
    type: 'developer-handbook',
    label: 'Developer Handbook',
    shortLabel: 'Dev Handbook',
    description: 'Epics, user stories, architecture overview, and component reference.',
    accent: 'text-cyan-400',
    accentLight: 'bg-cyan-500/10 border-cyan-500/30',
  },
  {
    type: 'detailed-design',
    label: 'Detailed Design',
    shortLabel: 'Detailed Design',
    description: 'Sprint-ready guide with data models, API contracts, and sprint plan.',
    accent: 'text-rose-400',
    accentLight: 'bg-rose-500/10 border-rose-500/30',
  },
];

type CopyState = 'idle' | 'copied' | 'error';

@Component({
  selector: 'app-document-studio',
  standalone: true,
  imports: [CommonModule, FormsModule, MarkdownPipe, MermaidDirective, LucideAngularModule],
  template: `
    <div class="relative flex h-full w-full min-h-0 overflow-hidden">

      <!-- ══ Left: Document Type Panel ═════════════════════════════ -->
      <aside class="flex w-72 shrink-0 flex-col border-r border-gray-800/60 bg-gray-950/60">

        <div class="border-b border-gray-800/60 px-5 py-4">
          <div class="flex items-center gap-2.5">
            <div class="flex h-7 w-7 items-center justify-center rounded-lg bg-indigo-500/15">
              <lucide-icon name="file-text" [size]="14" class="text-indigo-400" />
            </div>
            <h2 class="text-sm font-semibold tracking-tight text-white">Document Studio</h2>
          </div>
          <p class="mt-1.5 text-[11px] text-gray-400">
            Select a template, configure the mission, then generate.
          </p>
        </div>

        <!-- Scrollable body -->
        <div class="flex min-h-0 flex-1 flex-col overflow-y-auto">

          <!-- Document Type list -->
          <div class="flex flex-col gap-1 p-3">
            <p class="mb-1 px-2 text-[10px] font-semibold uppercase tracking-wider text-gray-500">
              Document Type
            </p>
            @for (tpl of templates; track tpl.type) {
              <button (click)="onTemplateChange(tpl.type)" [class]="templateCardClass(tpl)">
                <div [class]="selectedTemplate() === tpl.type
                              ? 'h-4 w-4 shrink-0 rounded-full border-2 border-current flex items-center justify-center ' + tpl.accent
                              : 'h-4 w-4 shrink-0 rounded-full border-2 border-gray-700 bg-gray-800'">
                  @if (selectedTemplate() === tpl.type) {
                    <div class="h-1.5 w-1.5 rounded-full bg-current"></div>
                  }
                </div>
                <span class="text-xs leading-tight"
                      [class]="selectedTemplate() === tpl.type ? tpl.accent + ' font-semibold' : 'text-gray-400'">
                  {{ tpl.label }}
                </span>
              </button>
            }
          </div>

          <!-- Mission summary card -->
          <div class="border-t border-gray-800/60 px-4 py-3">
            <div class="mb-2 flex items-center justify-between">
              <span class="text-[10px] font-semibold uppercase tracking-wider text-gray-500">Mission</span>
              <button
                (click)="isConfigureModalOpen.set(true)"
                class="flex items-center gap-1.5 rounded-md border border-gray-700/60 px-2 py-1
                       text-[10px] font-medium text-gray-400 transition-colors
                       hover:border-indigo-500/40 hover:text-indigo-400">
                <lucide-icon name="settings-2" [size]="11" />
                Configure
              </button>
            </div>

            @if (isSuggestionsLoading()) {
              <div class="space-y-1.5 rounded-lg border border-gray-800/40 bg-gray-900/40 p-3">
                <div class="h-3 w-28 animate-pulse rounded bg-gray-800"></div>
                <div class="h-3 w-36 animate-pulse rounded bg-gray-800"></div>
                <div class="h-3 w-32 animate-pulse rounded bg-gray-800"></div>
              </div>
            } @else {
              <div class="cursor-pointer rounded-lg border border-gray-800/40 bg-gray-900/40 px-3 py-2.5
                          space-y-1.5 transition-colors hover:border-indigo-500/20"
                   (click)="isConfigureModalOpen.set(true)">
                @if (suggestions()?.persona) {
                  <div class="flex items-start gap-2">
                    <lucide-icon name="user" [size]="10" class="mt-0.5 shrink-0 text-indigo-400" />
                    <span class="line-clamp-1 text-[10px] leading-tight text-indigo-300">
                      {{ suggestions()!.persona }}
                    </span>
                  </div>
                }
                @if (effectiveTone()) {
                  <div class="flex items-start gap-2">
                    <lucide-icon name="mic" [size]="10" class="mt-0.5 shrink-0 text-gray-500" />
                    <span class="line-clamp-1 text-[10px] leading-tight text-gray-400">{{ effectiveTone() }}</span>
                  </div>
                }
                @if (effectiveGoal()) {
                  <div class="flex items-start gap-2">
                    <lucide-icon name="target" [size]="10" class="mt-0.5 shrink-0 text-gray-500" />
                    <span class="line-clamp-2 text-[10px] leading-tight text-gray-400">
                      {{ effectiveGoal().slice(0, 90) }}{{ effectiveGoal().length > 90 ? '…' : '' }}
                    </span>
                  </div>
                }
                @if (effectiveCriteria().length) {
                  <div class="flex items-center gap-2">
                    <lucide-icon name="list-checks" [size]="10" class="shrink-0 text-gray-500" />
                    <span class="text-[10px] text-gray-500">{{ effectiveCriteria().length }} success criteria</span>
                  </div>
                }
                @if (wasRefined()) {
                  <span class="inline-block text-[9px] text-amber-400">✎ Refined</span>
                }
              </div>
            }
          </div>

        </div>

        <!-- Document Title + Generate (pinned to bottom) -->
        <div class="shrink-0 border-t border-gray-800/60 px-4 pb-4 pt-3">
          <label class="mb-1.5 block text-[10px] font-semibold uppercase tracking-wider text-gray-500">
            Document Title
          </label>
          <input
            type="text"
            [ngModel]="docTitle()"
            (ngModelChange)="docTitle.set($event)"
            placeholder="Auto-generated from blueprint…"
            class="h-9 w-full rounded-lg border border-gray-800 bg-gray-900/60 px-3
                   text-xs text-gray-300 placeholder-gray-500
                   focus:border-indigo-500/40 focus:outline-none focus:ring-1 focus:ring-indigo-500/20"
          />

          @if (!store.documentSource()) {
            <p class="mt-2 text-[10px] text-gray-500">
              Generate a blueprint or run a use-case assessment first to enable document creation.
            </p>
          }

          <button
            (click)="onGenerate()"
            [disabled]="store.isGeneratingDocument() || !store.documentSource()"
            class="mt-4 flex h-11 w-full items-center justify-center gap-2 rounded-xl
                   bg-indigo-600 text-sm font-medium text-white shadow-md shadow-indigo-600/20
                   transition-all hover:bg-indigo-500 focus:outline-none focus:ring-2
                   focus:ring-indigo-500/40 disabled:cursor-not-allowed disabled:opacity-50"
          >
            @if (store.isGeneratingDocument()) {
              <lucide-icon name="loader-2" [size]="15" class="animate-spin" />
              <span>{{ iterationLabel() }}</span>
            } @else {
              <lucide-icon name="sparkles" [size]="15" />
              <span>Generate {{ activeTemplate()?.shortLabel }}</span>
            }
          </button>
        </div>

      </aside>

      <!-- ══ Right: Document Viewer ══════════════════════════════════ -->
      <div class="flex min-w-0 flex-1 flex-col">

        <!-- Viewer header -->
        <div class="flex shrink-0 items-center justify-between border-b border-gray-800/60
                    bg-gray-950/80 px-6 py-3 backdrop-blur-sm">
          <div class="flex items-center gap-2.5">
            <lucide-icon name="book-open" [size]="15" class="text-gray-600" />
            <span class="text-sm font-medium text-gray-400">
              @if (store.currentDocument(); as doc) { {{ doc.title }} }
              @else { Document Preview }
            </span>
            @if (store.currentDocument(); as doc) {
              <!-- Goal achievement badge -->
              @if (doc.iterationsUsed > 0) {
                <span [class]="goalBadgeClass(doc)"
                      class="flex items-center gap-1 rounded-full px-2.5 py-0.5 text-[10px] font-medium">
                  @if (doc.goalAchieved) {
                    <lucide-icon name="check-circle-2" [size]="10" />
                    Goal achieved · {{ doc.goalAchievementPct }}%
                  } @else {
                    <lucide-icon name="alert-circle" [size]="10" />
                    Partial · {{ doc.goalAchievementPct }}% · {{ doc.iterationsUsed }} passes
                  }
                </span>
              }
              @if (doc.wasRefined) {
                <span class="text-[9px] text-amber-400">✎ Refined</span>
              }
              <!-- Fact-checked provenance: live model + passed the goal/faithfulness judge -->
              @if (doc.factChecked) {
                <span class="flex items-center gap-1 rounded-full bg-emerald-500/10 px-2.5 py-0.5 text-[10px] font-medium text-emerald-400"
                      title="Produced by a live model and passed the goal/faithfulness judge.">
                  <lucide-icon name="shield-check" [size]="10" />
                  Fact-checked
                </span>
              } @else {
                <span class="flex items-center gap-1 rounded-full bg-amber-500/10 px-2.5 py-0.5 text-[10px] font-medium text-amber-400"
                      title="Offline/heuristic or single-pass output — not verified against the goal/faithfulness judge.">
                  <lucide-icon name="shield-alert" [size]="10" />
                  Unverified
                </span>
              }
            }
          </div>

          @if (store.currentDocument()) {
            <div class="flex items-center gap-2">
              <button (click)="onReset()"
                class="flex h-9 items-center gap-1.5 rounded-lg border border-gray-700/60
                       px-3 text-[11px] font-medium text-gray-500 transition-colors
                       hover:border-gray-600 hover:text-gray-300">
                <lucide-icon name="rotate-ccw" [size]="12" />
                Reset
              </button>
              <button (click)="onRegenerate()"
                [disabled]="store.isGeneratingDocument() || !store.documentSource()"
                class="flex h-9 items-center gap-1.5 rounded-lg border border-amber-500/30
                       bg-amber-500/10 px-3 text-[11px] font-medium text-amber-400
                       transition-all hover:border-amber-500/50 hover:bg-amber-500/15
                       disabled:cursor-not-allowed disabled:opacity-40">
                <lucide-icon name="refresh-cw" [size]="12" />
                Regenerate
              </button>
              <button (click)="onCopyMarkdown()" [class]="copyBtnClass()"
                [disabled]="copyState() !== 'idle'">
                @if (copyState() === 'copied') {
                  <lucide-icon name="check" [size]="13" />
                  <span>Copied!</span>
                } @else {
                  <lucide-icon name="copy" [size]="13" />
                  <span>Copy Markdown</span>
                }
              </button>
              <button (click)="onDownload()"
                class="flex h-9 items-center gap-1.5 rounded-lg border border-gray-700/60
                       bg-gray-800/60 px-3.5 text-[11px] font-medium text-gray-300
                       transition-all hover:border-gray-600 hover:bg-gray-800">
                <lucide-icon name="download" [size]="13" />
                .txt
              </button>
              <button (click)="onDownloadPdf()" [disabled]="exporting() !== null"
                class="flex h-9 items-center gap-1.5 rounded-lg border border-gray-700/60
                       bg-gray-800/60 px-3.5 text-[11px] font-medium text-gray-300
                       transition-all hover:border-gray-600 hover:bg-gray-800
                       disabled:cursor-not-allowed disabled:opacity-40">
                @if (exporting() === 'pdf') {
                  <lucide-icon name="loader-2" [size]="13" class="animate-spin" />
                } @else {
                  <lucide-icon name="file-text" [size]="13" />
                }
                PDF
              </button>
              <button (click)="onDownloadDocx()" [disabled]="exporting() !== null"
                class="flex h-9 items-center gap-1.5 rounded-lg border border-gray-700/60
                       bg-gray-800/60 px-3.5 text-[11px] font-medium text-gray-300
                       transition-all hover:border-gray-600 hover:bg-gray-800
                       disabled:cursor-not-allowed disabled:opacity-40">
                @if (exporting() === 'docx') {
                  <lucide-icon name="loader-2" [size]="13" class="animate-spin" />
                } @else {
                  <lucide-icon name="file-text" [size]="13" />
                }
                Word
              </button>
            </div>
          }
        </div>

        <!-- Document area — relative so absolute overlays anchor here -->
        <div class="relative flex min-h-0 flex-1 flex-col overflow-y-auto">
          @if (store.isGeneratingDocument() && !isPatchMode()) {
            <!-- Full overlay only for complete generation — not for targeted patches -->
            <div class="absolute inset-0 flex flex-col items-center justify-center gap-6">
              <div class="relative flex h-16 w-16 items-center justify-center">
                <div class="absolute inset-0 animate-ping rounded-full bg-indigo-500/20"></div>
                <div class="relative flex h-12 w-12 items-center justify-center
                            rounded-full bg-indigo-500/15 ring-1 ring-indigo-500/30">
                  <lucide-icon name="sparkles" [size]="22" class="text-indigo-400" />
                </div>
              </div>
              <div class="flex flex-col items-center gap-2 text-center">
                <p class="text-sm font-semibold text-gray-200">{{ iterationLabelFull() }}</p>
                <p class="max-w-sm text-xs leading-relaxed text-gray-500">
                  Working toward: {{ effectiveGoal().slice(0, 100) }}{{ effectiveGoal().length > 100 ? '…' : '' }}
                </p>
              </div>
            </div>

          } @else {
            @if (store.currentDocument(); as doc) {
            <div class="px-10 py-10">

              <!-- Post-document critic (advisory): domain / opportunity-fidelity / faithfulness -->
              <div class="mb-4 flex items-center justify-end gap-2">
                @if (store.documentFreshness()?.fresh === false) {
                  <span class="flex items-center gap-1.5 rounded-full border border-amber-500/30 bg-amber-500/10 px-2.5 py-1 text-[10px] font-medium text-amber-300"
                        [title]="store.documentFreshness()?.detail || ''">
                    <lucide-icon name="alert-circle" [size]="11" /> Stale — blueprint changed
                  </span>
                }
                <button (click)="onReview()" [disabled]="store.isReviewingDocument()"
                  class="flex items-center gap-2 rounded-lg border border-gray-700/60 bg-gray-900/60 px-3 py-1.5
                         text-[11px] font-medium text-gray-300 transition-all hover:border-indigo-500/40
                         hover:text-indigo-200 disabled:cursor-not-allowed disabled:opacity-40">
                  @if (store.isReviewingDocument()) {
                    <lucide-icon name="loader-2" [size]="12" class="animate-spin" /> <span>Reviewing…</span>
                  } @else {
                    <lucide-icon name="shield-check" [size]="12" /> <span>Review (domain &amp; faithfulness)</span>
                  }
                </button>
              </div>
              @if (store.documentReview(); as rv) {
                <div class="mb-6 rounded-xl border p-4"
                     [class]="rv.findings.length ? 'border-amber-500/20 bg-amber-500/5' : 'border-emerald-500/20 bg-emerald-500/5'">
                  <div class="mb-2 flex items-center gap-2">
                    <span class="text-[11px] font-semibold" [class]="rv.findings.length ? 'text-amber-300' : 'text-emerald-300'">Document review</span>
                    <span class="rounded-full px-2 py-0.5 text-[10px] font-medium"
                          [class]="rv.reviewScore >= 70 ? 'bg-emerald-500/15 text-emerald-300'
                                 : rv.reviewScore >= 40 ? 'bg-amber-500/15 text-amber-300'
                                 : 'bg-red-500/15 text-red-300'">{{ rv.reviewScore }}%</span>
                    <span class="text-[10px] text-gray-500">{{ rv.modelUsed }}</span>
                    <button (click)="store.documentReview.set(null)"
                            class="ml-auto text-[11px] text-gray-500 hover:text-gray-300">dismiss</button>
                  </div>
                  <p class="mb-2 text-[11px] leading-relaxed text-gray-400">{{ rv.verdict }}</p>
                  @for (f of rv.findings; track $index) {
                    <div class="mb-1.5 rounded-lg border border-gray-800/60 bg-gray-900/50 px-3 py-2">
                      <div class="mb-0.5 flex items-center gap-2">
                        <span class="rounded px-1.5 py-0.5 text-[9px] font-semibold uppercase tracking-wide"
                              [class]="f.severity === 'high' ? 'bg-red-500/15 text-red-300'
                                     : f.severity === 'medium' ? 'bg-amber-500/15 text-amber-300'
                                     : 'bg-gray-700/40 text-gray-400'">{{ f.severity }}</span>
                        <span class="text-[10px] uppercase tracking-wide text-indigo-300">{{ f.axis }}</span>
                      </div>
                      <div class="text-[11px] text-gray-300">{{ f.detail }}</div>
                      @if (f.suggestedFix) { <div class="mt-1 text-[10px] italic text-gray-500">Fix: {{ f.suggestedFix }}</div> }
                    </div>
                  } @empty {
                    <p class="text-[11px] text-emerald-300/80">On-domain and faithful — no issues found.</p>
                  }
                </div>
              }

              <!-- Patch-mode progress banner — non-blocking, document stays readable -->
              @if (store.isGeneratingDocument() && isPatchMode()) {
                <div class="mb-5 flex items-center gap-2.5 rounded-xl border border-amber-500/20
                            bg-amber-500/5 px-4 py-2.5">
                  <lucide-icon name="loader-2" [size]="13" class="animate-spin shrink-0 text-amber-400" />
                  <div class="flex flex-col">
                    <span class="text-[11px] font-medium text-amber-400">{{ iterationLabelFull() }}</span>
                    <span class="text-[10px] text-gray-500">Document is visible — new sections will appear when complete</span>
                  </div>
                </div>
              }

              <!-- Criteria scorecard -->
              @if (doc.iterationsUsed > 0 && (doc.passedCriteria.length || doc.failedCriteria.length)) {
                <div class="mb-6 rounded-xl border"
                     [class]="doc.goalAchieved
                              ? 'border-emerald-500/20 bg-emerald-500/5'
                              : 'border-amber-500/20 bg-amber-500/5'">
                  <button
                    (click)="showScorecard.set(!showScorecard())"
                    class="flex w-full items-center justify-between px-4 py-2.5">
                    <span class="text-[11px] font-semibold"
                          [class]="doc.goalAchieved ? 'text-emerald-400' : 'text-amber-400'">
                      Criteria Scorecard
                    </span>
                    <lucide-icon [name]="showScorecard() ? 'chevron-up' : 'chevron-down'"
                                 [size]="13" class="text-gray-500" />
                  </button>
                  @if (showScorecard()) {
                    <div class="border-t px-4 pb-3 pt-2"
                         [class]="doc.goalAchieved ? 'border-emerald-500/10' : 'border-amber-500/10'">
                      @for (c of scoredCriteria(doc); track c.id) {
                        @if (c.passed) {
                          <div class="flex items-start gap-2 py-1.5">
                            <lucide-icon name="check" [size]="12" class="mt-0.5 shrink-0 text-emerald-400" />
                            <span class="text-[11px] leading-snug text-gray-200">{{ c.text }}</span>
                          </div>
                        } @else {
                          <div class="relative flex items-center gap-2 py-1.5"
                               (mouseenter)="hoveredCriterion.set(c.id)"
                               (mouseleave)="hoveredCriterion.set(null)">
                            <lucide-icon name="x" [size]="12" class="shrink-0 text-red-400" />
                            <span class="flex-1 text-[11px] leading-snug"
                                  [class]="c.reason ? 'cursor-help text-gray-300 underline decoration-dashed decoration-red-400/50 underline-offset-2' : 'text-gray-400'">
                              {{ c.text }}
                            </span>
                            @if (hoveredCriterion() === c.id && c.reason) {
                              <div class="absolute left-6 top-full z-50 mt-1 w-80
                                          rounded-xl border border-red-500/20 bg-gray-900
                                          px-4 py-3 shadow-xl">
                                <p class="mb-1 text-[10px] font-semibold uppercase tracking-wider text-red-400">
                                  Why it failed
                                </p>
                                <p class="text-[11px] leading-relaxed text-gray-300">{{ c.reason }}</p>
                              </div>
                            }
                            <button (click)="onFixCriterion(c.id)"
                                    [disabled]="store.isGeneratingDocument()"
                                    class="flex shrink-0 items-center gap-1 rounded-md border
                                           border-amber-500/30 bg-amber-500/5 px-1.5 py-0.5
                                           text-[10px] font-medium text-amber-400 transition-all
                                           hover:border-amber-500/50 hover:bg-amber-500/10
                                           disabled:cursor-not-allowed disabled:opacity-40">
                              @if (fixingCriterion() === c.id) {
                                <lucide-icon name="loader-2" [size]="10" class="animate-spin" />
                              } @else {
                                <lucide-icon name="wand" [size]="10" />
                              }
                              Fix
                            </button>
                          </div>
                        }
                      }
                      @if (openCriteriaCount(doc) > 0 && !store.isGeneratingDocument()) {
                        <div class="mt-3 border-t border-amber-500/10 pt-3">
                          <button (click)="onPatchFailed()"
                            class="flex w-full items-center justify-center gap-1.5 rounded-lg
                                   border border-amber-500/30 bg-amber-500/5 px-3 py-2
                                   text-[11px] font-medium text-amber-400 transition-all
                                   hover:border-amber-500/50 hover:bg-amber-500/10">
                            <lucide-icon name="wand" [size]="12" />
                            Fix missing ({{ openCriteriaCount(doc) }})
                          </button>
                        </div>
                      }
                      @if (doc.effectiveGoal) {
                        <details class="mt-2">
                          <summary class="cursor-pointer text-[10px] text-gray-500 hover:text-gray-400">
                            Active goal ▸
                          </summary>
                          <p class="mt-1 rounded-lg border border-gray-800 bg-gray-900/60
                                    px-3 py-2 text-[10px] leading-relaxed text-gray-400">
                            {{ doc.effectiveGoal }}
                          </p>
                        </details>
                      }
                    </div>
                  }
                </div>
              }

              <!-- Document header card -->
              <div class="mb-8 rounded-2xl border border-gray-800/60 bg-gray-900/60 p-6">
                <div class="mb-3 flex items-center gap-2">
                  <span class="text-[10px] text-gray-400">
                    {{ doc.createdAt | date:'MMM d, y · HH:mm' }}
                  </span>
                </div>
                <h1 class="text-2xl font-bold tracking-tight text-white">{{ doc.title }}</h1>
              </div>
              <article class="md-content" [innerHTML]="doc.content | markdown" appMermaid></article>
              <div class="mt-16 border-t border-gray-800/40 pt-6 text-center">
                <p class="text-[10px] text-gray-500">
                  Generated by Meridian Studio · {{ doc.modelUsed }}
                </p>
              </div>
            </div>

          } @else if (!store.isGeneratingDocument()) {
            <div class="absolute inset-0 flex flex-col items-center justify-center gap-5">
              <div class="relative">
                <div class="flex h-20 w-20 items-center justify-center rounded-2xl
                            border border-gray-800 bg-gray-900">
                  <lucide-icon name="file-text" [size]="32" class="text-gray-700" />
                </div>
                <div class="absolute -bottom-1 -right-1 flex h-7 w-7 items-center
                            justify-center rounded-lg bg-indigo-600/80 shadow-lg">
                  <lucide-icon name="sparkles" [size]="13" class="text-white" />
                </div>
              </div>
              <div class="flex flex-col items-center gap-2 text-center">
                <p class="text-sm font-medium text-gray-400">No document generated yet</p>
                <p class="max-w-sm text-xs leading-relaxed text-gray-400">
                  Select a template, review the mission panel, then click
                  <span class="text-indigo-400">Generate</span>.
                </p>
              </div>
            </div>
          }
        }
        </div>
      </div>

      <!-- ══ Configure Mission Modal ════════════════════════════════ -->
      @if (isConfigureModalOpen()) {
        <!--
          The overlay IS the flex container. It covers the whole document-studio
          area (absolute inset-0) and uses flexbox to center the panel.
          The panel gets its height from flex stretching (align-items: stretch
          is the default), giving flex children a definite px height so that
          flex-1 + overflow-y-auto on the body always works.
        -->
        <div class="absolute inset-0 z-50 flex items-stretch justify-center
                    bg-black/70 p-6 backdrop-blur-sm"
             (click)="isConfigureModalOpen.set(false)">

          <!-- Panel — stop propagation so clicks inside don't close -->
          <div class="flex w-full max-w-2xl flex-col
                      rounded-2xl border border-gray-800/80 bg-gray-950 shadow-2xl"
               (click)="$event.stopPropagation()">

            <!-- Modal header -->
            <div class="flex shrink-0 items-center justify-between border-b border-gray-800/60 px-6 py-4">
              <div class="flex items-center gap-2.5">
                <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-indigo-500/15">
                  <lucide-icon name="settings-2" [size]="15" class="text-indigo-400" />
                </div>
                <div>
                  <h2 class="text-sm font-semibold text-white">Configure Mission</h2>
                  <p class="text-[11px] text-gray-500">Writing context for {{ activeTemplate()?.label }}</p>
                </div>
              </div>
              <button (click)="isConfigureModalOpen.set(false)"
                class="flex h-7 w-7 items-center justify-center rounded-lg text-gray-500
                       transition-colors hover:bg-gray-800 hover:text-gray-300">
                <lucide-icon name="x" [size]="15" />
              </button>
            </div>

            <!-- Modal body (scrollable) -->
          <div class="flex-1 min-h-0 space-y-6 overflow-y-auto px-6 py-5">

            <!-- Writing as -->
            <div class="flex items-start gap-3 rounded-xl border border-gray-800/60
                        bg-gray-900/60 px-4 py-3">
              <lucide-icon name="user" [size]="14" class="mt-0.5 shrink-0 text-indigo-400" />
              <div>
                <span class="text-[10px] font-semibold uppercase tracking-wider text-gray-500">Writing as</span>
                @if (isSuggestionsLoading()) {
                  <div class="mt-1 h-4 w-48 animate-pulse rounded bg-gray-800"></div>
                } @else {
                  <p class="mt-0.5 text-sm font-medium text-indigo-300">
                    {{ suggestions()?.persona ?? '—' }}
                  </p>
                  @if (suggestions()?.secondaryAudience) {
                    <p class="text-[11px] text-gray-500">Also for: {{ suggestions()!.secondaryAudience }}</p>
                  }
                }
              </div>
            </div>

            <!-- Tone -->
            <div class="border-t border-gray-800/50 pt-5">
              <div class="mb-3 flex items-center justify-between">
                <span class="text-[11px] font-semibold uppercase tracking-widest text-gray-400">Tone</span>
                @if (refinedTone()) {
                  <span class="text-[9px] text-amber-400">✎ Refined</span>
                }
              </div>
              @if (isSuggestionsLoading()) {
                <div class="grid grid-cols-2 gap-2">
                  @for (_ of [1,2,3,4]; track $index) {
                    <div class="h-12 animate-pulse rounded-lg bg-gray-800/60"></div>
                  }
                </div>
              } @else if (suggestions()?.toneOptions?.length) {
                @if (!refinedTone()) {
                  <div class="grid grid-cols-2 gap-2">
                    @for (tone of suggestions()!.toneOptions; track $index) {
                      <button
                        (click)="selectTone($index)"
                        [class]="toneCardClass($index)"
                        [title]="tone.fullPhrase">
                        {{ tone.label }}
                      </button>
                    }
                  </div>
                  <button (click)="startRefineTone()"
                    class="mt-2 flex w-full items-center justify-center gap-1.5 rounded-lg
                           border border-dashed border-indigo-500/30 py-1.5 text-[10px] font-medium
                           text-indigo-400 transition-colors hover:border-indigo-500/50 hover:text-indigo-300">
                    <lucide-icon name="pencil" [size]="10" />
                    Copy & refine selected tone
                  </button>
                } @else {
                  <textarea
                    [ngModel]="refinedTone()!"
                    (ngModelChange)="refinedTone.set($event)"
                    rows="2"
                    class="w-full resize-none rounded-lg border border-amber-500/30
                           bg-amber-500/5 px-3 py-2 text-[11px] text-amber-200
                           focus:border-amber-500/50 focus:outline-none focus:ring-1
                           focus:ring-amber-500/20 placeholder-gray-600"
                    placeholder="Describe the tone..."></textarea>
                  <button (click)="refinedTone.set(null)"
                    class="mt-1 text-[10px] text-gray-500 hover:text-gray-400">
                    ↺ Back to options
                  </button>
                }
              }
            </div>

            <!-- Goal -->
            <div class="border-t border-gray-800/50 pt-5">
              <div class="mb-3 flex items-center justify-between">
                <span class="text-[11px] font-semibold uppercase tracking-widest text-gray-400">Goal</span>
                @if (refinedGoal()) {
                  <span class="text-[9px] text-amber-400">✎ Refined</span>
                }
              </div>
              @if (isSuggestionsLoading()) {
                <div class="space-y-2">
                  @for (_ of [1,2,3]; track $index) {
                    <div class="h-14 animate-pulse rounded-lg bg-gray-800/60"></div>
                  }
                </div>
              } @else if (!refinedGoal() && suggestions()?.goalOptions?.length) {
                <div class="space-y-2">
                  @for (goal of suggestions()!.goalOptions; track $index) {
                    <div [class]="goalCardClass($index)" class="rounded-lg border px-4 py-3 transition-all">
                      <button class="w-full text-left" (click)="selectGoal($index)">
                        <span class="text-[12px] font-semibold"
                              [class]="selectedGoalIndex() === $index ? 'text-indigo-300' : 'text-gray-300'">
                          {{ goal.label }}
                        </span>
                        <p class="mt-1 text-[11px] leading-relaxed text-gray-500">
                          {{ goal.text }}
                        </p>
                      </button>
                      <button (click)="startRefineGoal($index)"
                        class="mt-2 flex items-center gap-1 text-[10px] font-medium
                               text-indigo-400 transition-colors hover:text-indigo-300">
                        <lucide-icon name="copy" [size]="9" />
                        Copy & refine
                      </button>
                    </div>
                  }
                </div>
              } @else if (refinedGoal() !== null) {
                <textarea
                  [ngModel]="refinedGoal()!"
                  (ngModelChange)="refinedGoal.set($event)"
                  rows="4"
                  class="w-full resize-none rounded-lg border border-amber-500/30
                         bg-amber-500/5 px-3 py-2 text-[11px] text-amber-200
                         focus:border-amber-500/50 focus:outline-none focus:ring-1
                         focus:ring-amber-500/20 placeholder-gray-600"
                  placeholder="Describe the goal for this document..."></textarea>
                <button (click)="refinedGoal.set(null)"
                  class="mt-1 text-[10px] text-gray-500 hover:text-gray-400">
                  ↺ Back to options
                </button>
              }
            </div>

            <!-- Success Criteria -->
            <div class="border-t border-gray-800/50 pt-5">
              <div class="mb-3 flex items-center justify-between">
                <span class="text-[11px] font-semibold uppercase tracking-widest text-gray-400">
                  Success Criteria
                </span>
                @if (refinedCriteria()) {
                  <span class="text-[9px] text-amber-400">✎ Refined</span>
                }
              </div>
              <p class="mb-2 text-[10px] leading-relaxed text-gray-600">
                Judge evaluates each criterion as pass/fail per iteration.
              </p>
              @if (isSuggestionsLoading()) {
                <div class="space-y-2">
                  @for (_ of [1,2]; track $index) {
                    <div class="h-16 animate-pulse rounded-lg bg-gray-800/60"></div>
                  }
                </div>
              } @else if (!refinedCriteria() && suggestions()?.criteriaOptions?.length) {
                <div class="space-y-2">
                  @for (crit of suggestions()!.criteriaOptions; track $index) {
                    <div [class]="criteriaCardClass($index)" class="rounded-lg border px-4 py-3 transition-all">
                      <button class="w-full text-left" (click)="selectCriteria($index)">
                        <span class="text-[12px] font-semibold"
                              [class]="selectedCriteriaIndex() === $index ? 'text-indigo-300' : 'text-gray-300'">
                          {{ crit.label }}
                        </span>
                        <ul class="mt-1.5 space-y-1">
                          @for (c of crit.criteria; track $index) {
                            <li class="flex items-start gap-2 text-[11px] leading-relaxed text-gray-500">
                              <span class="mt-0.5 shrink-0 text-indigo-500">·</span>
                              <span>{{ c }}</span>
                            </li>
                          }
                        </ul>
                      </button>
                      <button (click)="startRefineCriteria($index)"
                        class="mt-2 flex items-center gap-1 text-[10px] font-medium
                               text-indigo-400 transition-colors hover:text-indigo-300">
                        <lucide-icon name="copy" [size]="9" />
                        Copy & refine
                      </button>
                    </div>
                  }
                </div>
              } @else if (refinedCriteria() !== null) {
                <div class="space-y-1.5">
                  @for (c of refinedCriteria()!; track $index) {
                    <div class="flex items-center gap-2">
                      <input type="text"
                        [ngModel]="c"
                        (ngModelChange)="updateCriterion($index, $event)"
                        class="flex-1 h-8 rounded-lg border border-amber-500/20
                               bg-amber-500/5 px-2.5 text-[11px] text-amber-200
                               focus:border-amber-500/40 focus:outline-none" />
                      <button (click)="removeCriterion($index)"
                        class="text-gray-600 transition-colors hover:text-red-400">
                        <lucide-icon name="x" [size]="12" />
                      </button>
                    </div>
                  }
                  <button (click)="addCriterion()"
                    class="flex w-full items-center justify-center gap-1 rounded-lg border
                           border-dashed border-gray-700/60 py-1.5 text-[10px] text-gray-500
                           transition-colors hover:border-gray-600 hover:text-gray-400">
                    <lucide-icon name="plus" [size]="10" />
                    Add criterion
                  </button>
                  <button (click)="refinedCriteria.set(null)"
                    class="mt-0.5 text-[10px] text-gray-500 hover:text-gray-400">
                    ↺ Back to options
                  </button>
                </div>
              }
            </div>

            <!-- Regenerate suggestions -->
            <button (click)="loadSuggestions()"
              [disabled]="isSuggestionsLoading()"
              class="mt-2 flex w-full items-center justify-center gap-1.5 rounded-lg
                     border border-gray-700/40 py-2.5 text-[10px] text-gray-400
                     transition-colors hover:border-gray-600 hover:text-gray-300
                     disabled:cursor-not-allowed disabled:opacity-40">
              <lucide-icon name="refresh-cw" [size]="10"
                           [class]="isSuggestionsLoading() ? 'animate-spin' : ''" />
              Regenerate suggestions
            </button>

          </div>

          <!-- Modal footer -->
          <div class="flex shrink-0 items-center justify-end gap-2 border-t border-gray-800/60 px-6 py-4">
            <button (click)="isConfigureModalOpen.set(false)"
              class="flex h-9 items-center gap-1.5 rounded-lg bg-indigo-600 px-5
                     text-sm font-medium text-white transition-all hover:bg-indigo-500">
              <lucide-icon name="check" [size]="14" />
              Done
            </button>
          </div>
          </div>
        </div>
      }

    </div>

    <!-- Copy feedback toast -->
    @if (copyState() !== 'idle') {
      <div class="fixed bottom-6 right-6 z-50 flex items-center gap-2.5 rounded-xl
                  border px-4 py-3 text-sm font-medium shadow-xl backdrop-blur-sm"
           [class]="copyState() === 'copied'
                    ? 'border-emerald-500/30 bg-emerald-950/90 text-emerald-300'
                    : 'border-red-500/30 bg-red-950/90 text-red-300'">
        @if (copyState() === 'copied') {
          <lucide-icon name="check" [size]="15" class="text-emerald-400" />
          Markdown copied to clipboard
        } @else {
          <lucide-icon name="alert-circle" [size]="15" class="text-red-400" />
          Clipboard access denied
        }
      </div>
    }
  `,
})
export class DocumentStudioComponent implements OnInit {
  protected readonly store = inject(WorkspaceStoreService);
  private readonly exportService = inject(DocumentExportService);
  protected readonly templates = TEMPLATES;
  protected readonly selectedTemplate = signal<DocumentTemplateType>('executive-summary');
  protected readonly docTitle = signal('');
  protected readonly copyState = signal<CopyState>('idle');
  /** Which export is in flight (diagram rasterizing can take a moment), or null. */
  protected readonly exporting = signal<'pdf' | 'docx' | null>(null);
  protected readonly showScorecard = signal(true);
  protected readonly isConfigureModalOpen = signal(false);
  protected readonly hoveredCriterion  = signal<string | null>(null);
  protected readonly fixingCriterion   = signal<string | null>(null);
  /** True while a targeted patch (Fix missing / Fix criterion) is running.
   *  Keeps the document visible behind a thin banner instead of showing
   *  the full-screen generation overlay. */
  protected readonly isPatchMode       = signal(false);
  /** Per-operation grounding toggle — runs live web grounding for fact-heavy templates. */
  protected readonly groundLive        = signal(true);

  // ── Mission suggestion state ──────────────────────────────────────────────
  protected readonly suggestions       = signal<MissionSuggestions | null>(null);
  protected readonly isSuggestionsLoading = signal(false);
  protected readonly selectedToneIndex     = signal(0);
  protected readonly selectedGoalIndex     = signal(0);
  protected readonly selectedCriteriaIndex = signal(0);
  protected readonly refinedTone       = signal<string | null>(null);
  protected readonly refinedGoal       = signal<string | null>(null);
  protected readonly refinedCriteria   = signal<string[] | null>(null);

  // ── Iteration progress ────────────────────────────────────────────────────
  private _iterationCount = signal(0);

  protected readonly iterationLabel = computed(() => {
    const n = this._iterationCount();
    if (n === 0) return 'Drafting…';
    if (n === 1) return 'Evaluating…';
    if (n <= 4) return `Refining (${n}/5)…`;
    return 'Final pass…';
  });

  // Full label used in the centre-panel loading animation
  protected readonly iterationLabelFull = computed(() => {
    const n = this._iterationCount();
    if (n === 0) return 'Drafting document...';
    if (n === 1) return 'Evaluating criteria...';
    if (n <= 4) return `Refining — pass ${n} of 5...`;
    return 'Final refinement pass...';
  });

  // ── Computed effective mission values ─────────────────────────────────────
  protected readonly effectiveTone = computed(() =>
    this.refinedTone() ??
    this.suggestions()?.toneOptions?.[this.selectedToneIndex()]?.fullPhrase ?? '');

  protected readonly effectiveGoal = computed(() =>
    this.refinedGoal() ??
    this.suggestions()?.goalOptions?.[this.selectedGoalIndex()]?.text ?? '');

  protected readonly effectiveCriteria = computed(() =>
    this.refinedCriteria() ??
    this.suggestions()?.criteriaOptions?.[this.selectedCriteriaIndex()]?.criteria ?? []);

  protected readonly wasRefined = computed(() =>
    this.refinedTone() !== null ||
    this.refinedGoal() !== null ||
    this.refinedCriteria() !== null);

  protected readonly activeTemplate = computed(() =>
    TEMPLATES.find(t => t.type === this.selectedTemplate()));

  private _lastFreshnessCheckId = '';

  constructor() {
    // Track SSE model-status events to update iteration label
    effect(() => {
      const status = this.store.currentModelStatus();
      if (!status || !this.store.isGeneratingDocument()) return;
      if (status.operation === 'generate-document' && status.type === 'attempting') {
        this._iterationCount.update(n => n + 1);
      }
    });

    // When a settled document is shown, check whether its grounding blueprint has since been revised.
    effect(() => {
      const doc = this.store.currentDocument();
      if (this.store.isGeneratingDocument()) return;
      const key = doc?.id ?? '';
      if (key === this._lastFreshnessCheckId) return;   // avoid re-firing on unrelated signal changes
      this._lastFreshnessCheckId = key;
      if (doc) this.store.checkDocumentFreshness(doc);
    });
  }

  ngOnInit(): void {
    // Launched from a recommended deliverable: pre-select the template + title and auto-run.
    const pending = this.store.pendingDocumentTemplate();
    if (pending) {
      this.store.pendingDocumentTemplate.set(null);
      this.selectedTemplate.set(pending.templateType as DocumentTemplateType);
      if (pending.title) this.docTitle.set(pending.title);
      // Pass onGenerate as a callback so it fires AFTER suggestions are loaded into
      // the signal — without this, effectiveTone/Goal/Criteria all resolve to empty
      // because suggestions() is still null when onGenerate() reads them.
      this.loadSuggestions(() => this.onGenerate());
      return;
    }
    this.loadSuggestions();
  }

  protected onTemplateChange(type: DocumentTemplateType): void {
    this.selectedTemplate.set(type);
    this.selectedToneIndex.set(0);
    this.selectedGoalIndex.set(0);
    this.selectedCriteriaIndex.set(0);
    this.refinedTone.set(null);
    this.refinedGoal.set(null);
    this.refinedCriteria.set(null);
    this.loadSuggestions();
  }

  protected loadSuggestions(afterLoad?: () => void): void {
    const src = this.store.documentSource();
    const req: MissionSuggestionsRequest = {
      templateType:     this.selectedTemplate(),
      domain:           src?.domain || this.store.activeDomain() || '',
      solutionType:     this.store.effectiveSolutionType() ?? '',
      blueprintContext: src?.context?.slice(0, 800) ?? '',
    };
    this.isSuggestionsLoading.set(true);
    this.store.getMissionSuggestions(req).subscribe({
      next: s => {
        this.suggestions.set(s);
        this.selectedToneIndex.set(0);
        this.selectedGoalIndex.set(0);
        this.selectedCriteriaIndex.set(0);
      },
      complete: () => { this.isSuggestionsLoading.set(false); afterLoad?.(); },
      error:    () => { this.isSuggestionsLoading.set(false); afterLoad?.(); },
    });
  }

  // ── Selection methods ─────────────────────────────────────────────────────

  protected selectTone(index: number): void {
    this.selectedToneIndex.set(index);
    this.refinedTone.set(null);
  }

  protected selectGoal(index: number): void {
    this.selectedGoalIndex.set(index);
    this.refinedGoal.set(null);
  }

  protected selectCriteria(index: number): void {
    this.selectedCriteriaIndex.set(index);
    this.refinedCriteria.set(null);
  }

  protected startRefineTone(): void {
    const current = this.suggestions()?.toneOptions?.[this.selectedToneIndex()]?.fullPhrase ?? '';
    this.refinedTone.set(current);
  }

  protected startRefineGoal(index: number): void {
    const current = this.suggestions()?.goalOptions?.[index]?.text ?? '';
    this.selectedGoalIndex.set(index);
    this.refinedGoal.set(current);
  }

  protected startRefineCriteria(index: number): void {
    const current = [...(this.suggestions()?.criteriaOptions?.[index]?.criteria ?? [])];
    this.selectedCriteriaIndex.set(index);
    this.refinedCriteria.set(current);
  }

  protected updateCriterion(index: number, value: string): void {
    const list = [...(this.refinedCriteria() ?? [])];
    list[index] = value;
    this.refinedCriteria.set(list);
  }

  protected addCriterion(): void {
    this.refinedCriteria.set([...(this.refinedCriteria() ?? []), '']);
  }

  protected removeCriterion(index: number): void {
    const list = [...(this.refinedCriteria() ?? [])];
    if (list.length <= 1) return;
    list.splice(index, 1);
    this.refinedCriteria.set(list);
  }

  // ── Generation ────────────────────────────────────────────────────────────

  protected onGenerate(
    isRerun = false,
    patchCriteria?: string[],
    existingContent?: string,
    knownFailureReasons?: Record<string, string>,
  ): void {
    const src = this.store.documentSource();
    if (!src) return;

    const tone      = this.effectiveTone();
    const goal      = this.effectiveGoal();
    const criteria  = this.effectiveCriteria();
    const refined   = this.wasRefined();

    // Record selection as training signal (fire-and-forget)
    if (goal) {
      this.store.recordMissionSelection({
        templateType:     this.selectedTemplate(),
        domain:           src.domain,
        solutionType:     this.store.effectiveSolutionType(),
        selectedTone:     tone,
        selectedGoal:     goal,
        selectedCriteria: criteria,
        wasRefined:       refined,
      });
    }

    this._iterationCount.set(0);
    this.showScorecard.set(true);

    // For market-analysis, forward the real researched competitor data so the LLM
    // cannot invent competitor names, strengths, or gaps.
    const isMarketAnalysis = this.selectedTemplate() === 'market-analysis';
    const competitorInsights = isMarketAnalysis
      ? (this.store.currentResearchData()?.competitorInsights ?? [])
      : undefined;

    const titleRaw = this.docTitle().trim() || `${src.title} — ${this.activeTemplate()?.label}`;
    const request: GenerateDocumentRequest = {
      // Ground from a blueprint or a use-case assessment, whichever is active.
      ...(src.kind === 'assessment' ? { assessmentId: src.id } : { blueprintId: src.id }),
      title:              titleRaw.length > 198 ? titleRaw.slice(0, 198).trimEnd() + '…' : titleRaw,
      templateType:       this.selectedTemplate(),
      domain:             src.domain,
      subDomain:          this.store.activeSubdomain()?.subdomain || undefined,
      solutionType:       this.store.effectiveSolutionType() || undefined,
      blueprintContext:   src.context?.slice(0, 1500),
      isRerun,
      selectedTone:       tone || undefined,
      selectedGoal:       goal || undefined,
      selectedCriteria:   patchCriteria?.length ? patchCriteria : (criteria.length ? criteria : undefined),
      competitorInsights: competitorInsights?.length ? competitorInsights : undefined,
      wasRefined:         refined,
      groundInLiveResearch: this.groundLive(),
      existingContent:    existingContent ?? undefined,
      knownFailureReasons: knownFailureReasons ?? undefined,
    };

    this.store.generateDocument(request).subscribe();
  }

  protected onRegenerate(): void {
    this.onGenerate(true);
  }

  protected onReview(): void {
    if (this.store.currentDocument()) this.store.reviewDocument().subscribe({ error: () => {} });
  }

  /** Flattens the scorecard: structured criteria (with ids) when present, else the legacy
   *  string arrays (id = text). Drives the template and the by-id fix calls. */
  protected scoredCriteria(doc: CorporateDocument): { id: string; text: string; passed: boolean; reason?: string | null }[] {
    const s = doc.structured;
    if (s?.criteria?.length) {
      return s.criteria.map(c => ({ id: c.id, text: c.text, passed: c.passed, reason: c.failureReason }));
    }
    return [
      ...(doc.passedCriteria ?? []).map(t => ({ id: t, text: t, passed: true, reason: null })),
      ...(doc.failedCriteria ?? []).map(t => ({ id: t, text: t, passed: false, reason: doc.failureReasons?.[t] })),
    ];
  }

  protected openCriteriaCount(doc: CorporateDocument): number {
    return this.scoredCriteria(doc).filter(c => !c.passed).length;
  }

  protected onPatchFailed(): void {
    const doc = this.store.currentDocument();
    if (!doc) return;

    // Structured path: fix each open criterion sequentially (deterministic, by-id).
    if (doc.structured?.criteria?.length) {
      const open = doc.structured.criteria.filter(c => !c.passed).map(c => c.id);
      if (!open.length) return;
      this.showScorecard.set(true);
      this.isPatchMode.set(true);
      const next = (i: number): void => {
        const cur = this.store.currentDocument();
        if (i >= open.length || !cur?.structured) { this.fixingCriterion.set(null); this.isPatchMode.set(false); return; }
        this.fixingCriterion.set(open[i]);
        this.store.fixCriterion(cur.structured, open[i]).subscribe({
          complete: () => next(i + 1),
          error:    () => { this.fixingCriterion.set(null); this.isPatchMode.set(false); },
        });
      };
      next(0);
      return;
    }

    // Legacy fallback (pre-structured docs).
    if (!doc.failedCriteria?.length) return;
    const src = this.store.documentSource();
    if (!src) return;
    this._iterationCount.set(0);
    this.showScorecard.set(true);
    this.isPatchMode.set(true);
    const request: GenerateDocumentRequest = {
      ...(src.kind === 'assessment' ? { assessmentId: src.id } : { blueprintId: src.id }),
      title:            doc.title,
      templateType:     doc.templateType as DocumentTemplateType,
      domain:           src.domain,
      subDomain:        this.store.activeSubdomain()?.subdomain || undefined,
      solutionType:     this.store.effectiveSolutionType() || undefined,
      blueprintContext: src.context?.slice(0, 1500),
      selectedGoal:     doc.effectiveGoal || undefined,
      selectedCriteria: doc.effectiveCriteria?.length ? doc.effectiveCriteria : undefined,
      wasRefined:       doc.wasRefined,
      existingContent:  doc.content,
      knownFailureReasons: doc.failureReasons,
    };
    this.store.generateDocument(request).subscribe({
      complete: () => this.isPatchMode.set(false),
      error:    () => this.isPatchMode.set(false),
    });
  }

  protected onFixCriterion(criterionId: string): void {
    const doc = this.store.currentDocument();
    if (!doc) return;

    // Structured by-id fix (Phase A) — deterministic, no duplicates / no scorecard churn.
    if (doc.structured?.criteria?.length) {
      this.fixingCriterion.set(criterionId);
      this.showScorecard.set(true);
      this.isPatchMode.set(true);
      this.store.fixCriterion(doc.structured, criterionId).subscribe({
        complete: () => { this.fixingCriterion.set(null); this.isPatchMode.set(false); },
        error:    () => { this.fixingCriterion.set(null); this.isPatchMode.set(false); },
      });
      return;
    }

    // Legacy fallback (pre-structured docs): criterionId is the criterion text.
    const src = this.store.documentSource();
    if (!src) return;
    this.fixingCriterion.set(criterionId);
    this._iterationCount.set(0);
    this.showScorecard.set(true);
    this.isPatchMode.set(true);
    const reason = doc.failureReasons?.[criterionId];
    const request: GenerateDocumentRequest = {
      ...(src.kind === 'assessment' ? { assessmentId: src.id } : { blueprintId: src.id }),
      title:            doc.title,
      templateType:     doc.templateType as DocumentTemplateType,
      domain:           src.domain,
      subDomain:        this.store.activeSubdomain()?.subdomain || undefined,
      solutionType:     this.store.effectiveSolutionType() || undefined,
      blueprintContext: src.context?.slice(0, 1500),
      selectedGoal:     doc.effectiveGoal || 'Produce a comprehensive document that meets all specified criteria.',
      selectedCriteria: doc.effectiveCriteria?.length ? doc.effectiveCriteria : undefined,
      wasRefined:       doc.wasRefined,
      existingContent:  doc.content,
      knownFailureReasons: { [criterionId]: reason ?? 'Add a section addressing this criterion.' },
    };
    this.store.generateDocument(request).subscribe({
      complete: () => { this.fixingCriterion.set(null); this.isPatchMode.set(false); },
      error:    () => { this.fixingCriterion.set(null); this.isPatchMode.set(false); },
    });
  }

  protected onReset(): void {
    this.store.currentDocument.set(null);
  }

  protected async onCopyMarkdown(): Promise<void> {
    const doc = this.store.currentDocument();
    if (!doc) return;
    try {
      await navigator.clipboard.writeText(doc.content);
      this.copyState.set('copied');
    } catch {
      this.copyState.set('error');
    } finally {
      setTimeout(() => this.copyState.set('idle'), 2400);
    }
  }

  protected onDownload(): void {
    const doc = this.store.currentDocument();
    if (!doc) return;
    const blob = new Blob([doc.content], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${doc.title.toLowerCase().replace(/\s+/g, '-')}.txt`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }

  protected async onDownloadPdf(): Promise<void> {
    const doc = this.store.currentDocument();
    if (!doc || this.exporting()) return;
    this.exporting.set('pdf');
    try {
      await this.exportService.downloadPdf(doc);
    } catch (err) {
      console.error('[DocumentStudio] PDF export failed', err);
    } finally {
      this.exporting.set(null);
    }
  }

  protected async onDownloadDocx(): Promise<void> {
    const doc = this.store.currentDocument();
    if (!doc || this.exporting()) return;
    this.exporting.set('docx');
    try {
      await this.exportService.downloadDocx(doc);
    } catch (err) {
      console.error('[DocumentStudio] DOCX export failed', err);
    } finally {
      this.exporting.set(null);
    }
  }

  // ── Style helpers ─────────────────────────────────────────────────────────

  protected toneCardClass(index: number): string {
    const base = 'rounded-lg border p-2 text-left text-[10px] font-medium transition-all';
    return index === this.selectedToneIndex() && !this.refinedTone()
      ? `${base} border-indigo-500/40 bg-indigo-500/10 text-indigo-300`
      : `${base} border-gray-800/60 text-gray-500 hover:border-gray-700 hover:text-gray-400`;
  }

  protected goalCardClass(index: number): string {
    return index === this.selectedGoalIndex() && !this.refinedGoal()
      ? 'border-indigo-500/30 bg-indigo-500/5'
      : 'border-gray-800/40 hover:border-gray-700/60';
  }

  protected criteriaCardClass(index: number): string {
    return index === this.selectedCriteriaIndex() && !this.refinedCriteria()
      ? 'border-indigo-500/30 bg-indigo-500/5'
      : 'border-gray-800/40 hover:border-gray-700/60';
  }

  protected templateCardClass(tpl: TemplateOption): string {
    const base =
      'flex w-full items-center gap-2.5 rounded-lg border p-2.5 text-left ' +
      'transition-all duration-150 focus:outline-none';
    return this.selectedTemplate() === tpl.type
      ? `${base} ${tpl.accentLight}`
      : `${base} border-transparent hover:border-gray-700/40 hover:bg-gray-800/20`;
  }

  protected hasReason(doc: { failureReasons?: Record<string, string> }, criterion: string): boolean {
    return !!doc.failureReasons?.[criterion];
  }

  protected goalBadgeClass(doc: { goalAchieved: boolean }): string {
    return doc.goalAchieved
      ? 'border border-emerald-500/30 bg-emerald-500/10 text-emerald-300'
      : 'border border-amber-500/30 bg-amber-500/10 text-amber-300';
  }

  protected copyBtnClass(): string {
    const base =
      'flex h-9 items-center gap-1.5 rounded-lg border px-3.5 text-[11px] font-medium ' +
      'transition-all duration-150 focus:outline-none disabled:cursor-not-allowed';
    return this.copyState() === 'copied'
      ? `${base} border-emerald-500/30 bg-emerald-500/10 text-emerald-400`
      : `${base} border-gray-700/60 bg-gray-800/40 text-gray-300 hover:border-gray-600`;
  }

  protected templateBadgeClass(type: string): string {
    const tpl = TEMPLATES.find(t => t.type === type);
    return tpl ? `${tpl.accentLight} ${tpl.accent}` : 'border-gray-700 text-gray-500';
  }

  protected templateLabel(type: string): string {
    return TEMPLATES.find(t => t.type === type)?.label ?? type;
  }
}
