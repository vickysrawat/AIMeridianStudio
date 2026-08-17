import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { MarkdownPipe } from '../../core/pipes/markdown.pipe';
import { MermaidDirective } from '../../core/directives/mermaid.directive';
import { WorkspaceStoreService } from '../../core/services/workspace-store.service';
import { AssessmentRequest, RecommendedDocument, UseCaseReadiness, ImprovementSuggestion, Assessment, ExportFormat } from '../../core/models/interfaces';
import { BlueprintChatDrawerComponent } from '../architectural-blueprinter/blueprint-chat-drawer.component';

const EXAMPLE_SCENARIO =
  'Our customer landing page runs on Azure and is hitting capacity limits during peak ' +
  'traffic for AI/ML and data-intensive workloads. Assess a multi-cloud strategy across ' +
  'AWS and GCP — feasibility, effort, governance impact, and a phased roadmap.';

@Component({
  selector: 'app-use-case-analyzer',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule, MarkdownPipe, MermaidDirective, BlueprintChatDrawerComponent],
  template: `
    <div class="flex h-full w-full min-h-0 flex-col overflow-y-auto bg-gray-950">

      <!-- Header -->
      <div class="shrink-0 border-b border-gray-800/60 bg-gray-950/80 px-8 py-5 backdrop-blur-sm">
        <div class="mx-auto flex max-w-5xl items-start gap-3">
          <div class="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-indigo-500/15">
            <lucide-icon name="lightbulb" [size]="18" class="text-indigo-400" />
          </div>
          <div>
            <h1 class="text-base font-semibold tracking-tight text-white">Use-Case Assessment</h1>
            <p class="mt-0.5 text-xs text-gray-400">
              Describe a scenario or a structured brief — get a concise, decision-ready assessment
              and the deep deliverables to generate next.
            </p>
          </div>
        </div>
      </div>

      <div class="mx-auto w-full max-w-5xl flex-1 px-8 py-6">

        <!-- Intake -->
        <div class="rounded-2xl border border-gray-800/60 bg-gray-900/40 p-5">
          <!-- Mode toggle -->
          <div class="mb-4 inline-flex rounded-lg border border-gray-800 p-0.5">
            <button (click)="mode.set('quick')" [class]="modeBtn('quick')">Quick scenario</button>
            <button (click)="mode.set('brief')" [class]="modeBtn('brief')">Structured brief</button>
          </div>

          @if (mode() === 'quick') {
            <textarea [ngModel]="scenario()" (ngModelChange)="scenario.set($event)"
              [disabled]="store.isGeneratingAssessment()" rows="5" [placeholder]="example"
              class="w-full resize-none rounded-xl border border-gray-800 bg-gray-950/60 px-4 py-3
                     text-sm leading-relaxed text-gray-200 placeholder-gray-600
                     focus:border-indigo-500/40 focus:outline-none focus:ring-1 focus:ring-indigo-500/20"></textarea>
            <div class="mt-1.5 flex items-center justify-between">
              <button (click)="useExample()" type="button"
                class="text-[11px] text-gray-500 underline transition-colors hover:text-indigo-400">
                Use the example scenario
              </button>
              <span class="text-[10px] tabular-nums transition-colors"
                    [class]="wordCountClass(wordCount(scenario()), SCENARIO_MAX_WORDS)">
                {{ wordCount(scenario()) | number }} / {{ SCENARIO_MAX_WORDS | number }} words
              </span>
            </div>
          } @else {
            <div class="grid gap-3 md:grid-cols-2">
              @for (f of briefFields; track f.key) {
                <div [class]="f.wide ? 'md:col-span-2' : ''">
                  <label class="mb-1 block text-[11px] font-semibold uppercase tracking-wider text-gray-500">{{ f.label }}</label>
                  <textarea [ngModel]="brief()[f.key]" (ngModelChange)="setBrief(f.key, $event)"
                    [disabled]="store.isGeneratingAssessment()" [rows]="f.rows" [placeholder]="f.placeholder"
                    class="w-full resize-none rounded-lg border border-gray-800 bg-gray-950/60 px-3 py-2
                           text-[13px] leading-relaxed text-gray-200 placeholder-gray-600
                           focus:border-indigo-500/40 focus:outline-none focus:ring-1 focus:ring-indigo-500/20"></textarea>
                  <div class="mt-0.5 flex justify-end">
                    <span class="text-[10px] tabular-nums transition-colors"
                          [class]="wordCountClass(wordCount(brief()[f.key]), f.maxWords)">
                      {{ wordCount(brief()[f.key]) | number }} / {{ f.maxWords | number }} words
                    </span>
                  </div>
                </div>
              }
            </div>
          }

          <div class="mt-4 flex items-center justify-between">
            <label class="flex cursor-pointer select-none items-center gap-2 text-[11px] text-gray-400"
                   title="Run live web search first and ground the assessment in real, cited sources.">
              <input type="checkbox" [checked]="groundLive()"
                     (change)="groundLive.set($any($event.target).checked)"
                     class="h-3.5 w-3.5 rounded border-gray-600 bg-gray-800 text-indigo-500" />
              <lucide-icon name="globe" [size]="12" class="text-gray-500" />
              Ground in live research
            </label>
            <div class="flex items-center gap-2">
              <button (click)="analyze()" [disabled]="!canGenerate() || store.isAnalyzingUseCase()"
                title="Check whether your brief is complete enough for a strong assessment"
                class="flex h-10 items-center justify-center gap-2 rounded-xl border border-indigo-500/30
                       px-4 text-xs font-medium text-indigo-300 transition-all
                       hover:border-indigo-500/50 hover:text-indigo-200
                       disabled:cursor-not-allowed disabled:opacity-40">
                @if (store.isAnalyzingUseCase()) {
                  <lucide-icon name="loader-2" [size]="14" class="animate-spin" /><span>Analyzing…</span>
                } @else {
                  <lucide-icon name="list-checks" [size]="14" /><span>Analyze</span>
                }
              </button>
              <button (click)="generate()" [disabled]="!canGenerate()"
                class="flex h-10 items-center justify-center gap-2 rounded-xl
                       bg-gradient-to-r from-indigo-600 to-violet-600 px-5 text-xs font-medium
                       text-white shadow-md shadow-indigo-600/20 transition-all
                       hover:from-indigo-500 hover:to-violet-500
                       disabled:cursor-not-allowed disabled:opacity-40">
                @if (store.isGeneratingAssessment()) {
                  <lucide-icon name="loader-2" [size]="14" class="animate-spin" /><span>Assessing…</span>
                } @else {
                  <lucide-icon name="sparkles" [size]="14" /><span>Run Assessment</span>
                }
              </button>
            </div>
          </div>
        </div>

        <!-- Readiness panel -->
        @if (!store.isGeneratingAssessment() && store.useCaseReadiness(); as r) {
          <div class="mt-4 rounded-2xl border p-5" [class]="readinessPanelClass(r.readinessScore)">
            <div class="mb-3 flex items-start justify-between gap-3">
              <div class="flex items-center gap-2.5">
                <span [class]="scoreBadgeClass(r.readinessScore)">{{ r.readinessScore }}</span>
                <div>
                  <div class="text-[11px] font-semibold uppercase tracking-wider text-gray-400">Brief readiness</div>
                  <p class="mt-0.5 text-[13px] leading-snug text-gray-200">{{ r.verdict }}</p>
                </div>
              </div>
              <button (click)="store.useCaseReadiness.set(null)" title="Dismiss"
                class="shrink-0 text-gray-600 transition-colors hover:text-gray-300">
                <lucide-icon name="x" [size]="14" />
              </button>
            </div>

            @if (r.fields?.length) {
              <div class="mb-3 flex flex-wrap gap-1.5">
                @for (f of r.fields; track f.field) {
                  <span [class]="fieldChipClass(f.status)" [title]="f.comment">
                    {{ fieldLabel(f.field) }} · {{ f.status }}
                  </span>
                }
              </div>
            }

            @if (r.clarifyingQuestions?.length) {
              <div class="mb-3">
                <div class="mb-1.5 text-[10px] font-semibold uppercase tracking-wider text-gray-500">Answer these to sharpen the brief</div>
                <ul class="space-y-1">
                  @for (q of r.clarifyingQuestions; track $index) {
                    <li class="flex items-start gap-1.5 text-[12px] leading-snug text-gray-300">
                      <lucide-icon name="message-circle" [size]="12" class="mt-0.5 shrink-0 text-indigo-400" /><span>{{ q }}</span>
                    </li>
                  }
                </ul>
              </div>
            }

            @if (r.suggestions?.length) {
              <div>
                <div class="mb-1.5 text-[10px] font-semibold uppercase tracking-wider text-gray-500">Suggestions</div>
                <div class="space-y-2">
                  @for (s of r.suggestions; track $index) {
                    <div class="flex items-start justify-between gap-3 rounded-xl border border-gray-800/60 bg-gray-950/40 px-3.5 py-2.5">
                      <div class="min-w-0">
                        <div class="flex items-center gap-2">
                          <span class="rounded-full bg-indigo-500/15 px-2 py-0.5 text-[9px] font-semibold uppercase tracking-wide text-indigo-400">{{ fieldLabel(s.field) }}</span>
                          @if (appliedSuggestions().has($index)) {
                            <span class="text-[9px] text-amber-400">✎ Applied</span>
                          }
                        </div>
                        <p class="mt-1 text-[12px] leading-snug text-gray-300">{{ s.suggestion }}</p>
                      </div>
                      @if (s.proposedText) {
                        <button (click)="applySuggestion(s, $index)"
                          class="flex h-7 shrink-0 items-center gap-1 rounded-md border border-amber-500/30 bg-amber-500/5 px-2
                                 text-[10px] font-medium text-amber-400 transition-all hover:border-amber-500/50 hover:bg-amber-500/10">
                          <lucide-icon name="wand" [size]="11" />Apply
                        </button>
                      }
                    </div>
                  }
                </div>
              </div>
            }
          </div>
        }

        <!-- Streaming preview -->
        @if (store.isGeneratingAssessment()) {
          <div class="mt-6 rounded-2xl border border-gray-800/60 bg-gray-900/40 p-5">
            <div class="mb-2 flex items-center gap-2 text-[11px] font-medium text-violet-400">
              <span class="inline-block h-1.5 w-1.5 animate-pulse rounded-full bg-violet-400"></span>
              Producing the assessment…
            </div>
            <pre class="max-h-48 overflow-y-auto whitespace-pre-wrap break-words font-mono
                        text-[11px] leading-relaxed text-gray-500">{{ streamPreview() }}</pre>
          </div>
        }

        <!-- Result -->
        @if (!store.isGeneratingAssessment() && store.currentAssessment(); as a) {
          <div class="mt-6 space-y-5">

            <!-- Executive summary -->
            <div class="rounded-2xl border border-indigo-500/20 bg-indigo-500/[0.04] p-5">
              <div class="mb-2 flex items-center justify-between gap-2">
                <div class="flex items-center gap-2">
                  <lucide-icon name="target" [size]="15" class="text-indigo-400" />
                  <h2 class="text-sm font-semibold text-white">{{ a.title }}</h2>
                </div>
                <div class="flex items-center gap-1.5">
                  <button (click)="openChat('assessment', 'Assessment')"
                    class="flex items-center gap-1 rounded-lg border border-indigo-500/20 px-2.5 py-1
                           text-[10px] text-indigo-500/70 transition-colors hover:border-indigo-500/40 hover:text-indigo-400">
                    <lucide-icon name="message-circle" [size]="11" />Refine
                  </button>
                  <button (click)="store.startWhitePaperFromAssessment()"
                    class="flex items-center gap-1 rounded-lg border border-violet-500/25 px-2.5 py-1
                           text-[10px] text-violet-300/80 transition-colors hover:border-violet-500/50 hover:text-violet-300"
                    title="Generate a market/competitive white paper for this use case">
                    <lucide-icon name="file-text" [size]="11" />White Paper
                  </button>
                  @for (f of exportFormats; track f) {
                    <button (click)="onDownload(a, f)" [disabled]="exporting() !== null"
                      class="flex items-center gap-1 rounded-lg border border-gray-700/60 bg-gray-800/60 px-2.5 py-1
                             text-[10px] text-gray-300 transition-colors hover:bg-gray-800 disabled:opacity-40"
                      [title]="'Download ' + f">
                      @if (exporting() === f) { <lucide-icon name="loader-2" [size]="11" class="animate-spin" /> }
                      @else { <lucide-icon name="download" [size]="11" /> }
                      {{ f === 'markdown' ? 'MD' : (f === 'pdf' ? 'PDF' : 'Word') }}
                    </button>
                  }
                </div>
              </div>
              <p class="text-sm leading-relaxed text-gray-300">{{ a.executiveSummary }}</p>
            </div>

            <!-- Adaptive sections -->
            @for (s of a.sections; track $index) {
              <div class="rounded-2xl border border-gray-800/60 bg-gray-900/40 p-5">
                <h3 class="mb-2 text-sm font-semibold text-white">{{ s.title }}</h3>
                <div class="md-content text-sm" [innerHTML]="s.body | markdown" appMermaid></div>
              </div>
            }

            <!-- Options comparison -->
            @if (a.feasibility; as fa) {
              <div class="rounded-2xl border border-gray-800/60 bg-gray-900/40 p-5">
                <div class="mb-3 flex items-center gap-2">
                  <lucide-icon name="git-compare" [size]="15" class="text-indigo-400" />
                  <h3 class="text-sm font-semibold text-white">Options Comparison</h3>
                </div>
                @if (fa.primaryConcernVerdict) {
                  <p class="mb-3 text-xs leading-relaxed text-gray-400">{{ fa.primaryConcernVerdict }}</p>
                }
                <div [class]="optionsGridClass(fa.options.length)">
                  @for (opt of fa.options; track $index) {
                    <div class="flex flex-col rounded-xl border border-gray-800/60 bg-gray-950/40 p-4">
                      <div class="mb-2 flex items-start justify-between gap-2">
                        <h4 class="text-[13px] font-semibold leading-snug text-white">{{ opt.name }}</h4>
                        <span [class]="verdictBadge(opt.verdict)">{{ opt.verdict }}</span>
                      </div>
                      <div class="mb-3 flex items-center gap-4 text-[11px] text-gray-400">
                        <span class="flex items-center gap-1"><lucide-icon name="gauge" [size]="12" class="text-indigo-400" />{{ opt.score }}/10</span>
                        <span class="flex items-center gap-1"><lucide-icon name="clock" [size]="12" class="text-indigo-400" />{{ opt.effortEstimate }}</span>
                      </div>
                      @if (opt.challenges?.length) {
                        <ul class="mb-2 space-y-1">
                          @for (c of opt.challenges; track $index) {
                            <li class="flex items-start gap-1.5 text-[11px] leading-snug text-gray-400">
                              <lucide-icon name="alert-circle" [size]="11" class="mt-0.5 shrink-0 text-amber-400" /><span>{{ c }}</span>
                            </li>
                          }
                        </ul>
                      }
                      @if (opt.recommendation) {
                        <p class="mt-auto text-[11px] leading-snug text-indigo-300">{{ opt.recommendation }}</p>
                      }
                    </div>
                  }
                </div>
              </div>
            }

            <!-- Recommendations / Risks / Next steps -->
            <div class="grid gap-4 md:grid-cols-3">
              @if (a.recommendations?.length) {
                <div class="rounded-2xl border border-emerald-500/15 bg-gray-900/40 p-5">
                  <h3 class="mb-2 text-[11px] font-semibold uppercase tracking-wider text-emerald-400">Recommendations</h3>
                  <ul class="space-y-1.5">
                    @for (r of a.recommendations; track $index) {
                      <li class="flex items-start gap-1.5 text-[12px] leading-snug text-gray-300">
                        <lucide-icon name="check-circle" [size]="12" class="mt-0.5 shrink-0 text-emerald-400" /><span>{{ r }}</span>
                      </li>
                    }
                  </ul>
                </div>
              }
              @if (a.risks?.length) {
                <div class="rounded-2xl border border-red-500/15 bg-gray-900/40 p-5">
                  <h3 class="mb-2 text-[11px] font-semibold uppercase tracking-wider text-red-400">Risks</h3>
                  <ul class="space-y-1.5">
                    @for (r of a.risks; track $index) {
                      <li class="flex items-start gap-1.5 text-[12px] leading-snug text-gray-300">
                        <lucide-icon name="x-circle" [size]="12" class="mt-0.5 shrink-0 text-red-400" /><span>{{ r }}</span>
                      </li>
                    }
                  </ul>
                </div>
              }
              @if (a.nextSteps?.length) {
                <div class="rounded-2xl border border-indigo-500/15 bg-gray-900/40 p-5">
                  <h3 class="mb-2 text-[11px] font-semibold uppercase tracking-wider text-indigo-400">Next Steps</h3>
                  <ul class="space-y-1.5">
                    @for (r of a.nextSteps; track $index) {
                      <li class="flex items-start gap-1.5 text-[12px] leading-snug text-gray-300">
                        <lucide-icon name="arrow-right" [size]="12" class="mt-0.5 shrink-0 text-indigo-400" /><span>{{ r }}</span>
                      </li>
                    }
                  </ul>
                </div>
              }
            </div>

            <!-- Recommended deliverables -->
            @if (a.recommendedDocuments?.length) {
              <div class="rounded-2xl border border-gray-800/60 bg-gray-900/40 p-5">
                <div class="mb-3 flex items-center gap-2">
                  <lucide-icon name="file-text" [size]="15" class="text-indigo-400" />
                  <h3 class="text-sm font-semibold text-white">Recommended Deliverables</h3>
                  <span class="text-[11px] text-gray-500">— generate the deep documents on demand</span>
                </div>
                <div class="space-y-2.5">
                  @for (rec of a.recommendedDocuments; track $index) {
                    <div class="flex items-center justify-between gap-3 rounded-xl border border-gray-800/60 bg-gray-950/40 px-4 py-3">
                      <div class="min-w-0">
                        <div class="flex items-center gap-2">
                          <span class="truncate text-[13px] font-medium text-gray-200">{{ rec.title }}</span>
                          <span class="shrink-0 rounded-full bg-indigo-500/15 px-2 py-0.5 text-[9px] font-semibold uppercase tracking-wide text-indigo-400">{{ rec.templateType }}</span>
                        </div>
                        <p class="mt-0.5 truncate text-[11px] text-gray-500">{{ rec.rationale }}</p>
                      </div>
                      <button (click)="generateDoc(rec)"
                        class="flex h-8 shrink-0 items-center gap-1.5 rounded-lg bg-gradient-to-r from-indigo-600 to-violet-600
                               px-3 text-[11px] font-medium text-white transition-all hover:from-indigo-500 hover:to-violet-500">
                        <lucide-icon name="sparkles" [size]="12" />Generate
                      </button>
                    </div>
                  }
                </div>
              </div>
            }
          </div>
        }

        <!-- Empty hint -->
        @if (!store.isGeneratingAssessment() && !store.currentAssessment()) {
          <div class="mt-10 flex flex-col items-center gap-3 text-center text-gray-500">
            <lucide-icon name="lightbulb" [size]="30" class="text-gray-700" />
            <p class="max-w-md text-xs leading-relaxed">
              Describe a use case — a migration, a platform decision, a capacity problem, or a
              strategy question — and Meridian produces a concise assessment plus the deliverables
              you can generate next.
            </p>
          </div>
        }
      </div>

      <!-- Chat drawer -->
      @if (chatOpen() && store.currentAssessment(); as a) {
        <app-blueprint-chat-drawer
          [blueprintId]="a.id"
          basePath="assessment"
          [sectionKey]="chatSectionKey()"
          [sectionLabel]="chatSectionLabel()"
          [sectionData]="a"
          (applyPatch)="handleApplyPatch($event)"
          (closed)="chatOpen.set(false)" />
      }
    </div>
  `,
})
export class UseCaseAnalyzerComponent {
  protected readonly store = inject(WorkspaceStoreService);
  protected readonly example = EXAMPLE_SCENARIO;

