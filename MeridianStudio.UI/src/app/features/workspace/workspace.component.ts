import { Component, inject } from '@angular/core';
import { WorkspaceStoreService } from '../../core/services/workspace-store.service';
import { ThemeService } from '../../core/services/theme.service';
import { ModelStatusEvent, ProviderStatusItem, WorkspaceTab } from '../../core/models/interfaces';
import { TrendsDiscoveryComponent } from '../trends-discovery/trends-discovery.component';
import { UseCaseAnalyzerComponent } from '../use-case-analyzer/use-case-analyzer.component';
import { ExecutionPipelineComponent } from '../execution-pipeline/execution-pipeline.component';
import { ArchitecturalBlueprintersComponent } from '../architectural-blueprinter/architectural-blueprinter.component';
import { DocumentStudioComponent } from '../document-studio/document-studio.component';
import { PromptStudioComponent } from '../prompt-studio/prompt-studio.component';
import { ExecutiveSlidesComponent } from '../executive-slides/executive-slides.component';
import { DomainSettingsComponent } from '../domain-settings/domain-settings.component';
import { WhitePaperComponent } from '../whitepaper/whitepaper.component';
import { ArtifactCompareComponent } from '../artifact-compare/artifact-compare.component';
import { ArtifactLibraryComponent } from '../artifact-library/artifact-library.component';
import { InsightsComponent } from '../insights/insights.component';
import { LucideAngularModule } from 'lucide-angular';

interface TabDef {
  id: WorkspaceTab;
  label: string;
  glyph: string;
}

const TABS: TabDef[] = [
  { id: 'research',     label: 'Research',     glyph: '◎' },
  { id: 'use-case',     label: 'Use Case',     glyph: '◇' },
  { id: 'blueprint',    label: 'Blueprint',    glyph: '⬡' },
  { id: 'execution',    label: 'Execution',    glyph: '▶' },
  { id: 'documents',    label: 'Documents',    glyph: '◻' },
  { id: 'prompts',      label: 'Prompts',      glyph: '✦' },
  { id: 'presentation', label: 'Presentation', glyph: '⬭' },
  { id: 'whitepaper',   label: 'White Paper',  glyph: '◨' },
  { id: 'compare',      label: 'Compare',      glyph: '▤' },
  { id: 'library',      label: 'Library',      glyph: '▦' },
  { id: 'insights',     label: 'Insights',     glyph: '◍' },
];

