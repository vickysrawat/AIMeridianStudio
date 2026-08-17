import {
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { WorkspaceStoreService } from '../../core/services/workspace-store.service';
import { ExecuteTaskRequest, GenerateProjectRequest } from '../../core/models/interfaces';

interface LangDef {
  id: string;
  label: string;
  ext: string;
  accentText: string;
  accentBg: string;
  accentBorder: string;
}

const LANGUAGES: LangDef[] = [
  { id: 'csharp',     label: 'C#',         ext: 'cs',   accentText: 'text-violet-300', accentBg: 'bg-violet-500/15', accentBorder: 'border-violet-500/35' },
  { id: 'typescript', label: 'TypeScript',  ext: 'ts',   accentText: 'text-blue-300',   accentBg: 'bg-blue-500/15',   accentBorder: 'border-blue-500/35'   },
  { id: 'python',     label: 'Python',      ext: 'py',   accentText: 'text-yellow-300', accentBg: 'bg-yellow-500/15', accentBorder: 'border-yellow-500/35' },
  { id: 'java',       label: 'Java',        ext: 'java', accentText: 'text-orange-300', accentBg: 'bg-orange-500/15', accentBorder: 'border-orange-500/35' },
  { id: 'go',         label: 'Go',          ext: 'go',   accentText: 'text-cyan-300',   accentBg: 'bg-cyan-500/15',   accentBorder: 'border-cyan-500/35'   },
];

@Component({
  selector: 'app-execution-pipeline',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule],
  template: `
    <section class="flex h-full min-h-0 flex-col">

      <!-- ── Header ─────────────────────────────────────────────── -->
      <div class="shrink-0 border-b border-gray-800/60 px-6 py-4">
        <div class="flex items-center justify-between gap-4">
          <div class="flex items-center gap-2.5">
            <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-violet-500/15">
              <lucide-icon name="code-2" [size]="16" class="text-violet-400" />
            </div>
            <div>
              <h2 class="text-sm font-semibold tracking-tight text-white">Implementation Studio</h2>
              <p class="text-[11px] text-gray-400">AI-generated code scaffolds for your integration steps</p>
            </div>
          </div>

          <div class="flex items-center gap-2 text-[11px]">
            @if (store.isExecutingTask()) {
              <span class="flex items-center gap-1.5 font-medium text-blue-400">
                <span class="h-1.5 w-1.5 animate-pulse rounded-full bg-blue-400"></span>
                Generating…
              </span>
            } @else if (store.currentTaskSpec()) {
              <span class="flex items-center gap-1.5 font-medium text-emerald-400">
                <span class="h-1.5 w-1.5 rounded-full bg-emerald-400"></span>
                {{ store.currentTaskSpec()!.status }}
              </span>
            } @else {
              <span class="text-gray-600">Awaiting task</span>
            }
          </div>
        </div>

        <!-- Progress bar -->
        @if (integrationSteps().length > 0) {
          <div class="mt-3 flex items-center gap-3">
            <div class="relative h-1.5 flex-1 overflow-hidden rounded-full bg-gray-800">
              <div class="absolute inset-y-0 left-0 rounded-full
                          bg-gradient-to-r from-violet-600 to-indigo-400 transition-all duration-700"
                   [style.width]="progressPercent() + '%'"></div>
            </div>
            <span class="shrink-0 text-right text-xs font-bold tabular-nums text-violet-400">
              {{ completedStepIndices().length }}/{{ integrationSteps().length }} scaffolded
            </span>
          </div>
        }
      </div>

      <!-- ── Controls ────────────────────────────────────────────── -->
      <div class="flex shrink-0 flex-wrap items-center gap-2 border-b border-gray-800/40
                  bg-gray-950/40 px-5 py-3">

        <!-- Custom task input -->
        <input
          type="text"
          [ngModel]="customTaskName()"
          (ngModelChange)="customTaskName.set($event)"
          (keyup.enter)="onRunCustom()"
          placeholder="Describe a custom implementation task…"
          class="h-9 flex-1 rounded-lg border border-gray-800 bg-gray-900/60 px-3
                 text-xs text-gray-300 placeholder-gray-500
                 focus:border-violet-500/40 focus:outline-none focus:ring-1 focus:ring-violet-500/20" />

        <!-- Language pills -->
        <div class="flex items-center gap-0.5 rounded-xl border border-gray-800 bg-gray-900/60 p-0.5">
          @for (lang of languages; track lang.id) {
            <button (click)="selectedLanguage.set(lang.id)" [class]="langTabClass(lang)">
              {{ lang.label }}
            </button>
          }
        </div>

        <!-- Generate Code / Re-generate -->
        <button
          (click)="onGenerateCode()"
          [disabled]="store.isExecutingTask() || isGeneratingAll() || integrationSteps().length === 0"
          class="flex h-9 items-center gap-1.5 rounded-lg
                 bg-gradient-to-r from-violet-600 to-indigo-600 px-4 text-xs font-medium
                 text-white shadow-md shadow-violet-600/20 transition-all
                 hover:from-violet-500 hover:to-indigo-500
                 focus:outline-none focus:ring-2 focus:ring-violet-500/40
                 disabled:cursor-not-allowed disabled:opacity-40">
          @if (store.isExecutingTask()) {
            <lucide-icon name="loader-2" [size]="13" class="animate-spin" />
            <span>Generating…</span>
          } @else if (isCompleted(selectedStepIndex())) {
            <lucide-icon name="refresh-cw" [size]="13" />
            <span>Re-generate</span>
          } @else {
            <lucide-icon name="code-2" [size]="13" />
            <span>Generate Code</span>
          }
        </button>

        <!-- Run Custom -->
        <button
          (click)="onRunCustom()"
          [disabled]="!customTaskName().trim() || store.isExecutingTask() || isGeneratingAll()"
          class="flex h-9 items-center gap-1.5 rounded-lg border border-gray-700/60
                 bg-gray-800/40 px-3 text-xs font-medium text-gray-300 transition-all
                 hover:border-gray-600 hover:bg-gray-800
                 disabled:cursor-not-allowed disabled:opacity-40">
          <lucide-icon name="play" [size]="13" class="fill-current" />
          <span>Run Custom</span>
        </button>
      </div>

      <!-- ── Main Split ──────────────────────────────────────────── -->
      <div class="flex min-h-0 flex-1 overflow-hidden">

        <!-- Left: Steps (click-to-select) -->
        <div class="flex w-72 shrink-0 flex-col border-r border-gray-800/60 xl:w-80">
          <div class="flex items-center gap-2 border-b border-gray-800/40 px-4 py-2.5">
            <lucide-icon name="list-checks" [size]="13" class="text-gray-600" />
            <span class="text-xs font-semibold uppercase tracking-wider text-gray-400">
              Integration Steps
            </span>
            @if (integrationSteps().length > 0) {
              <span class="ml-auto text-[10px] text-gray-400">click to select</span>
            }
          </div>

          <!-- Generate complete e2e button -->
          @if (store.selectedSolution()) {
            <div class="shrink-0 border-b border-gray-800/40 px-3 py-2.5">
              <button (click)="onGenerateComplete()"
                [disabled]="store.isExecutingTask() || isGeneratingAll()"
                class="flex w-full items-center justify-center gap-1.5 rounded-lg border
                       border-violet-500/25 bg-violet-500/8 px-3 py-2 text-[11px] font-medium
                       text-violet-300 transition-all
                       hover:border-violet-500/40 hover:bg-violet-500/15
                       disabled:cursor-not-allowed disabled:opacity-40">
                @if (isGeneratingAll()) {
                  <lucide-icon name="loader-2" [size]="12" class="animate-spin" />
                  <span>Step {{ selectedStepIndex() + 1 }}/{{ integrationSteps().length }}…</span>
                } @else {
                  <lucide-icon name="zap" [size]="12" />
                  <span>Generate Complete Implementation</span>
                }
              </button>
              <p class="mt-1.5 text-center text-[9px] text-gray-400">
                Full end-to-end scaffold for this solution
              </p>

              <!-- Download as complete project zip -->
              <button (click)="onDownloadProject()"
                [disabled]="store.isExecutingTask() || isGeneratingAll() || isDownloadingProject()"
                class="mt-1.5 flex w-full items-center justify-center gap-1.5 rounded-lg border
                       border-emerald-500/25 bg-emerald-500/8 px-3 py-2 text-[11px] font-medium
                       text-emerald-300 transition-all
                       hover:border-emerald-500/40 hover:bg-emerald-500/15
                       disabled:cursor-not-allowed disabled:opacity-40">
                @if (isDownloadingProject()) {
                  <lucide-icon name="loader-2" [size]="12" class="animate-spin" />
                  <span>Packaging…</span>
                } @else {
                  <lucide-icon name="package" [size]="12" />
                  <span>Download Project (.zip)</span>
                }
              </button>
              <p class="mt-1 text-center text-[9px] text-gray-400">
                Complete app — sln/csproj, layers, migration, tests
              </p>
            </div>
          }

          <div class="relative flex min-h-0 flex-1 flex-col overflow-y-auto p-3">
            @if (!store.selectedSolution()) {
              <div class="absolute inset-0 flex flex-col items-center justify-center gap-3 text-center">
                <lucide-icon name="mouse-pointer-click" [size]="28" class="text-gray-800" />
                <p class="text-xs leading-relaxed text-gray-400">
                  Select a solution in the Research tab — its integration steps will appear here.
                </p>
              </div>
            } @else if (integrationSteps().length === 0) {
              <p class="py-16 text-center text-xs leading-relaxed text-gray-400">
                No structured steps found.<br>Use the custom task input above.
              </p>
            } @else {
              <div class="flex flex-col gap-1.5">
                @for (step of integrationSteps(); track $index; let idx = $index) {
                  <div [class]="stepRowClass(idx)"
                       (click)="selectStep(idx)"
                       role="button"
                       tabindex="0"
                       (keydown.enter)="selectStep(idx)">
                    <div [class]="stepNumClass(idx)">
                      @if (isRunning(idx)) {
                        <lucide-icon name="loader-2" [size]="11" class="animate-spin text-blue-400" />
                      } @else if (isCompleted(idx) && isSelected(idx)) {
                        <lucide-icon name="code-2" [size]="11" class="text-violet-400" />
                      } @else if (isCompleted(idx)) {
                        <lucide-icon name="check" [size]="11" class="text-emerald-400" />
                      } @else {
                        <span class="text-[10px] font-bold">{{ idx + 1 }}</span>
                      }
                    </div>
                    <p class="flex-1 text-[11px] leading-snug"
                       [class]="isRunning(idx)                        ? 'text-blue-300' :
                                isCompleted(idx) && isSelected(idx)   ? 'text-violet-300 line-through' :
                                isCompleted(idx)                       ? 'text-gray-500 line-through' :
                                isSelected(idx)                        ? 'text-violet-300' : 'text-gray-400'">
                      {{ step }}
                    </p>
                  </div>
                }
              </div>
            }
          </div>

          @if (store.selectedSolution(); as sol) {
            <div class="border-t border-gray-800/60 p-3">
              <p class="mb-1 text-[9px] font-semibold uppercase tracking-wider text-gray-400">
                Active Solution
              </p>
              <p class="line-clamp-2 text-[11px] font-medium text-violet-400">{{ sol.name }}</p>
            </div>
          }
        </div>

        <!-- Right: Code Output (full height — logs removed) -->
        <div class="flex min-h-0 flex-1 flex-col">

          <!-- Code Output -->
          <div class="flex min-h-0 flex-1 flex-col bg-gray-950">

            <!-- Code header bar -->
            <div class="flex shrink-0 items-center justify-between border-b border-gray-800/60
                        bg-gray-900/80 px-4 py-2">
              <div class="flex items-center gap-3">
                <div class="flex items-center gap-1.5">
                  <span class="h-2.5 w-2.5 rounded-full bg-red-500/80"></span>
                  <span class="h-2.5 w-2.5 rounded-full bg-amber-400/80"></span>
                  <span class="h-2.5 w-2.5 rounded-full bg-emerald-500/80"></span>
                </div>
                <div class="flex items-center gap-1.5 text-[11px] text-gray-400">
                  <lucide-icon name="file-code-2" [size]="12" />
                  <span>implementation.{{ activeLang().ext }}</span>
                </div>
                @if (activeLang(); as lang) {
                  <span [class]="'rounded border px-1.5 py-0.5 text-[9px] font-semibold ' + lang.accentBg + ' ' + lang.accentBorder + ' ' + lang.accentText">
                    {{ lang.label }}
                  </span>
                }
              </div>

              <!-- Meta + last-log status + copy -->
              <div class="flex items-center gap-2">
                @if (store.currentTaskSpec(); as spec) {
                  <span class="max-w-[120px] truncate text-[10px] text-gray-400">{{ spec.taskName }}</span>
                  @if (spec.estimatedEffort) {
                    <span class="rounded border border-gray-700/50 bg-gray-900 px-1.5 py-0.5
                                 text-[9px] text-gray-400">{{ spec.estimatedEffort }}</span>
                  }
                  @if (spec.modelUsed) {
                    <span class="text-[9px] text-gray-400">via {{ spec.modelUsed }}</span>
                  }
                }
                @if (store.logsQueue().length > 0) {
                  <span class="hidden max-w-[160px] truncate text-[9px] text-gray-400 xl:inline">
                    {{ parseMsg(store.logsQueue()[store.logsQueue().length - 1]) }}
                  </span>
                }
                <button (click)="onCopy()"
                  [disabled]="!currentCode() || copyState() !== 'idle'"
                  [class]="copyBtnClass()">
                  @if (copyState() === 'copied') {
                    <lucide-icon name="check" [size]="12" class="text-emerald-400" />
                    <span>Copied!</span>
                  } @else {
                    <lucide-icon name="copy" [size]="12" />
                    <span>Copy</span>
                  }
                </button>
              </div>
            </div>

            <!-- Code body -->
            <div class="relative flex min-h-0 flex-1 flex-col overflow-auto p-4"
                 style="font-family: var(--font-mono, 'JetBrains Mono', monospace)">
              @if (currentCode()) {
                <pre class="text-[11px] leading-relaxed text-emerald-300/85 whitespace-pre">{{ currentCode() }}</pre>
              } @else {
                <div class="absolute inset-0 flex flex-col items-center justify-center gap-4 text-center">
                  <div class="flex h-14 w-14 items-center justify-center rounded-2xl
                              border border-gray-800 bg-gray-900/60">
                    <lucide-icon name="code-2" [size]="24" class="text-gray-700" />
                  </div>
                  <div>
                    <p class="text-sm font-medium text-gray-400">No code generated yet</p>
                    <p class="mt-1 text-xs text-gray-400">
                      @if (integrationSteps().length > 0) {
                        Select a step and click "Generate Code"
                      } @else {
                        Enter a task in the custom field and click "Run Custom"
                      }
                    </p>
                  </div>
                </div>
              }
            </div>
          </div>

        </div>
      </div>
    </section>
  `,
})
export class ExecutionPipelineComponent {