  // ── Assessment export (server-side markdown → md/pdf/docx) ──────────────────
  protected readonly exportFormats: ExportFormat[] = ['markdown', 'pdf', 'docx'];
  protected readonly exporting = signal<ExportFormat | null>(null);

  protected onDownload(a: Assessment, format: ExportFormat): void {
    const markdown = this.assessmentToMarkdown(a);
    const ext = format === 'markdown' ? 'md' : format;
    const slug = (a.title || 'assessment').toLowerCase().replace(/[^\w]+/g, '-').replace(/^-|-$/g, '').slice(0, 60) || 'assessment';
    this.exporting.set(format);
    this.store.exportMarkdown(a.title || 'Assessment', markdown, format, `${slug}.${ext}`)
      .subscribe({ next: () => this.exporting.set(null), error: () => this.exporting.set(null) });
  }

  private assessmentToMarkdown(a: Assessment): string {
    const lines: string[] = [`# ${a.title}`, ''];
    if (a.domain) lines.push(`*Domain: ${a.domain}*`, '');
    if (a.executiveSummary) lines.push('## Executive Summary', '', a.executiveSummary, '');
    for (const s of a.sections ?? []) lines.push(`## ${s.title}`, '', s.body, '');
    if (a.recommendations?.length) { lines.push('## Recommendations', ''); a.recommendations.forEach(r => lines.push(`- ${r}`)); lines.push(''); }
    if (a.risks?.length)          { lines.push('## Risks', '');          a.risks.forEach(r => lines.push(`- ${r}`));          lines.push(''); }
    if (a.nextSteps?.length)      { lines.push('## Next Steps', '');     a.nextSteps.forEach(r => lines.push(`- ${r}`));      lines.push(''); }
    return lines.join('\n');
  }

