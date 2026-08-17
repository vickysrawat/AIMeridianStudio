import {
  Component,
  ElementRef,
  OnInit,
  ViewChild,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { WorkspaceStoreService } from '../../core/services/workspace-store.service';
import { AiModel, GenerateComponentPromptRequest } from '../../core/models/interfaces';

interface SavedPrompt {
  id: string;
  name: string;
  model: AiModel;
  content: string;
  createdAt: string;
}

interface ModelDef {
  id: AiModel;
  label: string;
  shortLabel: string;
  provider: string;
  accentText: string;
  accentBg: string;
  accentBorder: string;
}

const MODELS: ModelDef[] = [
  {
    id: 'claude-sonnet-4-6',
    label: 'Claude Sonnet 4.6',
    shortLabel: 'Claude',
    provider: 'Anthropic',
    accentText: 'text-orange-300',
    accentBg: 'bg-orange-500/15',
    accentBorder: 'border-orange-500/35',
  },
  {
    id: 'gemini-2.5-flash',
    label: 'Gemini 2.5 Flash',
    shortLabel: 'Gemini',
    provider: 'Google DeepMind',
    accentText: 'text-blue-300',
    accentBg: 'bg-blue-500/15',
    accentBorder: 'border-blue-500/35',
  },
  {
    id: 'llama-3.3-70b-versatile',
    label: 'Llama 3.3 70B',
    shortLabel: 'Llama',
    provider: 'Meta / Groq',
    accentText: 'text-violet-300',
    accentBg: 'bg-violet-500/15',
    accentBorder: 'border-violet-500/35',
  },
];

const LS_KEY = 'meridian-saved-prompts';

function genId(): string {
  return Math.random().toString(36).slice(2, 10);
}

@Component({
  selector: 'app-prompt-studio',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule],
  template: `
    <div class="flex h-full w-full min-h-0 overflow-hidden">

      <!-- ══ Left: Saved Library ══════════════════════════════ -->
      <aside class="flex w-64 shrink-0 flex-col border-r border-gray-800/60 bg-gray-950/50 xl:w-72">

        <div class="border-b border-gray-800/60 px-4 py-3.5">
          <div class="flex items-center gap-2">
            <lucide-icon name="book-marked" [size]="14" class="text-indigo-400" />
            <span class="text-xs font-semibold text-gray-300">Saved Library</span>
            @if (savedPrompts().length) {
              <span class="ml-auto rounded-full bg-indigo-500/20 px-2 py-0.5
                           text-[10px] font-semibold text-indigo-400">
                {{ savedPrompts().length }}
              </span>
            }
          </div>
          <div class="relative mt-2.5">
            <lucide-icon name="search" [size]="12"
              class="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-gray-400" />
            <input type="text"
              [ngModel]="librarySearch()"
              (ngModelChange)="librarySearch.set($event)"
              placeholder="Search prompts…"
              class="h-8 w-full rounded-lg border border-gray-800 bg-gray-900/60 pl-7 pr-2
                     text-[11px] text-gray-400 placeholder-gray-500
                     focus:border-indigo-500/40 focus:outline-none" />
          </div>
        </div>

        <div class="flex-1 overflow-y-auto p-2">
          @if (filteredSaved().length === 0) {
            <div class="flex flex-col items-center gap-3 py-12 text-center">
              <lucide-icon name="book-marked" [size]="24" class="text-gray-800" />
              <p class="text-[11px] leading-relaxed text-gray-400">
                @if (librarySearch()) { No prompts match "{{ librarySearch() }}"
                } @else { No saved prompts yet. }
              </p>
            </div>
          }
          @for (prompt of filteredSaved(); track prompt.id) {
            <div [class]="promptCardClass(prompt.id)"
                 (click)="loadPrompt(prompt)" role="button" tabindex="0"
                 (keydown.enter)="loadPrompt(prompt)">
              @if (renamingId() === prompt.id) {
                <input #renameInput type="text"
                  [ngModel]="renameValue()"
                  (ngModelChange)="renameValue.set($event)"
                  (keydown.enter)="commitRename(prompt.id)"
                  (keydown.escape)="renamingId.set(null)"
                  (blur)="commitRename(prompt.id)"
                  (click)="$event.stopPropagation()"
                  class="w-full rounded border border-indigo-500/40 bg-gray-900 px-2 py-1
                         text-[11px] text-white focus:outline-none" />
              } @else {
                <div class="flex min-w-0 flex-1 flex-col gap-0.5">
                  <span class="truncate text-[11px] font-medium"
                        [class]="activePromptId() === prompt.id ? 'text-indigo-300' : 'text-gray-300'">
                    {{ prompt.name }}
                  </span>
                  <span [class]="modelDef(prompt.model).accentText + ' text-[9px] font-semibold'">
                    {{ modelDef(prompt.model).shortLabel }}
                  </span>
                </div>
                <div class="flex shrink-0 items-center gap-0.5 opacity-0
                            transition-opacity group-hover:opacity-100">
                  <button (click)="startRename(prompt, $event)"
                    class="rounded p-1 text-gray-400 hover:bg-gray-700 hover:text-gray-300">
                    <lucide-icon name="pencil" [size]="11" />
                  </button>
                  <button (click)="deletePrompt(prompt.id, $event)"
                    class="rounded p-1 text-gray-400 hover:bg-red-500/15 hover:text-red-400">
                    <lucide-icon name="trash-2" [size]="11" />
                  </button>
                </div>
              }
            </div>
          }
        </div>

        <div class="border-t border-gray-800/60 px-4 py-3 text-[10px] text-gray-400">
          Persisted in localStorage
        </div>
      </aside>

      <!-- ══ Main: Editor ══════════════════════════════════════ -->
      <div class="flex min-w-0 flex-1 flex-col">

        <!-- Header / controls -->
        <div class="flex shrink-0 flex-wrap items-center gap-3 border-b border-gray-800/60
                    bg-gray-950/80 px-5 py-3 backdrop-blur-sm">
          <div class="flex items-center gap-2.5">
            <div class="flex h-7 w-7 items-center justify-center rounded-lg bg-violet-500/15">
              <lucide-icon name="code-2" [size]="14" class="text-violet-400" />
            </div>
            <span class="text-sm font-semibold text-white">Developer Prompt Studio</span>
          </div>

          <!-- Component name input -->
          <input type="text"
            [ngModel]="componentName()"
            (ngModelChange)="componentName.set($event)"
            placeholder="Component / feature name…"
            class="h-8 rounded-lg border border-gray-800 bg-gray-900/60 px-3 text-xs
                   text-gray-300 placeholder-gray-500
                   focus:border-indigo-500/40 focus:outline-none focus:ring-1 focus:ring-indigo-500/20" />

          <!-- Model selector -->
          <div class="flex rounded-xl border border-gray-800 bg-gray-900/60 p-0.5">
            @for (model of models; track model.id) {
              <button (click)="selectedModel.set(model.id)" [class]="modelTabClass(model)">
                <lucide-icon name="bot" [size]="11" />
                {{ model.shortLabel }}
              </button>
            }
          </div>

          <!-- Generate -->
          <button (click)="onGenerate()" [disabled]="store.isGeneratingPrompt()"
            class="ml-auto flex h-9 items-center gap-1.5 rounded-lg
                   bg-gradient-to-r from-violet-600 to-indigo-600 px-4 text-xs font-medium
                   text-white shadow-md shadow-violet-600/20 transition-all
                   hover:from-violet-500 hover:to-indigo-500
                   focus:outline-none focus:ring-2 focus:ring-violet-500/40
                   disabled:cursor-not-allowed disabled:opacity-50">
            @if (store.isGeneratingPrompt()) {
              <lucide-icon name="loader-2" [size]="13" class="animate-spin" />
              <span>Generating…</span>
            } @else {
              <lucide-icon name="sparkles" [size]="13" />
              <span>Generate System Prompt</span>
            }
          </button>
        </div>

        <!-- ── Code editor ────────────────────────────────── -->
        <div class="flex min-h-0 flex-1 flex-col bg-gray-950">

          <!-- Editor chrome bar -->
          <div class="flex shrink-0 items-center gap-3 border-b border-gray-800/60
                      bg-gray-900/60 px-4 py-2">
            <div class="flex items-center gap-1.5">
              <span class="h-2.5 w-2.5 rounded-full bg-red-500/80"></span>
              <span class="h-2.5 w-2.5 rounded-full bg-amber-400/80"></span>
              <span class="h-2.5 w-2.5 rounded-full bg-emerald-500/80"></span>
            </div>
            <span class="text-[11px] text-gray-400"
                  style="font-family: var(--font-mono, monospace)">system-prompt.md</span>
            @if (activeModel(); as m) {
              <span [class]="'ml-2 flex items-center gap-1 rounded-md border px-2 py-0.5 text-[10px] font-semibold ' + m.accentBg + ' ' + m.accentBorder + ' ' + m.accentText">
                {{ m.label }}
              </span>
            }
            <div class="ml-auto flex items-center gap-3 text-[10px] text-gray-400">
              @if (lineCount() > 0) {
                <span>{{ lineCount() }} lines</span>
                <span class="h-3 w-px bg-gray-800"></span>
              }
              @if (tokenEstimate() > 0) {
                <span>~{{ tokenEstimate() | number }} tokens</span>
              }
              @if (store.currentPrompt()?.modelUsed) {
                <span class="h-3 w-px bg-gray-800"></span>
                <span>via {{ store.currentPrompt()!.modelUsed }}</span>
              }
            </div>
          </div>

          <!-- Editor body -->
          <div class="prompt-editor-wrap flex-1">
            <div class="prompt-line-numbers" #lineNumsEl>
              @for (n of lineNumbers(); track n) {
                <div>{{ n }}</div>
              }
            </div>
            <pre class="prompt-editor-pre" #displayPre
                 [innerHTML]="highlightedContent()"></pre>
            <textarea #editorTextarea class="prompt-editor-textarea"
              [value]="editorContent()"
              (input)="onEditorInput($event)"
              (scroll)="syncScroll($event)"
              (keydown.tab)="onTab($event)"
              placeholder="// Click 'Generate System Prompt' to populate this editor,&#10;// or start typing your custom prompt…"
              spellcheck="false"
              autocomplete="off"></textarea>
          </div>

          <!-- Action bar -->
          <div class="flex shrink-0 items-center gap-2 border-t border-gray-800/60
                      bg-gray-900/40 px-4 py-2.5">
            <button (click)="onCopy()" [class]="copyBtnClass()"
              [disabled]="!editorContent() || copyState() !== 'idle'">
              @if (copyState() === 'copied') {
                <lucide-icon name="check" [size]="13" class="text-emerald-400" />
                <span>Copied!</span>
              } @else {
                <lucide-icon name="copy" [size]="13" />
                <span>Copy Prompt</span>
              }
            </button>
            <button (click)="onSave()" [disabled]="!editorContent()"
              class="flex h-9 items-center gap-1.5 rounded-lg border border-gray-700/60
                     bg-gray-800/40 px-3.5 text-[11px] font-medium text-gray-300
                     transition-all hover:border-indigo-500/30 hover:bg-indigo-500/8
                     hover:text-indigo-300 focus:outline-none
                     disabled:cursor-not-allowed disabled:opacity-40">
              <lucide-icon name="save" [size]="13" />
              <span>Save to Library</span>
            </button>
            <button (click)="onClear()" [disabled]="!editorContent()"
              class="flex h-9 items-center gap-1.5 rounded-lg border border-transparent
                     px-3 text-[11px] font-medium text-gray-400 transition-all
                     hover:border-gray-700/40 hover:text-gray-400
                     disabled:cursor-not-allowed disabled:opacity-30">
              <lucide-icon name="x" [size]="13" />
              <span>Clear</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  `,
})
export class PromptStudioComponent implements OnInit {
  @ViewChild('displayPre') private displayPre!: ElementRef<HTMLPreElement>;
  @ViewChild('lineNumsEl') private lineNumsEl!: ElementRef<HTMLDivElement>;
  @ViewChild('renameInput') private renameInput?: ElementRef<HTMLInputElement>;

