import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { WorkspaceStoreService } from '../../core/services/workspace-store.service';
import {
  PrioritizedItem, PainPoint, DimensionWeights,
  compositeScore, priorityBadge,
  ImportanceLevel, DimensionImportance, IMPORTANCE_LEVELS, IMPORTANCE_LABELS,
  defaultImportanceForDomain, importanceToWeights, PRESET_IMPORTANCE,
} from '../../core/models/interfaces';

const DIMENSION_DEFS: { key: keyof DimensionWeights; label: string; description: string }[] = [
  { key: 'businessValue',            label: 'Business Value',            description: 'Revenue/cost impact for the buyer' },
  { key: 'marketUrgency',            label: 'Market Urgency',            description: 'How fast buyers are moving NOW' },
  { key: 'feasibility',              label: 'Feasibility',               description: 'Can an IT firm deliver in <18 months?' },
  { key: 'competitiveGap',           label: 'Competitive Gap',           description: 'How underserved by existing vendors' },
  { key: 'implementationDifficulty', label: 'Difficulty (inverse)',       description: 'Lower = easier to deliver' },
  { key: 'regulatoryTailwind',       label: 'Regulatory Tailwind',       description: 'Compliance/regulation forcing adoption' },
  { key: 'strategicFit',             label: 'Strategic Fit',             description: 'Aligns with your firm\'s capabilities' },
  { key: 'aiFitness',                label: 'AI Fitness',                description: 'How AI-native is this problem?' },
];

