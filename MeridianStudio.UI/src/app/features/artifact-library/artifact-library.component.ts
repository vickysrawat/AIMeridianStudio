import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { WorkspaceStoreService } from '../../core/services/workspace-store.service';
import { ArtifactKind, ArtifactMetadata, ExportFormat } from '../../core/models/interfaces';

/** Library view: browse / filter / delete persisted artifacts; export documents to PDF/DOCX/MD. */
@Component({
  selector: 'app-artifact-library',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule],
  template: `
  <div class="flex h-full flex-col overflow-hidden bg-gray-950 text-gray-100">
    <header class="flex items-center justify-between border-b border-gray-800/60 px-6 py-4">
      <div>
        <h1 class="text-lg font-semibold text-white">Library</h1>
        <p class="text-[11px] text-gray-500">{{ artifacts().length }} saved artifact(s) · latest version per lineage</p>
      </div>
      <div class="flex items-center gap-2">
        <select [(ngModel)]="kindFilter" (ngModelChange)="reload()"
          class="rounded-md border border-gray-700/60 bg-gray-800/60 px-2 py-1.5 text-[11px] text-gray-300 outline-none">
          <option value="">All kinds</option>
          <option value="research">Research</option>
          <option value="blueprint">Blueprint</option>
          <option value="document">Document</option>
          <option value="taskSpec">Task</option>
          <option value="developerPrompt">Prompt</option>
        </select>
        <button (click)="reload()" class="flex h-8 items-center gap-1.5 rounded-lg border border-gray-800 bg-gray-900/60 px-2.5 text-[11px] text-gray-400 hover:text-indigo-300">
          <lucide-icon name="refresh-cw" [size]="12" /> Refresh
        </button>
      </div>
    </header>

    <main class="min-h-0 flex-1 overflow-y-auto p-4">
      <table class="w-full border-separate border-spacing-y-1.5 text-left">
        <thead class="text-[10px] uppercase tracking-wide text-gray-500">
          <tr><th class="px-3">Title</th><th class="px-3">Kind</th><th class="px-3">Domain</th><th class="px-3">Model</th><th class="px-3">Ver</th><th class="px-3">Created</th><th class="px-3 text-right">Actions</th></tr>
        </thead>
        <tbody>
          @for (a of artifacts(); track a.artifactId) {
            <tr class="bg-gray-900/50">
              <td class="max-w-[280px] truncate rounded-l-lg px-3 py-2 text-xs font-medium text-gray-200">{{ a.title || a.lineageId }}</td>
              <td class="px-3 py-2"><span class="rounded px-1.5 py-0.5 text-[10px] uppercase" [class]="kindClass(a.kind)">{{ a.kind }}</span></td>
              <td class="px-3 py-2 text-[11px] text-gray-400">{{ a.domain || '—' }}</td>
              <td class="px-3 py-2 text-[11px] text-gray-500">{{ a.modelUsed }}</td>
              <td class="px-3 py-2 text-[11px] text-gray-400">v{{ a.version }}</td>
              <td class="px-3 py-2 text-[11px] text-gray-500">{{ a.createdAt | date:'short' }}</td>
              <td class="rounded-r-lg px-3 py-2">
                <div class="flex items-center justify-end gap-1">
                  @if (a.kind === 'document') {
                    @for (f of formats; track f) {
                      <button (click)="download(a, f)" [disabled]="exportingId() === a.artifactId + f"
                        class="rounded border border-gray-700/60 bg-gray-800/60 px-1.5 py-1 text-[10px] text-gray-300 hover:bg-gray-800 disabled:opacity-40"
                        [title]="'Download ' + f">
                        {{ f === 'markdown' ? 'MD' : (f === 'pdf' ? 'PDF' : 'DOCX') }}
                      </button>
                    }
                  }
                  <button (click)="remove(a)" class="rounded border border-gray-700/60 bg-gray-800/60 p-1 text-gray-500 hover:border-red-500/40 hover:text-red-400" title="Delete">
                    <lucide-icon name="trash-2" [size]="12" />
                  </button>
                </div>
              </td>
            </tr>
          } @empty {
            <tr><td colspan="7" class="px-3 py-10 text-center text-xs text-gray-600">No artifacts. Generate research, blueprints, or documents to populate the library.</td></tr>
          }
        </tbody>
      </table>
    </main>
  </div>
  `,
})
export class ArtifactLibraryComponent implements OnInit {
  private readonly store = inject(WorkspaceStoreService);

  protected readonly artifacts = signal<ArtifactMetadata[]>([]);
  protected kindFilter = '';
  protected readonly exportingId = signal<string | null>(null);
  protected readonly formats: ExportFormat[] = ['markdown', 'pdf', 'docx'];

  ngOnInit(): void { this.reload(); }

  protected reload(): void {
    const kind = (this.kindFilter || undefined) as ArtifactKind | undefined;
    this.store.listArtifacts({ kind, latestOnly: true, take: 300 })
      .subscribe({ next: rows => this.artifacts.set(rows), error: () => {} });
  }

  protected download(a: ArtifactMetadata, format: ExportFormat): void {
    const ext = format === 'markdown' ? 'md' : format;
    const slug = (a.title || 'document').toLowerCase().replace(/[^\w]+/g, '-').replace(/^-|-$/g, '').slice(0, 60) || 'document';
    this.exportingId.set(a.artifactId + format);
    this.store.exportArtifact(a.artifactId, format, `${slug}.${ext}`)
      .subscribe({ next: () => this.exportingId.set(null), error: () => this.exportingId.set(null) });
  }

  protected remove(a: ArtifactMetadata): void {
    this.store.deleteArtifact(a.artifactId)
      .subscribe({ next: () => this.artifacts.update(list => list.filter(x => x.artifactId !== a.artifactId)), error: () => {} });
  }

  protected kindClass(kind: ArtifactKind): string {
    switch (kind) {
      case 'research':        return 'bg-cyan-500/15 text-cyan-300';
      case 'blueprint':       return 'bg-violet-500/15 text-violet-300';
      case 'document':        return 'bg-indigo-500/15 text-indigo-300';
      case 'taskSpec':        return 'bg-emerald-500/15 text-emerald-300';
      default:                return 'bg-gray-500/15 text-gray-300';
    }
  }
}