@Component({
  selector: 'app-workspace',
  standalone: true,
  imports: [
    TrendsDiscoveryComponent,
    UseCaseAnalyzerComponent,
    ExecutionPipelineComponent,
    ArchitecturalBlueprintersComponent,
    DocumentStudioComponent,
    PromptStudioComponent,
    ExecutiveSlidesComponent,
    DomainSettingsComponent,
    WhitePaperComponent,
    ArtifactCompareComponent,
    ArtifactLibraryComponent,
    InsightsComponent,
    LucideAngularModule,
  ],
  template: `
    <div class="flex h-screen flex-col overflow-hidden bg-gray-950 text-gray-100">

      <!-- ── App Shell Header ──────────────────────────────────── -->
      <header class="shrink-0 border-b border-gray-800/60 bg-gray-950/90 px-6 py-3 backdrop-blur-sm">
        <div class="flex items-center justify-between gap-4">

          <!-- Wordmark -->
          <div class="flex items-center gap-3">
            <div class="flex h-7 w-7 items-center justify-center rounded-lg
                        bg-gradient-to-br from-indigo-500 to-violet-600 text-xs
                        font-bold text-white shadow-lg shadow-indigo-500/20">
              M
            </div>
            <span class="text-sm font-semibold tracking-tight text-white">Meridian Studio</span>
            <span class="hidden text-xs text-gray-400 sm:block">
              — AI Solution Agent &amp; System Architect Hub
            </span>
          </div>

          <!-- Theme toggle -->
          <button (click)="themeService.toggle()"
            [title]="themeService.theme() === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'"
            class="flex h-8 w-8 items-center justify-center rounded-lg border border-gray-800
                   text-gray-500 transition-all hover:border-indigo-500/30 hover:text-indigo-400">
            @if (themeService.theme() === 'dark') {
              <lucide-icon name="sun" [size]="14" />
            } @else {
              <lucide-icon name="moon" [size]="14" />
            }
          </button>

          <!-- Domain settings button -->
          <button (click)="store.openDomainSettings()"
            class="flex h-8 items-center gap-1.5 rounded-lg border border-gray-800 bg-gray-900/60
                   px-2.5 text-[11px] font-medium text-gray-500 transition-all
                   hover:border-indigo-500/30 hover:bg-indigo-500/8 hover:text-indigo-300">
            <lucide-icon name="globe" [size]="13" />
            <span class="hidden sm:inline">Domains</span>
            @if (store.preferredDomains().length > 0) {
              <span class="rounded-full bg-indigo-500/20 px-1.5 py-0.5 text-[9px]
                           font-bold text-indigo-400">{{ store.preferredDomains().length }}</span>
            }
          </button>

          <!-- Status indicators + model badge -->
          <div class="flex items-center gap-3">
            @if (store.isGeneratingBlueprint()) {
              <span class="flex items-center gap-1.5 text-[11px] font-medium text-violet-400">
                <span class="inline-block h-1.5 w-1.5 animate-pulse rounded-full bg-violet-400"></span>
                Compiling blueprint
              </span>
            }
            @if (store.isGeneratingDocument()) {
              <span class="flex items-center gap-1.5 text-[11px] font-medium text-indigo-400">
                <span class="inline-block h-1.5 w-1.5 animate-pulse rounded-full bg-indigo-400"></span>
                Generating document
              </span>
            }
            <!-- Global error badge -->
            @if (store.error(); as err) {
              <button
                (click)="store.clearError()"
                class="flex max-w-xs items-center gap-2 truncate rounded-lg border
                       border-red-500/25 bg-red-500/10 px-3 py-1.5 text-xs text-red-400
                       transition-colors hover:bg-red-500/20"
              >
                <span class="truncate">{{ err.message }}</span>
                <span class="shrink-0 text-red-600">✕</span>
              </button>
            }
            <!-- Model status badge — click to view all providers -->
            <button
              (click)="store.openProviderModal()"
              title="View AI model routing status"
              class="flex items-center gap-1.5 rounded-lg border border-gray-800/60
                     bg-gray-900/60 px-2.5 py-1.5 transition-colors
                     hover:border-indigo-500/30 hover:bg-gray-900">
              @if (store.currentModelStatus(); as s) {
                <span [class]="modelDotClass(s.type)"></span>
                <span [class]="'text-[11px] font-medium ' + modelTextClass(s.type)">
                  {{ modelLabel(s) }}
                </span>
              } @else {
                <span class="h-1.5 w-1.5 rounded-full bg-gray-800"></span>
                <span class="text-[11px] text-gray-400">Connecting…</span>
              }
              <lucide-icon name="chevron-down" [size]="10"
                           class="ml-0.5 text-gray-600" />
            </button>
          </div>
        </div>

        <!-- ── Tab Navigation ─────────────────────────────────── -->
        <nav class="mt-3 flex gap-0.5" role="tablist" aria-label="Workspace tabs">
          @for (tab of tabs; track tab.id) {
            <button
              role="tab"
              [attr.aria-selected]="store.activeWorkspace() === tab.id"
              [attr.aria-controls]="'panel-' + tab.id"
              [class]="tabClass(tab.id)"
              (click)="store.setActiveWorkspace(tab.id)"
            >
              <span [class]="glyphClass(tab.id)">{{ tab.glyph }}</span>
              <span>{{ tab.label }}</span>
              <!-- Live activity dots -->
              @if (tab.id === 'execution' && store.isExecutingTask()) {
                <span class="ml-0.5 h-1.5 w-1.5 animate-pulse rounded-full bg-blue-400"></span>
              }
              @if (tab.id === 'blueprint' && store.isGeneratingBlueprint()) {
                <span class="ml-0.5 h-1.5 w-1.5 animate-pulse rounded-full bg-violet-400"></span>
              }
              @if (tab.id === 'documents' && store.isGeneratingDocument()) {
                <span class="ml-0.5 h-1.5 w-1.5 animate-pulse rounded-full bg-indigo-400"></span>
              }
            </button>
          }
        </nav>
      </header>

      <!-- ── Workspace Panel ───────────────────────────────────── -->
      <main class="min-h-0 flex-1 overflow-hidden" role="tabpanel">

        @if (store.activeWorkspace() === 'research') {
          <app-trends-discovery class="flex h-full min-h-0" />

        } @else if (store.activeWorkspace() === 'use-case') {
          <app-use-case-analyzer class="flex h-full w-full min-h-0" />

        } @else if (store.activeWorkspace() === 'blueprint') {
          <div class="h-full overflow-y-auto">
            <app-architectural-blueprinter />
          </div>

        } @else if (store.activeWorkspace() === 'execution') {
          <app-execution-pipeline class="flex h-full flex-col" />

        } @else if (store.activeWorkspace() === 'documents') {
          <app-document-studio class="flex h-full" />

        } @else if (store.activeWorkspace() === 'prompts') {
          <app-prompt-studio class="flex h-full" />

        } @else if (store.activeWorkspace() === 'presentation') {
          <app-executive-slides class="flex h-full flex-col" />

        } @else if (store.activeWorkspace() === 'whitepaper') {
          <app-whitepaper class="flex h-full" />

        } @else if (store.activeWorkspace() === 'compare') {
          <app-artifact-compare class="flex h-full" />

        } @else if (store.activeWorkspace() === 'library') {
          <app-artifact-library class="flex h-full" />

        } @else if (store.activeWorkspace() === 'insights') {
          <app-insights class="flex h-full" />

        } @else {
          <!-- Fallback for any future tabs -->
          <div class="flex h-full flex-col items-center justify-center gap-5">
            <div class="flex h-16 w-16 items-center justify-center rounded-2xl
                        border border-gray-800 bg-gray-900 text-2xl text-gray-500">
              {{ activeGlyph() }}
            </div>
            <div class="flex flex-col items-center gap-1.5 text-center">
              <p class="text-sm font-semibold text-gray-500">{{ activeLabel() }} Panel</p>
              <p class="text-xs text-gray-400">This workspace is coming in the next session.</p>
            </div>
          </div>
        }

      </main>

      <!-- ── Domain Settings slide-over ──────────────────────── -->
      @if (store.isDomainSettingsOpen()) {
        <div class="fixed inset-0 z-40 bg-black/50 backdrop-blur-sm"
             (click)="store.closeDomainSettings()"></div>
        <aside class="fixed right-0 top-0 z-50 h-full w-96 border-l border-gray-800
                      bg-gray-950 shadow-2xl shadow-black/60">
          <app-domain-settings />
        </aside>
      }

      <!-- ── Provider Status Modal ─────────────────────────────── -->
      @if (store.isProviderModalOpen()) {
        <div class="fixed inset-0 z-40 bg-black/60 backdrop-blur-sm"
             (click)="store.closeProviderModal()"></div>

        <div class="pointer-events-none fixed inset-0 z-50 flex items-center justify-center p-4">
          <div class="pointer-events-auto w-full max-w-md rounded-2xl border border-gray-800
                      bg-gray-950 shadow-2xl shadow-black/60">

            <!-- Modal header -->
            <div class="flex items-start justify-between border-b border-gray-800/60 px-5 py-4">
              <div>
                <h2 class="text-sm font-semibold text-white">AI Model Routing</h2>
                <p class="mt-0.5 text-[11px] text-gray-500">
                  Providers are tried in priority order — first to respond wins
                </p>
              </div>
              <button (click)="store.closeProviderModal()"
                      class="ml-4 flex h-6 w-6 items-center justify-center rounded-md
                             text-gray-600 transition-colors hover:bg-gray-800 hover:text-gray-300">
                <lucide-icon name="x" [size]="13" />
              </button>
            </div>

            <!-- Provider list -->
            <div class="divide-y divide-gray-800/40">
              @if (store.providerStatuses().length === 0) {
                <div class="flex items-center justify-center py-8 text-xs text-gray-600">
                  Loading…
                </div>
              }
              @for (p of store.providerStatuses(); track p.name) {
                <div class="flex items-start gap-3 px-5 py-3.5">

                  <!-- Priority badge -->
                  <span class="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center
                               rounded-full border border-gray-700 text-[10px] font-bold
                               text-gray-500">{{ p.priority }}</span>

                  <!-- Provider info -->
                  <div class="min-w-0 flex-1">
                    <div class="flex flex-wrap items-center gap-2">
                      <span class="text-xs font-semibold text-gray-200">
                        {{ providerShortName(p.name) }}
                      </span>
                      @if (providerModelId(p.name)) {
                        <span class="text-[9px] text-gray-600">{{ providerModelId(p.name) }}</span>
                      }
                      <span [class]="providerStatusBadgeClass(p.status)">
                        {{ providerStatusLabel(p.status) }}
                      </span>
                    </div>
                    <p class="mt-1 text-[11px] leading-relaxed text-gray-500">{{ p.reason }}</p>
                  </div>
                </div>
              }
            </div>

            <!-- Footer hint -->
            <div class="border-t border-gray-800/60 px-5 py-3">
              <p class="text-[10px] text-gray-600">
                Add a key:
                <code class="ml-1 rounded bg-gray-800/80 px-1.5 py-0.5 font-mono
                             text-[9px] text-gray-400">
                  dotnet user-secrets set "LLM:&lt;Provider&gt;:ApiKey" "…"
                </code>
              </p>
            </div>

          </div>
        </div>
      }

    </div>
  `,
})
export class WorkspaceComponent {
  protected readonly store        = inject(WorkspaceStoreService);
  protected readonly themeService = inject(ThemeService);
  protected readonly tabs = TABS;