@Component({
  selector: 'app-trends-discovery',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule],
  template: `
    <div class="flex h-full min-h-0 w-full overflow-hidden">

      <!-- ══ Left pane: Selected subdomains ══════════════════════════════════ -->
      <aside class="flex w-72 shrink-0 flex-col border-r border-gray-800/60 bg-gray-950/30 xl:w-80">

        <div class="flex shrink-0 items-center justify-between border-b border-gray-800/60 px-4 py-3.5">
          <div class="flex items-center gap-2">
            <lucide-icon name="layers" [size]="14" class="text-indigo-400" />
            <span class="text-xs font-semibold uppercase tracking-wider text-gray-500">Research Areas</span>
            @if (store.selectedSubdomains().length > 0) {
              <span class="rounded-full bg-indigo-500/20 px-1.5 py-0.5 text-[9px] font-bold text-indigo-400">
                {{ store.selectedSubdomains().length }}
              </span>
            }
          </div>
          <button (click)="store.openDomainSettings()"
            class="text-[10px] text-gray-400 transition-colors hover:text-indigo-400 underline">
            Manage
          </button>
        </div>

        <!-- Subdomain list grouped by domain -->
        <div class="min-h-0 flex-1 overflow-y-auto p-3">
          @if (store.selectedSubdomains().length === 0) {
            <div class="flex flex-col items-center gap-3 py-12 text-center">
              <lucide-icon name="layers" [size]="28" class="text-gray-800" />
              <p class="text-xs leading-relaxed text-gray-400">
                No research areas selected.<br>
                Click <span class="text-indigo-400">Manage</span> to choose<br>domains and subdomains.
              </p>
            </div>
          } @else {
            @for (group of groupedSubdomains(); track group.domain) {
              <!-- Domain group header -->
              <p class="mt-3 mb-1 px-1 text-[9px] font-bold uppercase tracking-wider text-gray-600 first:mt-0">
                {{ group.domain }}
              </p>
              @for (sub of group.subdomains; track sub.subdomain) {
                <!-- Subdomain row — div not button so action icons can be real buttons -->
                <div class="group/sub mb-0.5 flex items-center gap-1">
                  <!-- Main clickable area -->
                  <div [class]="subdomainRowClass(sub.domain, sub.subdomain)"
                       (click)="store.setActiveSubdomain(sub.domain, sub.subdomain)"
                       role="button" tabindex="0"
                       (keydown.enter)="store.setActiveSubdomain(sub.domain, sub.subdomain)">
                    <span class="shrink-0 text-[8px]"
                          [class]="sub.hasResults ? 'text-emerald-400' : 'text-gray-700'">●</span>
                    <span class="flex-1 truncate text-[11px]">{{ sub.subdomain }}</span>
                  </div>
                  <!-- Action icons — always visible, proper buttons -->
                  <button (click)="openDimensionDrawer(sub.domain, sub.subdomain)"
                    title="Configure dimension weights"
                    class="flex h-6 w-6 shrink-0 items-center justify-center rounded
                           border border-gray-800 bg-gray-900 text-gray-500
                           transition-colors hover:border-violet-500/40 hover:text-violet-400">
                    <lucide-icon name="sliders-horizontal" [size]="11" />
                  </button>
                  <button (click)="store.removeSubdomain(sub.domain, sub.subdomain)"
                    title="Remove subdomain"
                    class="flex h-6 w-6 shrink-0 items-center justify-center rounded
                           border border-gray-800 bg-gray-900 text-gray-600
                           transition-colors hover:border-red-500/30 hover:text-red-400">
                    <lucide-icon name="x" [size]="11" />
                  </button>
                </div>
              }
            }
          }
        </div>

        <!-- Add + Analyze footer -->
        <div class="shrink-0 border-t border-gray-800/60 p-3 flex flex-col gap-2">
          <button (click)="store.openDomainSettings()"
            class="flex h-8 w-full items-center justify-center gap-1.5 rounded-lg border
                   border-dashed border-gray-700 text-[11px] text-gray-500 transition-colors
                   hover:border-indigo-500/40 hover:text-indigo-400">
            <lucide-icon name="plus" [size]="12" />
            Add from taxonomy
          </button>

          <!-- Custom domain + subdomain entry -->
          <div class="rounded-lg border border-gray-800 bg-gray-900/60 p-2.5 flex flex-col gap-1.5">
            <p class="text-[9px] font-semibold uppercase tracking-wider text-gray-600">Or type custom</p>
            <input type="text"
              [ngModel]="customDomain()"
              (ngModelChange)="customDomain.set($event)"
              placeholder="Domain (e.g. Healthcare)"
              class="h-7 w-full rounded border border-gray-800 bg-gray-950/60 px-2
                     text-[11px] text-gray-300 placeholder-gray-600
                     focus:border-indigo-500/40 focus:outline-none" />
            <input type="text"
              [ngModel]="customSubdomain()"
              (ngModelChange)="customSubdomain.set($event)"
              (keyup.enter)="addCustomSubdomain()"
              placeholder="Sub-domain (e.g. AI Diagnostics)"
              class="h-7 w-full rounded border border-gray-800 bg-gray-950/60 px-2
                     text-[11px] text-gray-300 placeholder-gray-600
                     focus:border-indigo-500/40 focus:outline-none" />
            <button (click)="addCustomSubdomain()"
              [disabled]="!customDomain().trim() || !customSubdomain().trim()"
              class="flex h-7 w-full items-center justify-center gap-1 rounded
                     bg-indigo-500/15 text-[11px] font-medium text-indigo-400
                     transition-colors hover:bg-indigo-500/25
                     disabled:cursor-not-allowed disabled:opacity-40">
              <lucide-icon name="plus" [size]="11" />
              Add
            </button>
          </div>
          @if (store.activeSubdomain(); as active) {
            <button (click)="onAnalyzeActive()"
              [disabled]="store.isLoading()"
              class="flex h-9 w-full items-center justify-center gap-1.5 rounded-lg
                     bg-gradient-to-r from-indigo-600 to-violet-600 text-xs font-medium
                     text-white shadow-md transition-all
                     hover:from-indigo-500 hover:to-violet-500
                     disabled:cursor-not-allowed disabled:opacity-40">
              @if (store.isLoading()) {
                <lucide-icon name="loader-2" [size]="13" class="animate-spin" />
                <span>Searching live sources…</span>
              } @else {
                <lucide-icon name="search" [size]="13" />
                <span>Analyze: {{ active.subdomain | slice:0:22 }}{{ active.subdomain.length > 22 ? '…' : '' }}</span>
              }
            </button>
          }
        </div>
      </aside>

      <!-- ══ Right panel: Results ══════════════════════════════════════════════ -->
      <div class="relative flex min-h-0 flex-1 flex-col">

        @if (store.hasResearchResults() || store.isLoading()) {

          <!-- Pinned header -->
          <div class="shrink-0 flex items-start justify-between gap-4 px-6 pt-5 pb-2 lg:px-8">
            <div class="flex flex-col gap-1">
              <div class="flex items-center gap-2.5">
                <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-indigo-500/15">
                  <lucide-icon name="sparkles" [size]="16" class="text-indigo-400" />
                </div>
                <h2 class="text-lg font-semibold tracking-tight text-white">
                  Research &amp; Trends Discovery
                </h2>
              </div>
              @if (store.currentResearchData(); as data) {
                <div class="ml-[42px] flex items-center gap-2">
                  <span class="text-[10px] text-gray-500">{{ data.domain }}</span>
                  @if (data.liveSourcesQueried && data.liveSourcesQueried.length > 0) {
                    <span class="rounded-full bg-emerald-500/10 border border-emerald-500/20
                                 px-2 py-0.5 text-[9px] text-emerald-400">
                      Live: {{ data.liveSourcesQueried.join(' · ') }}
                    </span>
                  }
                </div>
              }
            </div>
            @if (store.currentResearchData(); as data) {
              <div class="flex shrink-0 items-center gap-3">
                <button (click)="store.startWhitePaperFromResearch()"
                  class="flex h-8 items-center gap-1.5 rounded-lg border border-violet-500/30 bg-violet-500/10
                         px-3 text-[11px] font-medium text-violet-300 transition-all hover:border-violet-500/50 hover:bg-violet-500/20"
                  title="Generate a market/competitive white paper for this domain·subdomain">
                  <lucide-icon name="file-text" [size]="13" /> White Paper
                </button>
                <div class="flex flex-col items-end">
                  <span class="text-2xl font-bold tabular-nums text-white">{{ data.items.length }}</span>
                  <span class="text-xs text-gray-400">opportunities</span>
                </div>
              </div>
            }
          </div>

          <!-- Result tabs -->
          <div class="shrink-0 flex gap-0 border-b border-gray-800/60 px-6 lg:px-8">
            @for (tab of resultTabs; track tab.key) {
              <button (click)="activeTab.set(tab.key)"
                [class]="tabClass(tab.key)">
                {{ tab.label }}
                @if (tab.count() > 0) {
                  <span class="ml-1.5 rounded-full bg-current/20 px-1.5 py-0.5 text-[9px] font-bold opacity-70">
                    {{ tab.count() }}
                  </span>
                }
              </button>
            }
          </div>

          <!-- Scrollable results -->
          <div class="flex-1 overflow-y-auto">
            <div class="flex flex-col gap-6 px-6 pb-8 pt-4 lg:px-8">

              <!-- TAB: Opportunities -->
              @if (activeTab() === 'opportunities') {

                <!-- Competitor strip -->
                @if (store.currentResearchData()?.competitorInsights?.length) {
                  <div class="flex gap-3 overflow-x-auto pb-1">
                    @for (ci of store.currentResearchData()!.competitorInsights; track $index) {
                      <div class="min-w-[200px] shrink-0 rounded-xl border border-gray-800/60
                                  bg-gray-900/40 px-4 py-3 text-xs">
                        <p class="font-semibold text-gray-300">{{ ci.competitorName }}</p>
                        <p class="mt-0.5 line-clamp-1 text-gray-400">{{ ci.featureGap }}</p>
                        <p class="mt-1.5 font-bold text-indigo-400">{{ ci.impactScore }}</p>
                      </div>
                    }
                  </div>
                }

                @if (store.hasResearchResults()) {
                  <div class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
                    @for (item of store.currentResearchData()!.items; track item.id) {
                      @let composite = getComposite(item);
                      @let badge = getBadge(item);
                      <article [class]="cardClass()"
                        (click)="onSelect(item)" (keydown.enter)="onSelect(item)"
                        tabindex="0" role="button" [attr.aria-label]="'Select: ' + item.name">

                        <!-- Priority badge + composite score -->
                        <div class="flex items-center justify-between gap-2">
                          <span [class]="priorityBadgeClass(badge)">
                            {{ badge }}
                            @if (composite > 0) { {{ composite.toFixed(1) }} }
                          </span>
                          <div class="flex items-center gap-2">
                            <span class="text-[10px] font-bold tabular-nums text-gray-400">
                              Value {{ item.value }}/10
                            </span>
                          </div>
                        </div>

                        <!-- Composite score bar -->
                        @if (composite > 0) {
                          <div class="h-1 w-full overflow-hidden rounded-full bg-gray-800">
                            <div [class]="compositeBarClass(badge)"
                                 [style.width]="(composite * 10) + '%'"
                                 class="h-full rounded-full transition-all duration-700"></div>
                          </div>
                        }

                        <div class="flex flex-1 flex-col gap-1.5">
                          <h3 class="line-clamp-2 text-sm font-semibold leading-snug text-white
                                     transition-colors group-hover:text-indigo-300">
                            {{ item.name }}
                          </h3>
                          <p class="line-clamp-2 text-xs leading-relaxed text-gray-500">
                            {{ item.description }}
                          </p>
                        </div>

                        <div class="flex flex-col gap-2.5 rounded-xl bg-gray-950/60 p-3">
                          <div>
                            <div class="mb-1.5 flex items-center justify-between">
                              <span class="text-[10px] font-medium uppercase tracking-wider text-gray-400">Business Value</span>
                              <span [class]="valueTextClass(item.value)" class="text-[10px] font-bold tabular-nums">{{ item.value }}/10</span>
                            </div>
                            <div class="h-1.5 overflow-hidden rounded-full bg-gray-800">
                              <div [class]="valueBarClass(item.value)" [style.width]="(item.value * 10) + '%'"
                                   class="h-full rounded-full transition-all duration-700"></div>
                            </div>
                          </div>
                          <div class="grid grid-cols-2 gap-3">
                            <div>
                              <span class="mb-1.5 block text-[10px] font-medium uppercase tracking-wider text-gray-400">Difficulty</span>
                              <div class="flex gap-0.5">
                                @for (seg of [1,2,3]; track seg) {
                                  <div class="h-1.5 flex-1 rounded-full transition-all duration-500"
                                       [class]="difficultySegClass(item.difficulty, seg)"></div>
                                }
                              </div>
                              <span class="mt-1 block text-[10px] text-gray-400">{{ difficultyLabel(item.difficulty) }}</span>
                            </div>
                            <div>
                              <span class="mb-1.5 block text-[10px] font-medium uppercase tracking-wider text-gray-400">Urgency</span>
                              <div class="h-1.5 overflow-hidden rounded-full bg-gray-800">
                                <div class="h-full rounded-full bg-gradient-to-r from-orange-600 to-red-500 transition-all duration-700"
                                     [style.width]="(item.urgency * 10) + '%'"></div>
                              </div>
                              <span class="mt-1 block text-[10px] text-gray-400">{{ item.urgency }}/10</span>
                            </div>
                          </div>
                        </div>

                        @if (item.feasibilityScore > 0) {
                          <div class="rounded-xl border p-3 text-[11px]" [class]="feasibilityPanelClass(item.feasibilityScore)">
                            <div class="mb-1.5 flex items-center justify-between">
                              <span class="font-semibold uppercase tracking-wider text-[9px]" [class]="feasibilityLabelClass(item.feasibilityScore)">
                                Feasibility · {{ feasibilityLabel(item.feasibilityScore) }}
                              </span>
                              <span class="font-bold tabular-nums text-[10px]" [class]="feasibilityLabelClass(item.feasibilityScore)">
                                {{ item.feasibilityScore }}/10
                              </span>
                            </div>
                            @if (item.feasibilityAnalysis) {
                              <p class="leading-relaxed text-gray-400 line-clamp-3">{{ item.feasibilityAnalysis }}</p>
                            }
                          </div>
                        } @else if (item.rationale) {
                          <p class="line-clamp-2 text-[11px] leading-relaxed text-gray-400 italic">"{{ item.rationale }}"</p>
                        }

                        <div class="flex items-center justify-between border-t border-gray-800/60 pt-2.5">
                          <button (click)="$event.stopPropagation(); store.startWhitePaperFromResearch(item.id)"
                            class="flex items-center gap-1 rounded-md border border-violet-500/25 px-2 py-1
                                   text-[10px] text-violet-300/80 transition-colors hover:border-violet-500/50 hover:text-violet-300"
                            title="Generate a white paper focused on this opportunity">
                            <lucide-icon name="file-text" [size]="10" /> White Paper
                          </button>
                          @if (item.realLifeValue) {
                            <span class="mx-2 line-clamp-1 flex-1 text-[10px] text-gray-500">{{ item.realLifeValue }}</span>
                          }
                          <span class="ml-auto flex items-center gap-1 text-[10px] font-medium text-indigo-500
                                       transition-colors group-hover:text-indigo-400">
                            Select
                            <lucide-icon name="arrow-right" [size]="10" class="transition-transform group-hover:translate-x-0.5" />
                          </span>
                        </div>
                        <div class="pointer-events-none absolute inset-0 rounded-2xl ring-1 ring-inset
                                    ring-indigo-500/0 transition-all duration-300 group-hover:ring-indigo-500/25"></div>
                      </article>
                    }
                  </div>

                  @if (store.canLoadMore()) {
                    <div class="flex justify-center py-2">
                      <button (click)="onLoadMore()" [disabled]="store.isLoading()"
                        class="group flex h-11 min-w-[240px] items-center justify-center gap-2.5
                               rounded-xl border border-gray-700/60 px-6 text-sm font-medium text-gray-400
                               transition-all hover:border-indigo-500/40 hover:bg-indigo-500/5 hover:text-indigo-300
                               disabled:cursor-not-allowed disabled:opacity-50">
                        @if (store.isLoading()) {
                          <lucide-icon name="loader-2" [size]="15" class="animate-spin text-indigo-400" />
                          <span class="text-indigo-400">Expanding…</span>
                        } @else {
                          <lucide-icon name="chevron-down" [size]="15" />
                          <span>Load More Priorities</span>
                        }
                      </button>
                    </div>
                  }

                } @else {
                  <!-- Loading skeleton -->
                  <div class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
                    @for (n of skeletons; track n) {
                      <div class="flex animate-pulse flex-col gap-4 rounded-2xl border border-gray-800/60 bg-gray-900 p-5">
                        <div class="flex justify-between">
                          <div class="h-6 w-20 rounded-md bg-gray-800"></div>
                          <div class="h-4 w-12 rounded bg-gray-800"></div>
                        </div>
                        <div class="space-y-2">
                          <div class="h-4 w-3/4 rounded bg-gray-800"></div>
                          <div class="h-3 w-full rounded bg-gray-800"></div>
                        </div>
                        <div class="h-24 rounded-xl bg-gray-800/50"></div>
                      </div>
                    }
                  </div>
                }
              }

              <!-- TAB: Pain Points -->
              @if (activeTab() === 'painPoints') {
                @let painPoints = store.currentResearchData()?.painPoints ?? [];
                @if (painPoints.length > 0) {
                  <div class="grid grid-cols-1 gap-4 md:grid-cols-2">
                    @for (pp of painPoints; track pp.id) {
                      <div class="rounded-2xl border border-gray-800 bg-gray-900 p-5">
                        <div class="mb-3 flex items-start justify-between gap-2">
                          <span [class]="severityBadge(pp.frequency)">
                            {{ pp.frequency.toUpperCase() }} · {{ pp.severity }}/10
                          </span>
                        </div>
                        <h3 class="mb-1 text-sm font-semibold text-white">{{ pp.title }}</h3>
                        <p class="mb-3 text-[11px] leading-relaxed text-gray-400">{{ pp.description }}</p>
                        <p class="mb-3 text-[10px] text-gray-600">Affects: <span class="text-gray-400">{{ pp.affectedSegment }}</span></p>
                        @if (pp.relatedOpportunityIds?.length) {
                          <div class="flex flex-wrap gap-1.5">
                            @for (id of pp.relatedOpportunityIds; track id) {
                              @let opp = findOpportunity(id);
                              @if (opp) {
                                <button (click)="activeTab.set('opportunities'); onSelect(opp)"
                                  class="rounded-md border border-indigo-500/25 bg-indigo-500/10
                                         px-2 py-0.5 text-[10px] text-indigo-400
                                         transition-colors hover:bg-indigo-500/20">
                                  ↗ {{ opp.name | slice:0:28 }}{{ opp.name.length > 28 ? '…' : '' }}
                                </button>
                              }
                            }
                          </div>
                        }
                        @if (pp.liveSource) {
                          <p class="mt-2 text-[9px] text-gray-700">Source: {{ pp.liveSource }}</p>
                        }
                      </div>
                    }
                  </div>
                } @else {
                  <div class="flex flex-col items-center gap-3 py-16 text-center">
                    <lucide-icon name="alert-circle" [size]="28" class="text-gray-700" />
                    <p class="text-xs text-gray-500">No pain points recorded for this analysis.</p>
                  </div>
                }
              }

            </div>
          </div>

        } @else {
          <!-- Empty state -->
          <div class="absolute inset-0 flex flex-col items-center justify-center gap-6 px-8 text-center">
            <div class="relative">
              <div class="flex h-20 w-20 items-center justify-center rounded-2xl
                          border border-indigo-500/20 bg-indigo-500/5">
                <lucide-icon name="sparkles" [size]="32" class="text-indigo-400" />
              </div>
              <div class="absolute -bottom-1 -right-1 flex h-7 w-7 items-center justify-center
                          rounded-lg bg-gradient-to-br from-indigo-600 to-violet-600 shadow-lg">
                <lucide-icon name="bar-chart-3" [size]="13" class="text-white" />
              </div>
            </div>
            <div class="flex flex-col gap-2">
              <h2 class="text-xl font-semibold tracking-tight text-white">Research &amp; Trends Discovery</h2>
              <p class="text-sm text-gray-500">Select a domain, choose subdomains, configure dimensions, then Analyze.</p>
            </div>
          </div>
        }

      </div>

      <!-- ══ Dimension Weights Drawer (right slide-in) ════════════════════════ -->
      @if (dimensionDrawerOpen()) {
        <div class="fixed inset-y-0 right-0 z-50 flex w-80 flex-col
                    border-l border-gray-700/40 bg-gray-950 shadow-[-8px_0_32px_rgba(0,0,0,0.6)]">

          <!-- Drawer header -->
          <div class="flex shrink-0 items-center justify-between border-b border-gray-800
                      bg-gray-900 px-4 py-3">
            <div>
              <p class="text-xs font-semibold text-white">Dimension Weights</p>
              <p class="text-[10px] text-gray-500">{{ drawerSubdomain() }}</p>
            </div>
            <button (click)="closeDimensionDrawer()"
              class="rounded p-1 text-gray-600 hover:text-gray-400">
              <lucide-icon name="x" [size]="14" />
            </button>
          </div>

          <!-- Importance controls -->
          <div class="flex-1 overflow-y-auto bg-gray-950 px-4 py-3 space-y-4">
            <p class="text-[10px] leading-snug text-gray-500">
              Set how much each factor matters — no need to make anything add up. We balance them for you.
            </p>

            <!-- Preset chips -->
            <div class="flex flex-wrap gap-1.5">
              <button (click)="applyDomainDefault()"
                class="rounded-full border border-gray-700 bg-gray-900 px-2.5 py-1 text-[10px] text-gray-400
                       transition-colors hover:border-violet-500/40 hover:text-violet-300">
                Domain default
              </button>
              @for (p of PRESETS; track p.id) {
                <button (click)="applyPreset(p)"
                  class="rounded-full border border-gray-700 bg-gray-900 px-2.5 py-1 text-[10px] text-gray-400
                         transition-colors hover:border-violet-500/40 hover:text-violet-300">
                  {{ p.label }}
                </button>
              }
            </div>

            @for (dim of DIMENSIONS; track dim.key) {
              <div>
                <div class="mb-1.5 flex items-center justify-between gap-2">
                  <div class="min-w-0">
                    <p class="text-[11px] font-medium text-gray-300">{{ dim.label }}</p>
                    <p class="text-[9px] text-gray-600">{{ dim.description }}</p>
                  </div>
                  <span class="shrink-0 text-[9px] tabular-nums text-gray-600">{{ effectiveWeights()[dim.key] }}%</span>
                </div>
                <!-- 5-level segmented importance control -->
                <div class="flex gap-1" role="radiogroup" [attr.aria-label]="dim.label">
                  @for (lvl of LEVELS; track lvl) {
                    <button
                      (click)="onImportanceChange(dim.key, lvl)"
                      [attr.aria-pressed]="localImportance()[dim.key] === lvl"
                      [title]="LEVEL_LABELS[lvl]"
                      class="flex-1 rounded py-1 text-[9px] font-medium transition-colors"
                      [class]="localImportance()[dim.key] === lvl
                               ? 'bg-violet-500/25 text-violet-200 ring-1 ring-violet-500/40'
                               : 'bg-gray-800/60 text-gray-500 hover:bg-gray-800 hover:text-gray-300'">
                      {{ LEVEL_LABELS[lvl] }}
                    </button>
                  }
                </div>
              </div>
            }
          </div>

          <!-- Footer -->
          <div class="shrink-0 border-t border-gray-800 bg-gray-900 px-4 py-3">
            <div class="flex gap-2">
              <button (click)="resetToDomainDefault()"
                class="flex-1 rounded-lg border border-gray-700 px-3 py-1.5 text-[11px] text-gray-500
                       transition-colors hover:text-gray-300">
                Reset
              </button>
              <button (click)="applyImportance()"
                class="flex-1 rounded-lg bg-violet-500/20 border border-violet-500/30
                       px-3 py-1.5 text-[11px] font-medium text-violet-300
                       transition-colors hover:bg-violet-500/30">
                Apply
              </button>
            </div>
          </div>
        </div>
      }

    </div>
  `,
})
export class TrendsDiscoveryComponent implements OnInit, OnDestroy {
  protected readonly store = inject(WorkspaceStoreService);
  protected readonly DIMENSIONS = DIMENSION_DEFS;
  protected readonly skeletons  = [1, 2, 3, 4, 5, 6];

