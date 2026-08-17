import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { WorkspaceStoreService } from '../../core/services/workspace-store.service';
import { MarkdownPipe } from '../../core/pipes/markdown.pipe';
import { MermaidDirective } from '../../core/directives/mermaid.directive';
import { ArtifactMetadata, ExportFormat, WhitePaper, WhitePaperRequest } from '../../core/models/interfaces';

/**
 * White-paper view. Two modes:
 *  • DRIVEN — launched from a Research run / opportunity / use-case (store.pendingWhitePaper): auto-generates
 *    a market/competitive paper (what's happening · what others are doing · what we can do), grounded in the
 *    research payload + fresh domain-aware live research, with cited sources.
 *  • MANUAL — pick arbitrary saved artifacts (legacy).
 * Preview renders Markdown; download PDF / DOCX / Markdown via the server exporter.
 */
@Component({
  selector: 'app-whitepaper',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule, MarkdownPipe, MermaidDirective],
  template: `
  <div class="flex h-full flex-col overflow-hidden bg-gray-950 text-gray-100">
    <header class="flex items-center justify-between border-b border-gray-800/60 px-6 py-4">
      <div>
        <h1 class="text-lg font-semibold text-white">White Paper</h1>
        <p class="text-[11px] text-gray-500">
          Market & competitive white paper — what's happening, what others are working on, and what we can do.
        </p>
      </div>
      <label class="flex items-center gap-2 text-[11px] text-gray-400">
        <input type="checkbox" [(ngModel)]="groundFresh" class="accent-indigo-500" />
        Ground with fresh research
      </label>
    </header>

    <main class="grid min-h-0 flex-1 grid-cols-1 gap-4 overflow-hidden p-4 lg:grid-cols-[340px_1fr]">
      <!-- Left: driver -->
      <section class="flex min-h-0 flex-col rounded-lg border border-gray-800/60 bg-gray-900/60">
        @if (mode() === 'driven') {
          <div class="p-4">
            <div class="mb-3 flex items-center gap-2 text-xs font-medium text-indigo-300">
              <lucide-icon name="sparkles" [size]="14" /> {{ driverLabel() }}
            </div>
            <p class="mb-3 text-[11px] text-gray-500">
              Generating a white paper grounded in this research/scenario{{ groundFresh ? ' + fresh live research' : '' }}.
            </p>
            <button (click)="regenerate()" [disabled]="generating()"
              class="flex w-full items-center justify-center gap-1.5 rounded-lg border border-indigo-500/40 bg-indigo-500/15 px-3 py-2 text-xs font-medium text-indigo-200 hover:bg-indigo-500/25 disabled:opacity-40">
              @if (generating()) { <lucide-icon name="loader-2" [size]="14" class="animate-spin" /> Generating… }
              @else { <lucide-icon name="refresh-cw" [size]="14" /> Regenerate }
            </button>
            <button (click)="switchToManual()" class="mt-2 w-full text-[11px] text-gray-500 hover:text-gray-300">or pick artifacts manually</button>
          </div>
        } @else {
          <div class="border-b border-gray-800/60 p-3">
            <label class="text-[11px] font-medium text-gray-400">Title (optional)</label>
            <input [(ngModel)]="title" placeholder="Derived from selection if blank"
              class="mt-1 w-full rounded-md border border-gray-700/60 bg-gray-800/60 px-2.5 py-1.5 text-xs text-gray-200 outline-none focus:border-indigo-500/50" />
          </div>
          <div class="flex items-center justify-between px-3 pt-3 text-[11px] text-gray-500">
            <span>Artifacts ({{ selectedIds().size }} selected)</span>
            <button (click)="reload()" class="text-gray-500 hover:text-indigo-300"><lucide-icon name="refresh-cw" [size]="12" /></button>
          </div>
          <div class="min-h-0 flex-1 overflow-y-auto p-2">
            @for (a of artifacts(); track a.artifactId) {
              <button (click)="toggle(a.artifactId)"
                class="mb-1.5 flex w-full items-start gap-2 rounded-md border px-2.5 py-2 text-left transition-all"
                [class]="selectedIds().has(a.artifactId) ? 'border-indigo-500/50 bg-indigo-500/10' : 'border-gray-800/60 bg-gray-900/40 hover:border-gray-700'">
                <lucide-icon [name]="selectedIds().has(a.artifactId) ? 'check-square' : 'square'" [size]="14"
                  [class]="selectedIds().has(a.artifactId) ? 'mt-0.5 text-indigo-400' : 'mt-0.5 text-gray-600'" />
                <span class="min-w-0 flex-1">
                  <span class="block truncate text-xs font-medium text-gray-200">{{ a.title || a.lineageId }}</span>
                  <span class="block text-[10px] uppercase tracking-wide text-gray-500">{{ a.kind }} · {{ a.domain || '—' }} · v{{ a.version }}</span>
                </span>
              </button>
            } @empty {
              <p class="p-3 text-[11px] text-gray-600">No saved artifacts. Run research or generate a blueprint/document first — or launch from a research run.</p>
            }
          </div>
          <div class="border-t border-gray-800/60 p-3">
            <button (click)="generateManual()" [disabled]="selectedIds().size === 0 || generating()"
              class="flex w-full items-center justify-center gap-1.5 rounded-lg border border-indigo-500/40 bg-indigo-500/15 px-3 py-2 text-xs font-medium text-indigo-200 hover:bg-indigo-500/25 disabled:opacity-40">
              @if (generating()) { <lucide-icon name="loader-2" [size]="14" class="animate-spin" /> Generating… }
              @else { <lucide-icon name="sparkles" [size]="14" /> Generate White Paper }
            </button>
          </div>
        }
      </section>

      <!-- Right: preview -->
      <section class="flex min-h-0 flex-col rounded-lg border border-gray-800/60 bg-gray-900/60">
        @if (paper(); as p) {
          <div class="flex items-center justify-between gap-3 border-b border-gray-800/60 px-4 py-3">
            <div class="min-w-0">
              <h2 class="truncate text-sm font-semibold text-white">{{ p.title }}</h2>
              <div class="mt-0.5 flex flex-wrap items-center gap-1.5 text-[10px] text-gray-500">
                <span>{{ p.modelUsed }}</span>
                @if (p.provenance?.confidence !== undefined) {
                  <span [class]="confidenceClass(p.provenance!.confidence!)">confidence {{ (p.provenance!.confidence! * 100) | number:'1.0-0' }}%</span>
                }
                @if (p.provenance?.sourceCount) { <span>· {{ p.provenance!.sourceCount }} sources</span> }
                @for (s of p.sourcesQueried ?? []; track $index) { <span class="rounded bg-gray-800 px-1 py-0.5">{{ s }}</span> }
              </div>
            </div>
            <div class="flex items-center gap-1.5">
              @for (f of formats; track f) {
                <button (click)="download(f)" [disabled]="exporting() !== null"
                  class="flex h-8 items-center gap-1.5 rounded-lg border border-gray-700/60 bg-gray-800/60 px-3 text-[11px] font-medium text-gray-300 hover:bg-gray-800 disabled:opacity-40">
                  @if (exporting() === f) { <lucide-icon name="loader-2" [size]="12" class="animate-spin" /> }
                  @else { <lucide-icon name="download" [size]="12" /> }
                  {{ f === 'markdown' ? 'MD' : (f === 'pdf' ? 'PDF' : 'Word') }}
                </button>
              }
            </div>
          </div>
          <div class="md-content min-h-0 flex-1 overflow-y-auto p-6 text-sm" [innerHTML]="p.content | markdown" appMermaid></div>
        } @else {
          <div class="flex flex-1 items-center justify-center text-center text-gray-600">
            <div>
              @if (generating()) {
                <lucide-icon name="loader-2" [size]="28" class="mx-auto mb-2 animate-spin text-indigo-400" />
                <p class="text-xs">Researching the domain and drafting the white paper…</p>
              } @else if (error()) {
                <lucide-icon name="alert-triangle" [size]="28" class="mx-auto mb-2 text-amber-400" />
                <p class="text-xs">{{ error() }}</p>
              } @else {
                <lucide-icon name="file-text" [size]="32" class="mx-auto mb-2 text-gray-700" />
                <p class="text-xs">Launch from a research run / opportunity / use-case, or pick artifacts to generate.</p>
              }
            </div>
          </div>
        }
      </section>
    </main>
  </div>
  `,
})
export class WhitePaperComponent implements OnInit {
  private readonly store = inject(WorkspaceStoreService);