  protected readonly mode = signal<'quick' | 'brief'>('quick');
  protected readonly scenario = signal('');
  protected readonly brief = signal<Record<string, string>>({
    useCase: '', context: '', problemStatement: '', objective: '', scopeOfWork: '', expectedOutcome: '',
  });

  protected readonly SCENARIO_MAX_WORDS = 3000;

  protected readonly briefFields = [
    { key: 'useCase',          label: 'Use Case',          rows: 1, wide: true,  maxWords: 1200, placeholder: 'e.g. Multi-Cloud Strategy & Execution Assessment' },
    { key: 'context',          label: 'Context',           rows: 3, wide: true,  maxWords: 1200, placeholder: 'Current environment, estate maturity, governance…' },
    { key: 'problemStatement', label: 'Problem Statement', rows: 3, wide: true,  maxWords: 1200, placeholder: 'What is not working and why it matters…' },
    { key: 'objective',        label: 'Objective',         rows: 3, wide: false, maxWords: 1200, placeholder: 'What the assessment must achieve…' },
    { key: 'scopeOfWork',      label: 'Scope of Work',     rows: 3, wide: false, maxWords: 2000, placeholder: 'Workstreams the engagement covers…' },
    { key: 'expectedOutcome',  label: 'Expected Outcome',  rows: 3, wide: true,  maxWords: 1200, placeholder: 'The concrete deliverables you expect…' },
  ];