  protected readonly activeTab       = signal<'opportunities' | 'painPoints'>('opportunities');
  protected readonly customDomain    = signal('');
  protected readonly customSubdomain = signal('');

  // Dimension drawer state
  protected readonly dimensionDrawerOpen = signal(false);
  protected readonly drawerSubdomain     = signal('');
  private _drawerDomain  = '';
  private _drawerSubname = '';
  protected readonly LEVELS = IMPORTANCE_LEVELS;
  protected readonly LEVEL_LABELS = IMPORTANCE_LABELS;
  protected readonly PRESETS = PRESET_IMPORTANCE;
  protected readonly localImportance = signal<DimensionImportance>(defaultImportanceForDomain(''));
  // Live "effective %" preview so users can still see the normalized result of their choices.
  protected readonly effectiveWeights = computed(() => importanceToWeights(this.localImportance()));

  // Result tabs definition
  protected readonly resultTabs = [
    { key: 'opportunities' as const, label: 'Opportunities',
      count: computed(() => this.store.currentResearchData()?.items.length ?? 0) },
    { key: 'painPoints'   as const, label: 'Pain Points',
      count: computed(() => this.store.currentResearchData()?.painPoints?.length ?? 0) },
  ];

  // Group selected subdomains by domain
  protected readonly groupedSubdomains = computed(() => {
    type SubEntry = ReturnType<typeof this.store.selectedSubdomains>[0];
    const map = new Map<string, SubEntry[]>();
    for (const sub of this.store.selectedSubdomains()) {
      const list = map.get(sub.domain) ?? [];
      list.push(sub);
      map.set(sub.domain, list);
    }
    return [...map.entries()].map(([domain, subdomains]) => ({ domain, subdomains }));
  });