  protected tabClass(id: WorkspaceTab): string {
    const base =
      'flex h-9 min-w-[44px] items-center gap-1.5 rounded-md px-3 text-xs font-medium ' +
      'transition-all duration-150 focus:outline-none focus:ring-2 focus:ring-indigo-500/30';
    return this.store.activeWorkspace() === id
      ? `${base} bg-indigo-600 text-white shadow-md shadow-indigo-600/20`
      : `${base} text-gray-500 hover:bg-gray-800/60 hover:text-gray-200`;
  }

  protected glyphClass(id: WorkspaceTab): string {
    const base = 'text-[11px] leading-none';
    return this.store.activeWorkspace() === id
      ? `${base} text-white`        // active: white glyph on the indigo button
      : `${base} text-indigo-400`;  // inactive: light indigo (dark) / deep indigo (light)
  }

  protected activeGlyph(): string {
    return TABS.find(t => t.id === this.store.activeWorkspace())?.glyph ?? '○';
  }

  protected activeLabel(): string {
    return TABS.find(t => t.id === this.store.activeWorkspace())?.label ?? '';
  }

  protected modelDotClass(type: string): string {
    const base = 'h-1.5 w-1.5 rounded-full';
    if (type === 'attempting') return `${base} animate-pulse bg-blue-400`;
    if (type === 'succeeded')  return `${base} bg-emerald-400`;
    if (type === 'failed')     return `${base} bg-red-500`;
    if (type === 'fallback')   return `${base} bg-amber-400`;
    return `${base} bg-gray-600`;
  }