  protected readonly mode = signal<'driven' | 'manual'>('manual');
  protected readonly artifacts = signal<ArtifactMetadata[]>([]);
  protected readonly selectedIds = signal<Set<string>>(new Set());
  protected title = '';
  protected groundFresh = true;
  protected readonly generating = signal<boolean>(false);
  protected readonly paper = signal<WhitePaper | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly exporting = signal<ExportFormat | null>(null);
  protected readonly formats: ExportFormat[] = ['markdown', 'pdf', 'docx'];

  private driven: WhitePaperRequest | null = null;

  ngOnInit(): void {
    const pending = this.store.consumePendingWhitePaper();
    if (pending) {
      this.driven = pending;
      this.mode.set('driven');
      this.run(pending);
    } else {
      this.reload();
    }
  }

  protected driverLabel(): string {
    if (this.driven?.opportunityId) return 'From selected opportunity';
    if (this.driven?.researchArtifactId) return 'From research run';
    if (this.driven?.assessmentId) return 'From use-case assessment';
    return 'From selection';
  }

  protected reload(): void {
    this.store.listArtifacts({ latestOnly: true, take: 200 })
      .subscribe({ next: rows => this.artifacts.set(rows), error: () => {} });
  }

  protected toggle(id: string): void {
    this.selectedIds.update(set => {
      const next = new Set(set);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  }

  protected switchToManual(): void { this.mode.set('manual'); this.driven = null; this.reload(); }

  protected regenerate(): void { if (this.driven) this.run(this.driven); }

  protected generateManual(): void {
    const ids = [...this.selectedIds()];
    if (ids.length === 0) return;
    this.run({ title: this.title.trim() || undefined, artifactIds: ids });
  }

  private run(req: WhitePaperRequest): void {
    this.generating.set(true);
    this.error.set(null);
    this.store.generateWhitePaper({ ...req, groundWithFreshResearch: this.groundFresh })
      .subscribe({
        next: p => { this.paper.set(p); this.generating.set(false); },
        error: () => { this.generating.set(false); this.error.set('White paper generation failed. Check that the source exists and try again.'); },
      });
  }

  protected download(format: ExportFormat): void {
    const p = this.paper();
    if (!p) return;
    const ext = format === 'markdown' ? 'md' : format;
    const slug = p.title.toLowerCase().replace(/[^\w]+/g, '-').replace(/^-|-$/g, '').slice(0, 60) || 'whitepaper';
    this.exporting.set(format);
    this.store.exportArtifact(p.id, format, `${slug}.${ext}`)
      .subscribe({ next: () => this.exporting.set(null), error: () => this.exporting.set(null) });
  }

  protected confidenceClass(c: number): string {
    return c >= 0.7 ? 'text-emerald-400' : c >= 0.5 ? 'text-amber-400' : 'text-gray-400';
  }
}