  private hintTimer?: ReturnType<typeof setInterval>;
  ngOnInit(): void {
    this.hintTimer = setInterval(() => {}, 9999); // keep alive
  }
  ngOnDestroy(): void { clearInterval(this.hintTimer); }

  // ── Actions ───────────────────────────────────────────────────────────────

  protected addCustomSubdomain(): void {
    const d = this.customDomain().trim();
    const s = this.customSubdomain().trim();
    if (!d || !s) return;
    this.store.addSubdomain(d, s);
    this.store.setActiveSubdomain(d, s);
    this.customDomain.set('');
    this.customSubdomain.set('');
  }

  protected onAnalyzeActive(): void {
    const active = this.store.activeSubdomain();
    if (!active || this.store.isLoading()) return;
    this.store.analyzeSubdomain(active.domain, active.subdomain);
  }

  protected onLoadMore(): void {
    this.store.loadMoreSolutions().subscribe();
  }

  protected onSelect(item: PrioritizedItem): void {
    this.store.setSelectedSolution(item);
    this.store.setActiveWorkspace('blueprint');
  }

  protected findOpportunity(id: string): PrioritizedItem | undefined {
    return this.store.currentResearchData()?.items.find(i => i.id === id);
  }

  // ── Dimension drawer ──────────────────────────────────────────────────────