  protected modelTextClass(type: string): string {
    if (type === 'attempting') return 'text-blue-300';
    if (type === 'succeeded')  return 'text-emerald-300';
    if (type === 'failed')     return 'text-red-400';
    if (type === 'fallback')   return 'text-amber-300';
    return 'text-gray-400';
  }

  protected modelLabel(s: ModelStatusEvent): string {
    // "Gemini (gemini-2.5-flash)" → "Gemini"
    const short = s.provider.replace(/\s*\([^)]+\)$/, '').trim();
    if (s.type === 'attempting') return `${short} · ${s.operation}`;
    if (s.type === 'succeeded')  return short;
    if (s.type === 'failed')     return `${short} · retry`;
    if (s.type === 'fallback')   return 'Offline';
    return 'Ready';
  }

  // ── Provider modal helpers ────────────────────────────────────────────────

  protected providerShortName(name: string): string {
    return name.replace(/\s*\([^)]+\)$/, '').trim();
  }

  protected providerModelId(name: string): string {
    return name.match(/\(([^)]+)\)/)?.[1] ?? '';
  }

  protected providerStatusLabel(status: ProviderStatusItem['status']): string {
    switch (status) {
      case 'active':          return 'Active';
      case 'idle':            return 'Ready';
      case 'failed':          return 'Failed';
      case 'quota':           return 'Rate limited';
      case 'unavailable':     return 'Unavailable';
      case 'not-configured':  return 'Not configured';
      case 'fallback':        return 'Offline fallback';
    }
  }

  protected providerStatusBadgeClass(status: ProviderStatusItem['status']): string {
    const base = 'inline-flex items-center rounded-full px-1.5 py-0.5 ' +
                 'text-[9px] font-bold uppercase tracking-wide';
    switch (status) {
      case 'active':          return `${base} bg-emerald-500/15 text-emerald-400`;
      case 'idle':            return `${base} bg-blue-500/15 text-blue-400`;
      case 'failed':          return `${base} bg-red-500/15 text-red-400`;
      case 'quota':           return `${base} bg-amber-500/15 text-amber-400`;
      case 'unavailable':     return `${base} bg-orange-500/15 text-orange-400`;
      case 'not-configured':  return `${base} bg-gray-800 text-gray-600`;
      case 'fallback':        return `${base} bg-amber-500/15 text-amber-400`;
    }
  }
}