  protected readonly chatOpen         = signal(false);
  protected readonly chatSectionKey   = signal('assessment');
  protected readonly chatSectionLabel = signal('Assessment');
  /** When on, the assessment fetches live web evidence first and grounds the LLM in it. */
  protected readonly groundLive       = signal(true);
  /** Indices of readiness suggestions the user has applied (drives the ✎ Applied badge). */
  protected readonly appliedSuggestions = signal<Set<number>>(new Set());

  protected readonly streamPreview = computed(() => {
    const t = this.store.assessmentStreamText();
    return t.length > 1600 ? t.slice(-1600) : t;
  });

  protected wordCount(text: string | null | undefined): number {
    if (!text?.trim()) return 0;
    return text.trim().split(/\s+/).length;
  }

  protected wordCountClass(count: number, max: number): string {
    const ratio = count / max;
    if (ratio >= 1.0) return 'text-red-400';
    if (ratio >= 0.85) return 'text-amber-400';
    return 'text-gray-500';
  }

  protected modeBtn(m: 'quick' | 'brief'): string {
    const base = 'rounded-md px-3 py-1.5 text-[11px] font-medium transition-colors';
    return this.mode() === m ? `${base} bg-indigo-600 text-white` : `${base} text-gray-400 hover:text-gray-200`;
  }