  protected openDimensionDrawer(domain: string, subdomain: string): void {
    this._drawerDomain  = domain;
    this._drawerSubname = subdomain;
    this.drawerSubdomain.set(subdomain);
    this.localImportance.set({ ...this.store.getSubdomainImportance(domain, subdomain) });
    this.dimensionDrawerOpen.set(true);
  }

  protected closeDimensionDrawer(): void {
    this.dimensionDrawerOpen.set(false);
  }

  protected onImportanceChange(key: keyof DimensionWeights, level: ImportanceLevel): void {
    this.localImportance.update(imp => ({ ...imp, [key]: level }));
  }

  protected applyPreset(preset: { importance: DimensionImportance }): void {
    this.localImportance.set({ ...preset.importance });
  }

  protected applyDomainDefault(): void {
    this.localImportance.set(defaultImportanceForDomain(this._drawerDomain));
  }

  protected resetToDomainDefault(): void {
    this.localImportance.set(defaultImportanceForDomain(this._drawerDomain));
  }

  protected applyImportance(): void {
    this.store.setSubdomainImportance(this._drawerDomain, this._drawerSubname, this.localImportance());
    this.closeDimensionDrawer();
  }

  // ── Composite scoring ─────────────────────────────────────────────────────

  protected getComposite(item: PrioritizedItem): number {
    const active = this.store.activeSubdomain();
    if (!active) return 0;
    const w = this.store.getSubdomainWeights(active.domain, active.subdomain);
    const c = compositeScore(item, w);
    return isNaN(c) ? 0 : c;
  }

