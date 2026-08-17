import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LucideAngularModule } from 'lucide-angular';
import { WorkspaceStoreService } from '../../core/services/workspace-store.service';
import { ArtifactKind, ArtifactMetadata, ComparisonMatrix } from '../../core/models/interfaces';

/** Compare view: pick 2+ same-kind artifacts and render the divergence-highlighted comparison matrix. */
@Component({
  selector: 'app-artifact-compare',
  standalone: true,
  imports: [CommonModule, LucideAngularModule],
  template: `
  <div class="flex h-full flex-col overflow-hidden bg-gray-950 text-gray-100">
    <header class="flex items-center justify-between border-b border-gray-800/60 px-6 py-4">
      <div>
        <h1 class="text-lg font-semibold text-white">Compare</h1>
        <p class="text-[11px] text-gray-500">Select 2 or more artifacts of the same kind. Divergent cells are highlighted.</p>
      </div>
      <button (click)="compare()" [disabled]="selectedIds().size < 2 || loading()"
        class="flex h-8 items-center gap-1.5 rounded-lg border border-indigo-500/40 bg-indigo-500/15 px-3 text-[11px] font-medium text-indigo-200 hover:bg-indigo-500/25 disabled:opacity-40">
        @if (loading()) { <lucide-icon name="loader-2" [size]="12" class="animate-spin" /> } @else { <lucide-icon name="columns-3" [size]="12" /> }
        Compare ({{ selectedIds().size }})
      </button>
    </header>

    <main class="grid min-h-0 flex-1 grid-cols-1 gap-4 overflow-hidden p-4 lg:grid-cols-[300px_1fr]">
      <!-- selection -->
      <section class="flex min-h-0 flex-col rounded-lg border border-gray-800/60 bg-gray-900/60">
        <div class="border-b border-gray-800/60 px-3 py-2 text-[11px] text-gray-500">
          Artifacts @if (lockedKind()) { <span class="text-gray-400">· locked to {{ lockedKind() }}</span> }
        </div>
        <div class="min-h-0 flex-1 overflow-y-auto p-2">
          @for (a of artifacts(); track a.artifactId) {
            <button (click)="toggle(a)" [disabled]="isDisabled(a)"
              class="mb-1.5 flex w-full items-start gap-2 rounded-md border px-2.5 py-2 text-left transition-all disabled:opacity-30"
              [class]="selectedIds().has(a.artifactId) ? 'border-indigo-500/50 bg-indigo-500/10' : 'border-gray-800/60 bg-gray-900/40 hover:border-gray-700'">
              <lucide-icon [name]="selectedIds().has(a.artifactId) ? 'check-square' : 'square'" [size]="14"
                [class]="selectedIds().has(a.artifactId) ? 'mt-0.5 text-indigo-400' : 'mt-0.5 text-gray-600'" />
              <span class="min-w-0 flex-1">
                <span class="block truncate text-xs font-medium text-gray-200">{{ a.title || a.lineageId }}</span>
                <span class="block text-[10px] uppercase tracking-wide text-gray-500">{{ a.kind }} · v{{ a.version }}</span>
              </span>
            </button>
          }
        </div>
      </section>

      <!-- matrix -->
      <section class="min-h-0 overflow-auto rounded-lg border border-gray-800/60 bg-gray-900/60">
        @if (matrix(); as m) {
          <table class="w-full text-left text-xs">
            <thead class="sticky top-0 bg-gray-900">
              <tr>
                <th class="border-b border-gray-800 px-3 py-2 text-[10px] uppercase tracking-wide text-gray-500">Dimension</th>
                @for (c of m.columns; track c.artifactId) {
                  <th class="border-b border-gray-800 px-3 py-2 text-gray-300">
                    <span class="block truncate">{{ c.title || c.artifactId }}</span>
                    <span class="block text-[10px] font-normal text-gray-500">{{ c.modelUsed }} · v{{ c.version }}</span>
                  </th>
                }
              </tr>
            </thead>
            <tbody>
              @for (row of m.rows; track row.dimension) {
                <tr class="align-top">
                  <td class="border-b border-gray-800/60 px-3 py-2 text-[11px] font-medium text-gray-400">{{ row.dimension }}</td>
                  @for (cell of row.cells; track cell.artifactId) {
                    <td class="border-b border-gray-800/60 px-3 py-2 text-[11px]"
                      [class]="cell.divergent ? 'bg-amber-500/10 text-amber-200' : 'text-gray-300'">
                      {{ cell.value || '—' }}
                    </td>
                  }
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <div class="flex h-full items-center justify-center text-center text-gray-600">
            <div><lucide-icon name="columns-3" [size]="32" class="mx-auto mb-2 text-gray-700" /><p class="text-xs">Pick 2+ artifacts and press Compare.</p></div>
          </div>
        }
      </section>
    </main>
  </div>
  `,
})
export class ArtifactCompareComponent implements OnInit {
  private readonly store = inject(WorkspaceStoreService);

  protected readonly artifacts = signal<ArtifactMetadata[]>([]);
  protected readonly selectedIds = signal<Set<string>>(new Set());
  protected readonly matrix = signal<ComparisonMatrix | null>(null);
  protected readonly loading = signal<boolean>(false);

  // Comparison requires same kind — lock to the first selected artifact's kind.
  protected readonly lockedKind = computed<ArtifactKind | null>(() => {
    const first = [...this.selectedIds()][0];
    return first ? (this.artifacts().find(a => a.artifactId === first)?.kind ?? null) : null;
  });

  ngOnInit(): void {
    this.store.listArtifacts({ latestOnly: false, take: 300 })
      .subscribe({ next: rows => this.artifacts.set(rows), error: () => {} });
  }

  protected isDisabled(a: ArtifactMetadata): boolean {
    const k = this.lockedKind();
    return k !== null && a.kind !== k && !this.selectedIds().has(a.artifactId);
  }

  protected toggle(a: ArtifactMetadata): void {
    this.selectedIds.update(set => {
      const next = new Set(set);
      next.has(a.artifactId) ? next.delete(a.artifactId) : next.add(a.artifactId);
      return next;
    });
  }

  protected compare(): void {
    const ids = [...this.selectedIds()];
    if (ids.length < 2) return;
    this.loading.set(true);
    this.store.compareArtifacts(ids)
      .subscribe({ next: m => { this.matrix.set(m); this.loading.set(false); }, error: () => this.loading.set(false) });
  }
}