  protected setBrief(key: string, value: string): void {
    this.brief.update(b => ({ ...b, [key]: value }));
  }

  protected useExample(): void { this.scenario.set(EXAMPLE_SCENARIO); }

  protected canGenerate(): boolean {
    if (this.store.isGeneratingAssessment()) return false;
    if (this.mode() === 'quick') return this.scenario().trim().length > 0;
    const b = this.brief();
    return [b['useCase'], b['problemStatement'], b['objective'], b['expectedOutcome']]
      .some(v => v.trim().length > 0);
  }

  /** Build the AssessmentRequest from the current inputs — shared by Analyze and Run Assessment. */
  private buildRequest(): AssessmentRequest {
    return this.mode() === 'quick'
      ? { useCaseScenario: this.scenario().trim(), groundInLiveResearch: this.groundLive() }
      : {
          useCase:          this.brief()['useCase'].trim() || undefined,
          context:          this.brief()['context'].trim() || undefined,
          problemStatement: this.brief()['problemStatement'].trim() || undefined,
          objective:        this.brief()['objective'].trim() || undefined,
          scopeOfWork:      this.brief()['scopeOfWork'].trim() || undefined,
          expectedOutcome:  this.brief()['expectedOutcome'].trim() || undefined,
          groundInLiveResearch: this.groundLive(),
        };
  }