  protected getBadge(item: PrioritizedItem): 'Critical' | 'High' | 'Medium' | 'Low' {
    const c = this.getComposite(item);
    return c > 0 ? priorityBadge(c, item.urgency) : 'Medium';
  }

  // ── Style helpers ─────────────────────────────────────────────────────────

  protected subdomainRowClass(domain: string, subdomain: string): string {
    const active = this.store.activeSubdomain();
    const isActive = active?.domain === domain && active?.subdomain === subdomain;
    const base = 'flex flex-1 cursor-pointer items-center gap-2 rounded-lg px-2.5 py-1.5 transition-all min-w-0';
    return isActive
      ? `${base} bg-indigo-500/10 border border-indigo-500/25 text-indigo-300`
      : `${base} border border-transparent text-gray-400 hover:bg-gray-800/40 hover:text-gray-200`;
  }

  protected tabClass(key: string): string {
    const base = 'px-4 py-2 text-[11px] font-medium transition-colors border-b-2';
    return this.activeTab() === key
      ? `${base} border-indigo-500 text-indigo-400`
      : `${base} border-transparent text-gray-500 hover:text-gray-300`;
  }

  protected priorityBadgeClass(badge: string): string {
    const base = 'inline-flex items-center gap-1.5 rounded-md border px-2 py-0.5 text-[10px] font-semibold';
    switch (badge) {
      case 'Critical': return `${base} bg-rose-500/15 text-rose-400 border-rose-500/30`;
      case 'High':     return `${base} bg-amber-500/15 text-amber-400 border-amber-500/30`;
      case 'Medium':   return `${base} bg-sky-500/15 text-sky-400 border-sky-500/30`;
      default:         return `${base} bg-gray-500/15 text-gray-400 border-gray-500/30`;
    }
  }