  protected readonly store = inject(WorkspaceStoreService);

  // Language
  protected readonly languages = LANGUAGES;
  protected readonly selectedLanguage = signal<string>('csharp');
  protected readonly activeLang = computed(
    () => LANGUAGES.find(l => l.id === this.selectedLanguage()) ?? LANGUAGES[0],
  );

  // Steps
  protected readonly customTaskName       = signal('');
  protected readonly selectedStepIndex    = signal<number>(0);
  protected readonly completedStepIndices = signal<number[]>([]);
  protected readonly copyState            = signal<'idle' | 'copied'>('idle');
  protected readonly isGeneratingAll      = signal(false);
  protected readonly isDownloadingProject = signal(false);

  // Per-step code cache — preserves generated code for each step index
  private readonly _stepCodeCache = new Map<number, string>();
  // What the code panel currently displays (may differ from store.currentTaskSpec)
  protected readonly displayCode = signal<string>('');

  protected readonly integrationSteps = computed(() => {
    const sol = this.store.selectedSolution();
    if (!sol?.integrationSteps) return [];

    const strip = (s: string) => s.replace(/^\d+[.)]\s*/, '').trim();

    // Try newline-delimited first (ideal format)
    const byNewline = sol.integrationSteps
      .split('\n')
      .filter(l => /^\d+[.)]\s/.test(l.trim()))
      .map(strip)
      .filter(Boolean);