  protected generate(): void {
    if (!this.canGenerate()) return;
    this.store.useCaseReadiness.set(null);   // advice is stale once the assessment runs
    this.store.generateAssessment(this.buildRequest());
  }

  /** Advisory: critique the brief for completeness and surface the readiness panel. */
  protected analyze(): void {
    if (!this.canGenerate()) return;
    this.appliedSuggestions.set(new Set());
    this.store.analyzeUseCase(this.buildRequest()).subscribe({ error: () => {} });
  }

  /** One-click apply: drop a suggestion's scaffold into the input field it targets. */
  protected applySuggestion(s: ImprovementSuggestion, index: number): void {
    const text = s.proposedText ?? '';
    if (!text) return;
    if (s.field === 'useCaseScenario') {
      this.mode.set('quick');
      this.scenario.set(text);
    } else {
      this.mode.set('brief');
      this.setBrief(s.field, text);
    }
    this.appliedSuggestions.update(set => new Set(set).add(index));
  }

  protected generateDoc(rec: RecommendedDocument): void {
    this.store.generateDocumentFromAssessment(rec);
  }

  protected openChat(key: string, label: string): void {
    this.chatSectionKey.set(key);
    this.chatSectionLabel.set(label);
    this.chatOpen.set(true);
  }

  protected handleApplyPatch(event: { sectionKey: string; patch: Record<string, unknown> }): void {
    const a = this.store.currentAssessment();
    if (!a) return;
    this.store.patchAssessment(a.id, event.patch as Partial<typeof a>).subscribe();
    this.chatOpen.set(false);
  }