  protected compositeBarClass(badge: string): string {
    switch (badge) {
      case 'Critical': return 'bg-gradient-to-r from-rose-600 to-rose-400';
      case 'High':     return 'bg-gradient-to-r from-amber-600 to-amber-400';
      case 'Medium':   return 'bg-gradient-to-r from-sky-600 to-sky-400';
      default:         return 'bg-gradient-to-r from-gray-600 to-gray-500';
    }
  }

  protected severityBadge(frequency: string): string {
    const base = 'inline-flex items-center rounded-md border px-2 py-0.5 text-[10px] font-semibold';
    switch (frequency) {
      case 'Widespread': return `${base} bg-red-500/15 text-red-400 border-red-500/25`;
      case 'Common':     return `${base} bg-amber-500/15 text-amber-400 border-amber-500/25`;
      default:           return `${base} bg-gray-500/15 text-gray-400 border-gray-500/25`;
    }
  }

  protected cardClass(): string {
    return 'group relative flex flex-col gap-3.5 cursor-pointer rounded-2xl border ' +
           'border-gray-800/60 bg-gray-900 p-5 outline-none transition-all duration-300 ' +
           'hover:border-indigo-500/30 hover:shadow-lg hover:shadow-indigo-950/50 ' +
           'focus-visible:ring-2 focus-visible:ring-indigo-500/50';
  }