  protected readonly store = inject(WorkspaceStoreService);

  protected readonly models = MODELS;
  protected readonly selectedModel = signal<AiModel>('claude-sonnet-4-6');
  protected readonly componentName = signal('');
  protected readonly editorContent = signal<string>('');
  protected readonly copyState = signal<'idle' | 'copied'>('idle');
  protected readonly librarySearch = signal<string>('');
  protected readonly savedPrompts = signal<SavedPrompt[]>([]);
  protected readonly activePromptId = signal<string | null>(null);
  protected readonly renamingId = signal<string | null>(null);
  protected readonly renameValue = signal<string>('');

  protected readonly activeModel = computed(
    () => MODELS.find(m => m.id === this.selectedModel()) ?? MODELS[0],
  );

  protected readonly lineNumbers = computed(() => {
    const count = (this.editorContent().match(/\n/g)?.length ?? 0) + 1;
    return Array.from({ length: count }, (_, i) => i + 1);
  });

  protected readonly lineCount = computed(() => this.lineNumbers().length);
  protected readonly tokenEstimate = computed(() =>
    Math.round(this.editorContent().length / 4),
  );

  protected readonly highlightedContent = computed(() => {
    if (!this.editorContent()) return '';
    return this.editorContent()
      .split('\n')
      .map(line => {
        const esc = line
          .replace(/&/g, '&amp;')
          .replace(/</g, '&lt;')
          .replace(/>/g, '&gt;');
        if (esc.startsWith('//')) return `<span class="prompt-comment">${esc}</span>`;
        if (/^#{1,3}\s/.test(esc)) return `<span class="prompt-heading">${esc}</span>`;
        if (/^[A-Z][A-Z\s]+:/.test(esc)) return `<span class="prompt-key">${esc}</span>`;
        if (/"[^"]*"/.test(esc))
          return `<span class="prompt-line">${esc.replace(/"([^"]*)"/g, '<span class="prompt-string">"$1"</span>')}</span>`;
        return `<span class="prompt-line">${esc}</span>`;
      })
      .join('\n');
  });

  protected readonly filteredSaved = computed(() => {
    const q = this.librarySearch().toLowerCase();
    return q
      ? this.savedPrompts().filter(
          p => p.name.toLowerCase().includes(q) || p.model.toLowerCase().includes(q),
        )
      : this.savedPrompts();
  });

  private readonly _syncPrompt = effect(() => {
    const prompt = this.store.currentPrompt();
    if (prompt?.promptText) {
      this.editorContent.set(prompt.promptText);
      this.activePromptId.set(null);
    }
  });

  ngOnInit(): void {
    try {
      const raw = localStorage.getItem(LS_KEY);
      if (raw) this.savedPrompts.set(JSON.parse(raw) as SavedPrompt[]);
    } catch { /* ignore */ }
  }

  protected onEditorInput(event: Event): void {
    this.editorContent.set((event.target as HTMLTextAreaElement).value);
  }

  protected syncScroll(event: Event): void {
    const ta = event.target as HTMLTextAreaElement;
    if (this.displayPre?.nativeElement) {
      this.displayPre.nativeElement.scrollTop = ta.scrollTop;
      this.displayPre.nativeElement.scrollLeft = ta.scrollLeft;
    }
    if (this.lineNumsEl?.nativeElement) {
      this.lineNumsEl.nativeElement.scrollTop = ta.scrollTop;
    }
  }

  protected onTab(event: Event): void {
    event.preventDefault();
    const ta = event.target as HTMLTextAreaElement;
    const start = ta.selectionStart;
    const end = ta.selectionEnd;
    this.editorContent.set(
      this.editorContent().slice(0, start) + '  ' + this.editorContent().slice(end),
    );
    setTimeout(() => { ta.selectionStart = ta.selectionEnd = start + 2; }, 0);
  }

  protected onGenerate(): void {
    const sol = this.store.selectedSolution();
    const bp = this.store.compiledBlueprint();
    const name =
      this.componentName().trim() ||
      sol?.name ||
      bp?.solutionName ||
      'System Implementation';

    const request: GenerateComponentPromptRequest = {
      componentName: name,
      targetLLM: this.selectedModel(),
      context: bp
        ? `Blueprint: ${bp.solutionName}. Domain: ${bp.domain}.`
        : sol
        ? `Solution: ${sol.name}. ${sol.description}`
        : undefined,
    };
    this.store.generateComponentPrompt(request).subscribe();
  }

  protected async onCopy(): Promise<void> {
    if (!this.editorContent()) return;
    try {
      await navigator.clipboard.writeText(this.editorContent());
      this.copyState.set('copied');
      setTimeout(() => this.copyState.set('idle'), 2200);
    } catch { /* ignore */ }
  }

  protected onSave(): void {
    const content = this.editorContent().trim();
    if (!content) return;
    const first = content.split('\n')[0].replace(/^[/#\s]+/, '').slice(0, 60) || 'Untitled Prompt';
    const saved: SavedPrompt = {
      id: genId(),
      name: first,
      model: this.selectedModel(),
      content,
      createdAt: new Date().toISOString(),
    };
    this.savedPrompts.update(list => [saved, ...list]);
    this.activePromptId.set(saved.id);
    this.persist();
  }

  protected onClear(): void {
    this.editorContent.set('');
    this.activePromptId.set(null);
    this.store.currentPrompt.set(null);
  }

  protected loadPrompt(prompt: SavedPrompt): void {
    if (this.renamingId()) return;
    this.editorContent.set(prompt.content);
    this.selectedModel.set(prompt.model);
    this.activePromptId.set(prompt.id);
  }

  protected startRename(prompt: SavedPrompt, event: MouseEvent): void {
    event.stopPropagation();
    this.renamingId.set(prompt.id);
    this.renameValue.set(prompt.name);
    setTimeout(() => this.renameInput?.nativeElement.focus(), 50);
  }

  protected commitRename(id: string): void {
    const name = this.renameValue().trim();
    if (name) {
      this.savedPrompts.update(list => list.map(p => (p.id === id ? { ...p, name } : p)));
      this.persist();
    }
    this.renamingId.set(null);
  }

  protected deletePrompt(id: string, event: MouseEvent): void {
    event.stopPropagation();
    this.savedPrompts.update(list => list.filter(p => p.id !== id));
    if (this.activePromptId() === id) this.activePromptId.set(null);
    this.persist();
  }

  protected modelTabClass(model: ModelDef): string {
    const isActive = this.selectedModel() === model.id;
    const base =
      'flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-[11px] font-medium ' +
      'transition-all duration-150 focus:outline-none';
    return isActive
      ? `${base} ${model.accentBg} ${model.accentBorder} border ${model.accentText}`
      : `${base} text-gray-400 hover:text-gray-400`;
  }

  protected promptCardClass(id: string): string {
    const base =
      'group flex cursor-pointer items-center gap-2 rounded-lg px-2.5 py-2 ' +
      'transition-all duration-150 focus:outline-none';
    return this.activePromptId() === id
      ? `${base} bg-indigo-500/12 border border-indigo-500/30`
      : `${base} border border-transparent hover:bg-gray-800/40`;
  }

  protected copyBtnClass(): string {
    const base =
      'flex h-9 items-center gap-1.5 rounded-lg border px-3.5 text-[11px] font-medium ' +
      'transition-all duration-150 focus:outline-none disabled:cursor-not-allowed disabled:opacity-40';
    return this.copyState() === 'copied'
      ? `${base} border-emerald-500/30 bg-emerald-500/10 text-emerald-400`
      : `${base} border-gray-700/60 bg-gray-800/40 text-gray-300 hover:border-gray-600`;
  }

  protected modelDef(id: AiModel): ModelDef {
    return MODELS.find(m => m.id === id) ?? MODELS[0];
  }

  private persist(): void {
    try {
      localStorage.setItem(LS_KEY, JSON.stringify(this.savedPrompts()));
    } catch { /* ignore */ }
  }
}