  protected optionsGridClass(n: number): string {
    const base = 'grid gap-4';
    if (n > 2)   return `${base} md:grid-cols-2 xl:grid-cols-3`;
    if (n === 2) return `${base} md:grid-cols-2`;
    return base;
  }

  protected verdictBadge(verdict: string): string {
    const base = 'shrink-0 rounded-full px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide';
    const v = (verdict || '').toLowerCase();
    if (v.includes('not')) return `${base} bg-red-500/15 text-red-400`;
    if (v.includes('partial')) return `${base} bg-amber-500/15 text-amber-400`;
    if (v.includes('effort')) return `${base} bg-blue-500/15 text-blue-400`;
    return `${base} bg-emerald-500/15 text-emerald-400`;
  }

  // ── Readiness panel styling ────────────────────────────────────────────────
  protected readinessPanelClass(score: number): string {
    if (score < 50) return 'border-red-500/20 bg-red-500/[0.04]';
    if (score < 80) return 'border-amber-500/20 bg-amber-500/[0.04]';
    return 'border-emerald-500/20 bg-emerald-500/[0.04]';
  }

  protected scoreBadgeClass(score: number): string {
    const base = 'flex h-10 w-10 shrink-0 items-center justify-center rounded-xl text-sm font-bold tabular-nums';
    if (score < 50) return `${base} bg-red-500/15 text-red-400`;
    if (score < 80) return `${base} bg-amber-500/15 text-amber-400`;
    return `${base} bg-emerald-500/15 text-emerald-400`;
  }

  protected fieldChipClass(status: string): string {
    const base = 'rounded-full px-2 py-0.5 text-[10px] font-medium';
    if (status === 'missing') return `${base} bg-red-500/15 text-red-400`;
    if (status === 'weak')    return `${base} bg-amber-500/15 text-amber-400`;
    return `${base} bg-emerald-500/15 text-emerald-400`;
  }

  protected fieldLabel(field: string): string {
    if (field === 'useCaseScenario') return 'Scenario';
    return this.briefFields.find(f => f.key === field)?.label ?? field;
  }
}