  protected urgencyLabel(u: number): string { return u >= 8 ? 'Critical' : u >= 5 ? 'High' : 'Standard'; }
  protected valueTextClass(v: number): string { return v >= 8 ? 'text-emerald-400' : v >= 5 ? 'text-blue-400' : 'text-gray-500'; }
  protected valueBarClass(v: number): string {
    return v >= 8 ? 'bg-gradient-to-r from-emerald-600 to-teal-400'
         : v >= 5 ? 'bg-gradient-to-r from-blue-600 to-cyan-400'
         : 'bg-gradient-to-r from-gray-600 to-gray-500';
  }
  protected difficultySegClass(d: number, seg: number): string {
    return (d <= 3 ? 1 : d <= 7 ? 2 : 3) >= seg ? 'bg-slate-400' : 'bg-gray-800';
  }
  protected difficultyLabel(d: number): string { return d <= 3 ? 'Low' : d <= 7 ? 'Medium' : 'High'; }
  protected feasibilityLabel(s: number): string { return s >= 8 ? 'High' : s >= 5 ? 'Moderate' : 'Challenging'; }
  protected feasibilityLabelClass(s: number): string { return s >= 8 ? 'text-emerald-400' : s >= 5 ? 'text-amber-400' : 'text-red-400'; }
  protected feasibilityPanelClass(s: number): string {
    return s >= 8 ? 'border-emerald-500/20 bg-emerald-500/5'
         : s >= 5 ? 'border-amber-500/20 bg-amber-500/5'
         : 'border-red-500/20 bg-red-500/5';
  }
}
