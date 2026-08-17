import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { WorkspaceStoreService } from '../../core/services/workspace-store.service';
import { CompetitorAnalytics, PainPointAnalytics } from '../../core/models/interfaces';

/** Insights view: recurring pain points + competitor patterns aggregated across saved research runs. */
@Component({
  selector: 'app-insights',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule],
  template: `
  <div class="flex h-full flex-col overflow-hidden bg-gray-950 text-gray-100">
    <header class="flex items-center justify-between border-b border-gray-800/60 px-6 py-4">
      <div>
        <h1 class="text-lg font-semibold text-white">Insights</h1>
        <p class="text-[11px] text-gray-500">Cross-run analysis over {{ painPoints()?.runsAnalyzed ?? 0 }} research run(s).</p>
      </div>
      <div class="flex items-center gap-2">
        <input [(ngModel)]="domain" placeholder="Filter domain (optional)"
          class="rounded-md border border-gray-700/60 bg-gray-800/60 px-2.5 py-1.5 text-[11px] text-gray-300 outline-none focus:border-indigo-500/50" />
        <button (click)="reload()" [disabled]="loading()"
          class="flex h-8 items-center gap-1.5 rounded-lg border border-indigo-500/40 bg-indigo-500/15 px-3 text-[11px] font-medium text-indigo-200 hover:bg-indigo-500/25 disabled:opacity-40">
          @if (loading()) { <lucide-icon name="loader-2" [size]="12" class="animate-spin" /> } @else { <lucide-icon name="bar-chart-3" [size]="12" /> }
          Analyze
        </button>
      </div>
    </header>

    <main class="grid min-h-0 flex-1 grid-cols-1 gap-4 overflow-y-auto p-4 xl:grid-cols-2">
      <!-- Pain points -->
      <section class="rounded-lg border border-gray-800/60 bg-gray-900/60 p-4">
        <h2 class="mb-3 flex items-center gap-1.5 text-sm font-semibold text-white">
          <lucide-icon name="flame" [size]="14" class="text-amber-400" /> Recurring Pain Points
        </h2>
        @for (c of painPoints()?.clusters ?? []; track c.title) {
          <div class="mb-2 rounded-md border border-gray-800/60 bg-gray-900/40 p-2.5">
            <div class="flex items-start justify-between gap-2">
              <span class="text-xs font-medium text-gray-200">{{ c.title }}</span>
              <span class="shrink-0 rounded bg-amber-500/15 px-1.5 py-0.5 text-[10px] text-amber-300">×{{ c.occurrences }}</span>
            </div>
            <div class="mt-1 flex items-center gap-2 text-[10px] text-gray-500">
              <span>avg severity {{ c.avgSeverity }}</span><span>·</span><span>{{ c.domains.join(', ') }}</span>
            </div>
          </div>
        } @empty { <p class="text-[11px] text-gray-600">No pain points yet — run some research first, then Analyze.</p> }
      </section>

      <!-- Competitors -->
      <section class="rounded-lg border border-gray-800/60 bg-gray-900/60 p-4">
        <h2 class="mb-3 flex items-center gap-1.5 text-sm font-semibold text-white">
          <lucide-icon name="users" [size]="14" class="text-cyan-400" /> Competitor Patterns
        </h2>
        @for (p of competitors()?.patterns ?? []; track p.competitorName) {
          <div class="mb-2 rounded-md border border-gray-800/60 bg-gray-900/40 p-2.5">
            <div class="flex items-start justify-between gap-2">
              <span class="text-xs font-medium text-gray-200">{{ p.competitorName }}</span>
              <span class="shrink-0 rounded bg-cyan-500/15 px-1.5 py-0.5 text-[10px] text-cyan-300">×{{ p.occurrences }}</span>
            </div>
            @if (p.featureGaps.length) {
              <ul class="mt-1 list-disc pl-4 text-[10px] text-gray-500">
                @for (g of p.featureGaps.slice(0, 3); track $index) { <li class="truncate">{{ g }}</li> }
              </ul>
            }
          </div>
        } @empty { <p class="text-[11px] text-gray-600">No competitor data yet.</p> }
      </section>
    </main>
  </div>
  `,
})
export class InsightsComponent implements OnInit {
  private readonly store = inject(WorkspaceStoreService);

  protected domain = '';
  protected readonly loading = signal<boolean>(false);
  protected readonly painPoints = signal<PainPointAnalytics | null>(null);
  protected readonly competitors = signal<CompetitorAnalytics | null>(null);

  ngOnInit(): void { this.reload(); }

  protected reload(): void {
    const d = this.domain.trim() || undefined;
    this.loading.set(true);
    let pending = 2;
    const done = () => { if (--pending === 0) this.loading.set(false); };
    this.store.analyticsPainPoints(d).subscribe({ next: r => { this.painPoints.set(r); done(); }, error: done });
    this.store.analyticsCompetitors(d).subscribe({ next: r => { this.competitors.set(r); done(); }, error: done });
  }
}