    if (byNewline.length > 1) return byNewline;

    // Fallback: split inline "1. Step one. 2. Step two." on the numbered markers
    return sol.integrationSteps
      .split(/\s+(?=\d+[.)]\s)/)
      .map(strip)
      .filter(Boolean);
  });

  protected readonly progressPercent = computed(() => {
    const total = this.integrationSteps().length;
    const done  = this.completedStepIndices().length;
    return total === 0 ? 0 : Math.round((done / total) * 100);
  });

  protected readonly currentCode = computed(() => this.displayCode());

  constructor() {
    // Reset step state when the active solution changes
    effect(() => {
      void this.store.selectedSolution();
      this.selectedStepIndex.set(0);
      this.completedStepIndices.set([]);
      this._stepCodeCache.clear();
      this.displayCode.set('');
    });
  }

  protected selectStep(idx: number): void {
    this.selectedStepIndex.set(idx);
    // Show previously generated code for this step if available
    this.displayCode.set(this._stepCodeCache.get(idx) ?? '');
  }

  protected onGenerateCode(): void {
    const steps = this.integrationSteps();
    const idx   = this.selectedStepIndex();
    const step  = steps[idx];
    if (!step || this.store.isExecutingTask() || this.isGeneratingAll()) return;
    this.runTask(step, idx);
  }

  protected onRunCustom(): void {
    const name = this.customTaskName().trim();
    if (!name || this.store.isExecutingTask() || this.isGeneratingAll()) return;
    this.customTaskName.set('');
    this.runTask(name, -1);
  }

  protected onGenerateComplete(): void {
    const sol   = this.store.selectedSolution();
    const steps = this.integrationSteps();
    if (!sol || steps.length === 0 || this.store.isExecutingTask() || this.isGeneratingAll()) return;

    this.isGeneratingAll.set(true);
    this._stepCodeCache.clear();
    this.completedStepIndices.set([]);
    this.displayCode.set('');

    const collected: { step: string; code: string }[] = [];

    const runStep = (idx: number): void => {
      if (idx >= steps.length) {
        const sep = '\n\n// ' + '─'.repeat(60) + '\n\n';
        const combined = collected
          .map((item, i) => `// ── Step ${i + 1}: ${item.step} ──\n${item.code}`)
          .join(sep);
        this.displayCode.set(combined);
        this.isGeneratingAll.set(false);
        return;
      }

      this.selectedStepIndex.set(idx);
      const request: ExecuteTaskRequest = {
        taskName:      steps[idx],
        context:       `Solution: ${sol.name}. ${sol.description}`,
        systemicValue: sol.realLifeValue,
        language:      this.selectedLanguage(),
        blueprintId:   this.store.compiledBlueprint()?.id || undefined,
      };

      this.store.executeTask(request).subscribe({
        next: () => {
          const code = this.store.currentTaskSpec()?.generatedCodeTemplate ?? '';
          collected.push({ step: steps[idx], code });
          this._stepCodeCache.set(idx, code);
          this.completedStepIndices.update(list =>
            list.includes(idx) ? list : [...list, idx],
          );
          // Defer to next tick so the previous executeTask finalize runs first
          setTimeout(() => runStep(idx + 1), 0);
        },
        error: () => {
          collected.push({ step: steps[idx], code: '// Code generation failed for this step' });
          setTimeout(() => runStep(idx + 1), 0);
        },
      });
    };

    runStep(0);
  }

  protected onDownloadProject(): void {
    const sol = this.store.selectedSolution();
    if (!sol || this.isDownloadingProject()) return;

    const steps = this.integrationSteps();
    const codes: string[] = steps.map((_, i) => this._stepCodeCache.get(i) ?? '');

    const request: GenerateProjectRequest = {
      solutionName:     sol.name,
      description:      sol.description,
      integrationSteps: steps,
      stepCodes:        codes,
      language:         this.selectedLanguage(),
      realLifeValue:    sol.realLifeValue,
    };

    this.isDownloadingProject.set(true);
    this.store.downloadProject(request).subscribe({
      next: (blob) => {
        const url      = URL.createObjectURL(blob);
        const anchor   = document.createElement('a');
        const safeName = sol.name.replace(/[^\w\s-]/g, '').replace(/\s+/g, '_').slice(0, 40);
        anchor.href     = url;
        anchor.download = `${safeName}.zip`;
        anchor.click();
        URL.revokeObjectURL(url);
        this.isDownloadingProject.set(false);
      },
      error: () => this.isDownloadingProject.set(false),
    });
  }

  private runTask(taskName: string, stepIndex: number): void {
    const sol = this.store.selectedSolution();
    const request: ExecuteTaskRequest = {
      taskName,
      context:      sol ? `${sol.name}: ${sol.description}` : undefined,
      systemicValue: sol?.realLifeValue,
      language:     this.selectedLanguage(),
      blueprintId:  this.store.compiledBlueprint()?.id || undefined,
    };

    this.store.executeTask(request).subscribe({
      next: () => {
        // Cache the generated code for this step
        const code = this.store.currentTaskSpec()?.generatedCodeTemplate ?? '';
        this.displayCode.set(code);
        if (stepIndex < 0) return;
        if (code) this._stepCodeCache.set(stepIndex, code);
        if (!this.completedStepIndices().includes(stepIndex))
          this.completedStepIndices.update(list => [...list, stepIndex]);
        // Advance to next incomplete step
        const completed = new Set(this.completedStepIndices());
        const next = this.integrationSteps().findIndex((_, i) => !completed.has(i));
        if (next !== -1) this.selectedStepIndex.set(next);
      },
    });
  }

  protected async onCopy(): Promise<void> {
    if (!this.currentCode()) return;
    try {
      await navigator.clipboard.writeText(this.currentCode());
      this.copyState.set('copied');
      setTimeout(() => this.copyState.set('idle'), 2200);
    } catch { /* ignore */ }
  }

  protected isCompleted(idx: number): boolean {
    return this.completedStepIndices().includes(idx);
  }

  protected isRunning(idx: number): boolean {
    return this.store.isExecutingTask() && this.selectedStepIndex() === idx;
  }

  protected isSelected(idx: number): boolean {
    return !this.isRunning(idx) && this.selectedStepIndex() === idx;
  }

  // ── Style helpers ──────────────────────────────────────────────

  protected stepRowClass(idx: number): string {
    const base = 'flex items-start gap-2.5 rounded-lg px-2.5 py-2 border transition-all duration-200 cursor-pointer';
    if (this.isRunning(idx))
      return `${base} border-blue-500/30 bg-blue-500/10 cursor-default`;
    // Completed + currently selected → show violet selection on top of emerald done state
    if (this.isCompleted(idx) && this.isSelected(idx))
      return `${base} border-violet-500/30 bg-violet-500/10`;
    if (this.isCompleted(idx))
      return `${base} border-emerald-500/20 bg-emerald-500/5 hover:border-emerald-500/35`;
    if (this.isSelected(idx))
      return `${base} border-violet-500/30 bg-violet-500/10`;
    return `${base} border-gray-800/50 hover:border-gray-700`;
  }

  protected stepNumClass(idx: number): string {
    const base = 'flex h-5 w-5 shrink-0 items-center justify-center rounded-md border mt-0.5';
    if (this.isCompleted(idx)) return `${base} border-emerald-500/30 bg-emerald-500/15`;
    if (this.isRunning(idx))   return `${base} border-blue-500/30 bg-blue-500/15`;
    if (this.isSelected(idx))  return `${base} border-violet-500/30 bg-violet-500/15`;
    return `${base} border-gray-800 bg-gray-900 text-gray-400`;
  }

  protected langTabClass(lang: LangDef): string {
    const isActive = this.selectedLanguage() === lang.id;
    const base = 'rounded-lg px-2.5 py-1.5 text-[10px] font-semibold transition-all duration-150 focus:outline-none';
    return isActive
      ? `${base} ${lang.accentBg} ${lang.accentBorder} border ${lang.accentText}`
      : `${base} border border-transparent text-gray-400 hover:text-gray-400`;
  }

  protected copyBtnClass(): string {
    const base = 'flex items-center gap-1 rounded px-2 py-1 text-[10px] font-medium transition-all focus:outline-none disabled:opacity-40';
    return this.copyState() === 'copied'
      ? `${base} text-emerald-400`
      : `${base} text-gray-400 hover:text-gray-400`;
  }

  protected parseMsg(log: string): string {
    return log.replace(/^\[[^\]]+\]\s*/, '');
  }
}
