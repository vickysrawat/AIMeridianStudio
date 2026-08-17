import { Component, computed, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LucideAngularModule } from 'lucide-angular';
import { WorkspaceStoreService } from '../../core/services/workspace-store.service';
import { MarkdownPipe } from '../../core/pipes/markdown.pipe';
import { MermaidDirective } from '../../core/directives/mermaid.directive';
import { ArchDecision, BuyVsBuildOption, ImprovementSuggestion } from '../../core/models/interfaces';
import { BlueprintChatDrawerComponent } from './blueprint-chat-drawer.component';

@Component({
  selector: 'app-architectural-blueprinter',
  standalone: true,
  imports: [CommonModule, LucideAngularModule, MarkdownPipe, MermaidDirective, BlueprintChatDrawerComponent],
  template: `
    <div class="flex min-h-full flex-col">

      <!-- ── Sticky Action Header ────────────────────────────────── -->
      <div class="sticky top-0 z-20 shrink-0 border-b border-gray-800/60
                  bg-gray-950/90 px-6 py-4 backdrop-blur-sm">
        <div class="flex items-center justify-between gap-4">
          <div class="flex flex-col gap-0.5">
            <div class="flex items-center gap-2.5">
              <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-violet-500/15">
                <lucide-icon name="sparkles" [size]="15" class="text-violet-400" />
              </div>
              <h2 class="text-lg font-semibold tracking-tight text-white">
                Architectural Blueprinter
              </h2>
            </div>
            @if (store.selectedSolution(); as sol) {
              <p class="ml-[42px] text-xs text-gray-400">
                Scoped to: <span class="font-medium text-indigo-400">{{ sol.name }}</span>
              </p>
            } @else {
              <p class="ml-[42px] text-xs text-gray-500">
                Select a solution in the Research tab to enable generation.
              </p>
            }
          </div>

          <button
            (click)="onGenerate()"
            [disabled]="!store.selectedSolution() || store.isGeneratingBlueprint()"
            class="flex h-11 min-w-[220px] items-center justify-center gap-2 rounded-xl
                   bg-gradient-to-r from-violet-600 to-indigo-600 px-5 text-sm font-medium
                   text-white shadow-lg shadow-violet-500/20 transition-all duration-200
                   hover:from-violet-500 hover:to-indigo-500
                   focus:outline-none focus:ring-2 focus:ring-violet-500/40
                   disabled:cursor-not-allowed disabled:opacity-40 disabled:shadow-none"
          >
            @if (store.isGeneratingBlueprint()) {
              <lucide-icon name="loader-2" [size]="15" class="animate-spin" />
              <span>Compiling Blueprint...</span>
            } @else {
              <lucide-icon name="sparkles" [size]="15" />
              <span>Compile Custom Design Blueprint</span>
            }
          </button>

          <button
            (click)="onCheckReadiness()"
            [disabled]="!store.selectedSolution() || store.isAnalyzingBlueprint()"
            title="Get clarifying questions to sharpen the blueprint before generating"
            class="flex h-11 items-center justify-center gap-2 rounded-xl border border-gray-700/60
                   bg-gray-900/60 px-4 text-sm font-medium text-gray-300 transition-all
                   hover:border-indigo-500/40 hover:text-indigo-200
                   disabled:cursor-not-allowed disabled:opacity-40"
          >
            @if (store.isAnalyzingBlueprint()) {
              <lucide-icon name="loader-2" [size]="14" class="animate-spin" />
              <span>Checking…</span>
            } @else {
              <lucide-icon name="list-checks" [size]="14" />
              <span>Check readiness</span>
            }
          </button>
        </div>

        <!-- Blueprint meta row -->
        @if (store.compiledBlueprint(); as bp) {
          <div class="mt-3 flex flex-wrap items-center gap-3 rounded-lg border border-gray-800/60
                      bg-gray-900/60 px-4 py-2 text-[11px] text-gray-400">
            <span class="font-medium text-gray-200">{{ bp.solutionName }}</span>
            <span class="h-3 w-px bg-gray-700"></span>
            <span>Domain: {{ bp.domain }}</span>
            <span class="h-3 w-px bg-gray-700"></span>
            <span [class]="bp.modelUsed?.includes('Heuristic') ? 'text-gray-500' : 'text-emerald-400'">via {{ bp.modelUsed }}</span>

            <!-- Solution type badge + override picker -->
            @if (store.effectiveSolutionType()) {
              <span class="h-3 w-px bg-gray-700"></span>
              <div class="relative">
                <button
                  (click)="toggleTypePicker()"
                  class="flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[10px]
                         font-medium transition-colors
                         {{ store.solutionTypeOverride()
                            ? 'border-amber-500/40 bg-amber-500/10 text-amber-300'
                            : 'border-indigo-500/30 bg-indigo-500/10 text-indigo-300' }}"
                  title="Click to override solution type">
                  <lucide-icon name="cpu" [size]="10" />
                  {{ store.effectiveSolutionType() }}
                  @if (bp.solutionTypeConfidence && !store.solutionTypeOverride()) {
                    <span class="text-gray-500">{{ (bp.solutionTypeConfidence * 100).toFixed(0) }}%</span>
                  }
                  @if (store.solutionTypeOverride()) {
                    <span class="text-amber-400">✎</span>
                  }
                  <lucide-icon name="chevron-down" [size]="9" class="text-gray-500" />
                </button>

                <!-- Picker dropdown -->
                @if (showTypePicker()) {
                  <div class="absolute left-0 top-full z-30 mt-1.5 w-52 overflow-hidden rounded-xl
                              border border-gray-700 bg-gray-900 shadow-xl shadow-black/40">
                    <div class="border-b border-gray-800 px-3 py-2 text-[10px] font-semibold
                                uppercase tracking-wider text-gray-500">
                      Override solution type
                    </div>
                    @for (type of solutionTypes; track type) {
                      <button
                        (click)="selectType(type)"
                        class="flex w-full items-center justify-between px-3 py-2 text-left text-xs
                               transition-colors hover:bg-gray-800
                               {{ store.effectiveSolutionType() === type
                                  ? 'text-indigo-300 bg-gray-800/60'
                                  : 'text-gray-300' }}">
                        <span>{{ type }}</span>
                        @if (store.effectiveSolutionType() === type) {
                          <lucide-icon name="check" [size]="11" class="text-indigo-400" />
                        }
                      </button>
                    }
                    @if (store.solutionTypeOverride()) {
                      <div class="border-t border-gray-800 px-3 py-2">
                        <button
                          (click)="selectType(null)"
                          class="text-[10px] text-amber-400 hover:text-amber-300 transition-colors">
                          ↺ Reset to detected ({{ bp.solutionType }})
                        </button>
                      </div>
                    }
                  </div>
                }
              </div>
            }

            <!-- Chat to refine the solution profile / type from the description row -->
            <button (click)="openChat('solution-profile', 'Solution Profile', {solutionType: bp.solutionType, domain: bp.domain})"
              class="ml-auto flex items-center gap-1 rounded-lg border border-violet-500/40 bg-violet-500/15
                     px-2.5 py-1 text-[10px] font-medium text-violet-300 transition-colors
                     hover:border-violet-500/60 hover:bg-violet-500/25 hover:text-white">
              <lucide-icon name="message-circle" [size]="11" />Chat
            </button>
          </div>
        }
      </div>

      <!-- ── Main Content ────────────────────────────────────────── -->
      <div class="relative flex min-h-0 flex-1 flex-col overflow-y-auto p-6">

        <!-- Blueprint readiness critic (advisory): clarifying questions to sharpen inputs — shown
             whether or not a blueprint exists yet (pre-flight, or gaps to refine + regenerate). -->
        @if (store.blueprintReadiness(); as r) {
            <div class="mb-5 rounded-xl border border-indigo-500/20 bg-indigo-500/5 p-4">
              <div class="mb-2 flex items-center gap-2">
                <lucide-icon name="list-checks" [size]="14" class="text-indigo-300" />
                <span class="text-xs font-semibold text-indigo-200">Blueprint readiness</span>
                <span class="rounded-full px-2 py-0.5 text-[10px] font-medium"
                      [class]="r.readinessScore >= 70 ? 'bg-emerald-500/15 text-emerald-300'
                             : r.readinessScore >= 40 ? 'bg-amber-500/15 text-amber-300'
                             : 'bg-red-500/15 text-red-300'">{{ r.readinessScore }}%</span>
                <button (click)="store.blueprintReadiness.set(null)"
                        class="ml-auto text-[11px] text-gray-500 hover:text-gray-300">dismiss</button>
              </div>
              <p class="mb-3 text-[11px] leading-relaxed text-gray-400">{{ r.verdict }}</p>
              @if (r.clarifyingQuestions.length) {
                <p class="mb-1 text-[10px] font-semibold uppercase tracking-wider text-gray-500">Answer these to sharpen the blueprint</p>
                <ul class="mb-3 space-y-1">
                  @for (q of r.clarifyingQuestions; track $index) {
                    <li class="flex gap-2 text-[11px] text-gray-300"><span class="text-indigo-400">?</span><span>{{ q }}</span></li>
                  }
                </ul>
              }
              @for (s of r.suggestions; track $index; let i = $index) {
                <div class="mb-1.5 rounded-lg border border-gray-800/60 bg-gray-900/50 px-3 py-2">
                  <div class="text-[11px] text-gray-300"><span class="font-medium text-gray-200">{{ s.field }}:</span> {{ s.suggestion }}</div>
                  @if (s.proposedText) {
                    <div class="mt-1 whitespace-pre-wrap text-[10px] italic text-gray-500">{{ s.proposedText }}</div>
                    <button (click)="applyReadinessSuggestion(s, i)" [disabled]="appliedReadiness().has(i)"
                      class="mt-1.5 flex items-center gap-1 rounded-lg border border-teal-500/30 bg-teal-500/10
                             px-2.5 py-1 text-[10px] font-medium text-teal-300 transition-colors
                             hover:bg-teal-500/20 disabled:opacity-40">
                      <lucide-icon [name]="appliedReadiness().has(i) ? 'check' : 'plus'" [size]="10" />
                      {{ appliedReadiness().has(i) ? 'Applied to context' : 'Apply to context' }}
                    </button>
                  }
                </div>
              }
            </div>
        }

        <!-- No solution selected — centered overlay -->
        @if (!store.compiledBlueprint() && !store.isGeneratingBlueprint() && !store.selectedSolution()) {
          <div class="absolute inset-0 flex flex-col items-center justify-center gap-5">
            <div class="flex flex-col items-center gap-4 text-center">
              <div class="flex h-16 w-16 items-center justify-center rounded-2xl
                          border border-gray-800 bg-gray-900">
                <lucide-icon name="alert-circle" [size]="28" class="text-gray-700" />
              </div>
              <div class="flex flex-col gap-1.5">
                <p class="text-sm font-medium text-gray-300">No solution selected</p>
                <p class="max-w-sm text-xs leading-relaxed text-gray-400">
                  Go to the <span class="text-indigo-400">Research</span> tab, discover solution
                  priorities, then click a card to select one before generating a blueprint.
                </p>
              </div>
              <button (click)="store.setActiveWorkspace('research')"
                class="flex h-9 items-center gap-1.5 rounded-lg border border-indigo-500/30
                       bg-indigo-500/10 px-4 text-xs font-medium text-indigo-400
                       transition-colors hover:bg-indigo-500/20">
                <lucide-icon name="chevron-right" [size]="12" />
                Go to Research
              </button>
            </div>
          </div>
        }

        <!-- Solution selected, not yet compiled — pre-generation context (normal flow, sits below the
             readiness panel so its Apply buttons can drop scaffolds straight into this textarea). -->
        @if (store.selectedSolution() && !store.compiledBlueprint() && !store.isGeneratingBlueprint()) {
          <div class="overflow-hidden rounded-2xl border border-teal-500/35 bg-gray-900">
            <div class="flex items-center justify-between border-b border-teal-500/15 bg-teal-500/5 px-5 py-3.5">
              <div class="flex items-center gap-2">
                <lucide-icon name="clipboard" [size]="14" class="text-teal-400" />
                <span class="text-xs font-semibold uppercase tracking-wider text-teal-400">Context &amp; constraints</span>
                <span class="rounded-full border border-teal-500/25 bg-teal-500/10 px-2 py-0.5 text-[9px] font-medium text-teal-500">
                  You write this
                </span>
              </div>
              <span class="text-[10px] text-gray-600">Run <span class="text-indigo-400">Check readiness</span> for gap suggestions, then Apply them here</span>
            </div>
            <div class="p-5">
              <textarea
                [value]="store.preGenNotes()"
                (input)="store.preGenNotes.set($any($event.target).value)"
                rows="5"
                placeholder="Add anything that should shape the blueprint — existing stack, compliance, team skills, timeline, integration constraints…

Or click Apply on a readiness suggestion above to drop a ready-made scaffold here, then fill in the [e.g. …] placeholders."
                class="w-full resize-y rounded-xl border border-gray-700/50 bg-gray-800/50
                       px-4 py-3 text-xs leading-relaxed text-gray-300 placeholder-gray-700
                       focus:border-teal-500/40 focus:outline-none focus:bg-gray-800/80 transition-colors"></textarea>
              <p class="mt-3 flex items-center gap-1.5 text-[11px] text-gray-500">
                <lucide-icon name="sparkles" [size]="12" class="text-violet-500" />
                Click <span class="font-medium text-violet-400">Compile Custom Design Blueprint</span> above when ready —
                this context flows into the design and every downstream document.
              </p>
            </div>
          </div>
        }

        <!-- ── Bento Grid — visible during streaming (loading states) and after completion ── -->
        @let bp = store.compiledBlueprint();
        @let streaming = store.isGeneratingBlueprint();

        @if (bp || streaming) {
          <div class="grid grid-cols-1 gap-4 lg:grid-cols-3">

            <!-- 01. Core Scenario -->
            <div class="overflow-hidden rounded-2xl border border-indigo-500/30 bg-gray-900
                        transition-all duration-300 hover:border-indigo-500/50 lg:col-span-3">
              <div class="flex items-center justify-between border-b border-indigo-500/10
                          bg-indigo-500/5 px-5 py-3.5">
                <div class="flex items-center gap-2">
                  @if (streaming) {
                    <lucide-icon name="loader-2" [size]="14" class="animate-spin text-indigo-400" />
                  } @else {
                    <lucide-icon name="target" [size]="14" class="text-indigo-400" />
                  }
                  <span class="text-xs font-semibold uppercase tracking-wider text-indigo-400">
                    01 — Core Scenario
                  </span>
                </div>
                @if (bp && !streaming) {
                  <button (click)="openChat('core-scenario', 'Core Scenario', bp.coreScenario)"
                    class="flex items-center gap-1 rounded-lg border border-violet-500/40 bg-violet-500/15
                           px-2.5 py-1 text-[10px] font-medium text-violet-300 transition-colors
                           hover:border-violet-500/60 hover:bg-violet-500/25 hover:text-white">
                    <lucide-icon name="message-circle" [size]="11" />Chat
                  </button>
                }
              </div>
              @if (streaming && streamPreview().length > 0) {
                <div class="md-content p-5 opacity-80" [innerHTML]="streamPreview() | markdown" appMermaid></div>
                <div class="px-5 pb-4">
                  <span class="inline-block h-3.5 w-0.5 bg-indigo-400 animate-pulse align-middle"></span>
                </div>
              } @else if (streaming) {
                <div class="p-5 flex flex-col gap-2.5">
                  <div class="h-3 w-3/4 rounded-full bg-gray-800 animate-pulse"></div>
                  <div class="h-3 w-full rounded-full bg-gray-800 animate-pulse"></div>
                  <div class="h-3 w-2/3 rounded-full bg-gray-800 animate-pulse"></div>
                  <div class="h-3 w-5/6 rounded-full bg-gray-800 animate-pulse"></div>
                  <div class="h-3 w-1/2 rounded-full bg-gray-800 animate-pulse"></div>
                </div>
              } @else if (bp) {
                <div class="md-content p-5"
                     [innerHTML]="summarize(bp.coreScenario) | markdown" appMermaid></div>
              }
            </div>

            <!-- 02. Solution Profile -->
            <div class="overflow-hidden rounded-2xl border border-blue-500/30 bg-gray-900
                        transition-all duration-300 hover:border-blue-500/50 lg:col-span-3">
              <div class="flex items-center justify-between border-b border-blue-500/10
                          bg-blue-500/5 px-5 py-3.5">
                <div class="flex items-center gap-2">
                  <lucide-icon name="cpu" [size]="14" class="text-blue-400" />
                  <span class="text-xs font-semibold uppercase tracking-wider text-blue-400">
                    02 — Solution Profile
                  </span>
                </div>
                @if (bp && !streaming) {
                  <button (click)="openChat('solution-profile', 'Solution Profile', {solutionType: bp.solutionType, domain: bp.domain})"
                    class="flex items-center gap-1 rounded-lg border border-violet-500/40 bg-violet-500/15
                           px-2.5 py-1 text-[10px] font-medium text-violet-300 transition-colors
                           hover:border-violet-500/60 hover:bg-violet-500/25 hover:text-white">
                    <lucide-icon name="message-circle" [size]="11" />Chat
                  </button>
                }
              </div>
              @if (streaming) {
                <div class="flex flex-wrap gap-x-8 gap-y-4 p-5">
                  @for (_ of [1,2,3,4]; track $index) {
                    <div class="flex flex-col gap-2">
                      <div class="h-2 w-16 rounded-full bg-gray-800 animate-pulse"></div>
                      <div class="h-4 w-28 rounded bg-gray-800/60 animate-pulse"></div>
                    </div>
                  }
                </div>
              } @else if (bp) {
                <div class="flex flex-wrap gap-x-8 gap-y-4 p-5">
                  <div class="flex flex-col gap-1">
                    <span class="text-[10px] font-semibold uppercase tracking-wider text-gray-500">Solution Type</span>
                    <span class="text-sm font-medium text-blue-300">
                      {{ store.effectiveSolutionType() || '—' }}
                      @if (bp.solutionTypeConfidence) {
                        <span class="ml-1.5 text-xs text-gray-600">
                          {{ (bp.solutionTypeConfidence * 100).toFixed(0) }}% confidence
                        </span>
                      }
                    </span>
                  </div>
                  <div class="flex flex-col gap-1">
                    <span class="text-[10px] font-semibold uppercase tracking-wider text-gray-500">Domain</span>
                    <span class="text-sm font-medium text-gray-200">{{ bp.domain }}</span>
                  </div>
                  <div class="flex flex-col gap-1">
                    <span class="text-[10px] font-semibold uppercase tracking-wider text-gray-500">Generated By</span>
                    <span class="text-sm font-medium"
                          [class]="bp.modelUsed?.includes('Heuristic') ? 'text-gray-400' : 'text-emerald-400'">
                      {{ bp.modelUsed || '—' }}
                    </span>
                  </div>
                  @if (bp.archDecisions.length) {
                    <div class="flex flex-col gap-1">
                      <span class="text-[10px] font-semibold uppercase tracking-wider text-gray-500">Arch Decisions</span>
                      <span class="text-sm font-medium text-violet-300">{{ bp.archDecisions.length }}</span>
                    </div>
                  }
                  @if (bp.qualityAttributes.length) {
                    <div class="flex flex-col gap-1">
                      <span class="text-[10px] font-semibold uppercase tracking-wider text-gray-500">Quality Targets</span>
                      <span class="text-sm font-medium text-emerald-300">{{ bp.qualityAttributes.length }}</span>
                    </div>
                  }
                  @if (bp.techRadar.length) {
                    <div class="flex flex-col gap-1">
                      <span class="text-[10px] font-semibold uppercase tracking-wider text-gray-500">Tech Layers</span>
                      <span class="text-sm font-medium text-cyan-300">{{ bp.techRadar.length }}</span>
                    </div>
                  }
                </div>
              }
            </div>

            <!-- 03. Project Context — user-authored client-specific information -->
            <div class="overflow-hidden rounded-2xl border border-teal-500/35 bg-gray-900
                        transition-all duration-300 hover:border-teal-500/55 lg:col-span-3">
              <div class="flex items-center justify-between border-b border-teal-500/15
                          bg-teal-500/5 px-5 py-3.5">
                <div class="flex items-center gap-2">
                  <lucide-icon name="clipboard" [size]="14" class="text-teal-400" />
                  <span class="text-xs font-semibold uppercase tracking-wider text-teal-400">
                    03 — Project Context
                  </span>
                  <span class="rounded-full border border-teal-500/25 bg-teal-500/10
                               px-2 py-0.5 text-[9px] font-medium text-teal-500">
                    You write this
                  </span>
                  @if (hasNoteEdits() && !streaming) {
                    <span class="inline-flex items-center gap-1 rounded-full border border-amber-500/30
                                 bg-amber-500/10 px-2 py-0.5 text-[10px] font-medium text-amber-400">
                      <lucide-icon name="pencil" [size]="9" />
                      Unsaved
                    </span>
                  }
                </div>
                <div class="flex items-center gap-3">
                  @if (hasNoteEdits() && !streaming) {
                    <button (click)="localNotes.set(store.compiledBlueprint()?.projectNotes ?? '')"
                      class="text-[10px] text-gray-600 transition-colors hover:text-gray-400">
                      Discard
                    </button>
                    <button (click)="saveNotes()" [disabled]="isSavingNotes()"
                      class="flex items-center gap-1.5 rounded-lg border border-teal-500/30
                             bg-teal-500/10 px-3 py-1 text-[10px] font-medium text-teal-400
                             transition-colors hover:bg-teal-500/20 disabled:opacity-40">
                      @if (isSavingNotes()) {
                        <lucide-icon name="loader-2" [size]="10" class="animate-spin" />
                      } @else {
                        <lucide-icon name="save" [size]="10" />
                      }
                      Save
                    </button>
                  } @else if (!streaming) {
                    <div class="flex items-center gap-3">
                      <span class="text-[10px] text-gray-600">Feeds into all documents and AI chat</span>
                      @if (bp) {
                        <button (click)="openChat('project-context', 'Project Context', bp.projectNotes)"
                          class="flex items-center gap-1 rounded-lg border border-teal-500/20
                                 px-2.5 py-1 text-[10px] font-medium text-violet-300 transition-colors
                                 hover:border-violet-500/60 hover:bg-violet-500/25 hover:text-white">
                          <lucide-icon name="message-circle" [size]="11" />Chat
                        </button>
                      }
                    </div>
                  }
                </div>
              </div>
              @if (streaming) {
                <div class="p-5 flex flex-col gap-2">
                  <div class="h-3 w-2/3 rounded-full bg-gray-800 animate-pulse"></div>
                  <div class="h-3 w-1/2 rounded-full bg-gray-800/70 animate-pulse"></div>
                </div>
              } @else {
                <div class="p-5">
                  <textarea
                    [value]="localNotes()"
                    (input)="localNotes.set($any($event.target).value)"
                    rows="4"
                    placeholder="Add project-specific context here — anything the AI should know about this client or project.

Examples:
• Existing stack: client uses AWS, PostgreSQL 14, Angular 18
• Compliance: SOC 2 Type II already certified; GDPR applies (EU customers)
• Constraints: no new vendors without procurement approval; team of 6 backend devs
• Timeline: MVP in Q3 2026; full launch Q1 2027"
                    class="w-full resize-y rounded-xl border border-gray-700/50 bg-gray-800/50
                           px-4 py-3 text-xs leading-relaxed text-gray-300 placeholder-gray-700
                           focus:border-teal-500/40 focus:outline-none focus:bg-gray-800/80
                           transition-colors"></textarea>
                </div>
              }
            </div>

            <!-- 04. Architecture Decisions -->
            <div class="overflow-hidden rounded-2xl border border-violet-500/30 bg-gray-900
                        transition-all duration-300 hover:border-violet-500/50 lg:col-span-3">
              <div class="flex items-center justify-between border-b border-violet-500/10
                          bg-violet-500/5 px-5 py-3.5">
                <div class="flex items-center gap-2">
                  <lucide-icon name="git-branch" [size]="14" class="text-violet-400" />
                  <span class="text-xs font-semibold uppercase tracking-wider text-violet-400">
                    03 — Architecture Decisions
                  </span>
                  @if (hasDecisionEdits() && !streaming) {
                    <span class="inline-flex items-center gap-1 rounded-full border border-amber-500/30
                                 bg-amber-500/10 px-2 py-0.5 text-[10px] font-medium text-amber-400">
                      <lucide-icon name="pencil" [size]="9" />
                      Edited
                    </span>
                  }
                </div>
                <div class="flex items-center gap-3">
                  @if (hasDecisionEdits() && !streaming) {
                    <button (click)="resetDecisions()"
                      class="text-[10px] text-gray-600 transition-colors hover:text-gray-400">
                      Reset to AI
                    </button>
                    <button (click)="saveDecisions()"
                      [disabled]="isSaving()"
                      class="flex items-center gap-1.5 rounded-lg border border-violet-500/30
                             bg-violet-500/10 px-3 py-1 text-[10px] font-medium text-violet-400
                             transition-colors hover:bg-violet-500/20 disabled:opacity-40">
                      @if (isSaving()) {
                        <lucide-icon name="loader-2" [size]="10" class="animate-spin" />
                      } @else {
                        <lucide-icon name="save" [size]="10" />
                      }
                      Save to Blueprint
                    </button>
                  } @else if (!streaming) {
                    <div class="flex items-center gap-3">
                      <span class="text-[10px] text-gray-500">The WHY behind this blueprint · click
                        <lucide-icon name="pencil" [size]="10" class="inline text-gray-600" /> to edit a row</span>
                      @if (bp) {
                        <button (click)="openChat('arch-decisions', 'Architecture Decisions', localDecisions())"
                          class="flex items-center gap-1 rounded-lg border border-violet-500/20
                                 px-2.5 py-1 text-[10px] font-medium text-violet-300 transition-colors
                                 hover:border-violet-500/40 hover:text-violet-400">
                          <lucide-icon name="message-circle" [size]="11" />Chat
                        </button>
                      }
                    </div>
                  }
                </div>
              </div>
              @if (streaming) {
                <div class="p-5 space-y-3">
                  @for (_ of [1,2,3,4,5]; track $index) {
                    <div class="flex gap-3">
                      <div class="h-3 w-[13%] rounded-full bg-gray-800 animate-pulse"></div>
                      <div class="h-3 w-[30%] rounded-full bg-gray-800/70 animate-pulse"></div>
                      <div class="h-3 w-[32%] rounded-full bg-gray-800/50 animate-pulse"></div>
                      <div class="h-3 flex-1 rounded-full bg-gray-800/30 animate-pulse"></div>
                    </div>
                  }
                </div>
              } @else {
                <div class="overflow-x-auto p-5">
                  @if (localDecisions().length > 0) {
                    <table class="w-full text-xs">
                      <thead>
                        <tr class="border-b border-gray-800">
                          <th class="pb-2.5 pr-4 text-left font-semibold text-gray-400 w-[13%]">Decision</th>
                          <th class="pb-2.5 pr-4 text-left font-semibold text-gray-400 w-[30%]">
                            Chosen Approach
                            <span class="block text-[9px] font-normal text-gray-600 normal-case tracking-normal">name — why chosen</span>
                          </th>
                          <th class="pb-2.5 pr-4 text-left font-semibold text-gray-400 w-[30%]">
                            Alternatives Rejected
                            <span class="block text-[9px] font-normal text-gray-600 normal-case tracking-normal">name — why rejected</span>
                          </th>
                          <th class="pb-2.5 pr-4 text-left font-semibold text-gray-400 w-[22%]">
                            Mitigations Needed
                            <span class="block text-[9px] font-normal text-gray-600 normal-case tracking-normal">to make the chosen approach work</span>
                          </th>
                          <th class="pb-2.5 w-7"></th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (d of localDecisions(); track $index; let i = $index) {
                          @if (editingRow() === i) {
                            <!-- Edit mode row -->
                            <tr class="border-b border-gray-800/40 last:border-0 bg-violet-500/5">
                              <td class="py-2 pr-4 font-medium text-violet-300 align-top text-[10px]">{{ d.decision }}</td>
                              <td class="py-2 pr-4 align-top">
                                <input
                                  [value]="editDraftApproach()"
                                  (input)="editDraftApproach.set($any($event.target).value)"
                                  placeholder="Technology / pattern name"
                                  class="mb-1.5 w-full rounded border border-violet-500/30 bg-gray-800
                                         px-2 py-0.5 text-[10px] text-violet-200 placeholder-gray-700
                                         focus:border-violet-500/60 focus:outline-none" />
                                <textarea
                                  [value]="editDraftRationale()"
                                  (input)="editDraftRationale.set($any($event.target).value)"
                                  rows="2"
                                  placeholder="Why this approach was chosen…"
                                  class="w-full resize-none rounded border border-gray-700 bg-gray-800
                                         px-2 py-0.5 text-[10px] text-gray-400 placeholder-gray-700
                                         focus:border-violet-500/30 focus:outline-none"></textarea>
                              </td>
                              <td class="py-2 pr-4 align-top">
                                <textarea
                                  [value]="editDraftAlts()"
                                  (input)="editDraftAlts.set($any($event.target).value)"
                                  rows="3"
                                  placeholder="One per line:&#10;Name — why rejected"
                                  class="w-full resize-none rounded border border-gray-700 bg-gray-800
                                         px-2 py-0.5 text-[10px] text-gray-400 placeholder-gray-700
                                         focus:border-violet-500/30 focus:outline-none"></textarea>
                                <p class="mt-0.5 text-[9px] text-gray-700">One alternative per line</p>
                              </td>
                              <td class="py-2 pr-4 align-top">
                                <textarea
                                  [value]="editDraftMits()"
                                  (input)="editDraftMits.set($any($event.target).value)"
                                  rows="3"
                                  placeholder="One mitigation per line"
                                  class="w-full resize-none rounded border border-gray-700 bg-gray-800
                                         px-2 py-0.5 text-[10px] text-gray-400 placeholder-gray-700
                                         focus:border-amber-500/20 focus:outline-none"></textarea>
                              </td>
                              <td class="py-3 align-top">
                                <div class="flex flex-col gap-2">
                                  <button (click)="applyRow(i)" title="Apply"
                                    class="text-emerald-400 transition-colors hover:text-emerald-300">
                                    <lucide-icon name="check" [size]="15" />
                                  </button>
                                  <button (click)="cancelRow()" title="Cancel"
                                    class="text-gray-600 transition-colors hover:text-gray-400">
                                    <lucide-icon name="x" [size]="15" />
                                  </button>
                                </div>
                              </td>
                            </tr>
                          } @else {
                            <!-- View mode row -->
                            <tr class="group border-b border-gray-800/40 last:border-0">
                              <td class="py-3 pr-4 font-medium text-violet-300 align-top">{{ d.decision }}</td>
                              <td class="py-3 pr-4 align-top">
                                <span class="font-semibold text-violet-200">{{ d.chosenApproach }}</span>
                                @if (d.rationale) {
                                  <span class="text-gray-500"> — {{ d.rationale }}</span>
                                }
                              </td>
                              <td class="py-3 pr-4 align-top">
                                @for (alt of d.alternativesConsidered; track $index) {
                                  @let parts = alt.split(' — ');
                                  <div class="mb-2 last:mb-0">
                                    <span class="font-medium text-gray-300">{{ parts[0] }}</span>
                                    @if (parts.length > 1) {
                                      <span class="text-gray-600"> — {{ parts.slice(1).join(' — ') }}</span>
                                    }
                                  </div>
                                }
                              </td>
                              <td class="py-3 pr-4 align-top">
                                @for (m of d.risks; track $index) {
                                  <span class="mb-1.5 block leading-relaxed text-amber-400/70 last:mb-0">{{ m }}</span>
                                }
                              </td>
                              <td class="py-3 align-top">
                                <button (click)="editRow(i)" title="Edit this decision"
                                  class="text-gray-700 opacity-0 transition-all hover:text-violet-400
                                         group-hover:opacity-100">
                                  <lucide-icon name="pencil" [size]="13" />
                                </button>
                              </td>
                            </tr>
                          }
                        }
                      </tbody>
                    </table>
                  } @else if (bp) {
                    <p class="text-xs italic text-gray-600">No architecture decisions recorded.</p>
                  }
                </div>
              }
            </div>

            <!-- 04. Quality Attribute Scorecard — 2 cols -->
            <div class="overflow-hidden rounded-2xl border border-emerald-500/30 bg-gray-900
                        transition-all duration-300 hover:border-emerald-500/50 lg:col-span-2">
              <div class="flex items-center justify-between border-b border-emerald-500/10
                          bg-emerald-500/5 px-5 py-3.5">
                <div class="flex items-center gap-2">
                  <lucide-icon name="bar-chart-2" [size]="14" class="text-emerald-400" />
                  <span class="text-xs font-semibold uppercase tracking-wider text-emerald-400">
                    04 — Quality Attribute Scorecard
                  </span>
                </div>
                @if (bp && !streaming) {
                  <button (click)="openChat('qa-scorecard', 'Quality Attribute Scorecard', bp.qualityAttributes)"
                    class="flex items-center gap-1 rounded-lg border border-emerald-500/20
                           px-2.5 py-1 text-[10px] font-medium text-violet-300 transition-colors
                           hover:border-emerald-500/40 hover:text-emerald-400">
                    <lucide-icon name="message-circle" [size]="11" />Chat
                  </button>
                }
              </div>
              @if (streaming) {
                <div class="p-5 space-y-3">
                  @for (_ of [1,2,3,4,5,6]; track $index) {
                    <div class="flex gap-4">
                      <div class="h-3 w-[25%] rounded-full bg-gray-800 animate-pulse"></div>
                      <div class="h-3 w-[20%] rounded-full bg-gray-800/70 animate-pulse"></div>
                      <div class="h-3 flex-1 rounded-full bg-gray-800/50 animate-pulse"></div>
                    </div>
                  }
                </div>
              } @else {
                <div class="overflow-x-auto p-5">
                  @if (bp && bp.qualityAttributes && bp.qualityAttributes.length > 0) {
                    <table class="w-full text-xs">
                      <thead>
                        <tr class="border-b border-gray-800">
                          <th class="pb-2.5 pr-4 text-left font-semibold text-gray-400 w-[25%]">Attribute</th>
                          <th class="pb-2.5 pr-4 text-left font-semibold text-gray-400 w-[20%]">Target</th>
                          <th class="pb-2.5 text-left font-semibold text-gray-400">Measurement</th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (q of bp.qualityAttributes; track $index) {
                          <tr class="border-b border-gray-800/40 last:border-0">
                            <td class="py-2.5 pr-4 font-medium text-emerald-300 align-top">{{ q.attribute }}</td>
                            <td class="py-2.5 pr-4 text-gray-200 align-top">{{ q.target }}</td>
                            <td class="py-2.5 text-gray-400 align-top">{{ q.measurement }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  } @else if (bp) {
                    <p class="text-xs text-gray-600 italic">No quality targets recorded.</p>
                  }
                </div>
              }
            </div>

            <!-- 05. Technology Radar — 1 col -->
            <div class="overflow-hidden rounded-2xl border border-cyan-500/30 bg-gray-900
                        transition-all duration-300 hover:border-cyan-500/50 lg:col-span-1">
              <div class="flex items-center justify-between border-b border-cyan-500/10
                          bg-cyan-500/5 px-5 py-3.5">
                <div class="flex items-center gap-2">
                  <lucide-icon name="radar" [size]="14" class="text-cyan-400" />
                  <span class="text-xs font-semibold uppercase tracking-wider text-cyan-400">
                    05 — Technology Radar
                  </span>
                </div>
                @if (bp && !streaming) {
                  <button (click)="openChat('tech-radar', 'Technology Radar', bp.techRadar)"
                    class="flex items-center gap-1 rounded-lg border border-cyan-500/20
                           px-2.5 py-1 text-[10px] font-medium text-violet-300 transition-colors
                           hover:border-cyan-500/40 hover:text-cyan-400">
                    <lucide-icon name="message-circle" [size]="11" />Chat
                  </button>
                }
              </div>
              @if (streaming) {
                <div class="p-5 flex flex-col gap-4">
                  @for (_ of [1,2,3,4,5]; track $index) {
                    <div class="flex flex-col gap-2">
                      <div class="h-2 w-16 rounded-full bg-gray-800 animate-pulse"></div>
                      <div class="flex gap-1.5">
                        <div class="h-6 w-20 rounded-full bg-gray-800/60 animate-pulse"></div>
                        <div class="h-6 w-16 rounded-full bg-gray-800/40 animate-pulse"></div>
                      </div>
                    </div>
                  }
                </div>
              } @else {
                <div class="p-5 flex flex-col gap-4">
                  @if (bp && bp.techRadar && bp.techRadar.length > 0) {
                    @for (entry of bp.techRadar; track $index) {
                      <div class="flex flex-col gap-1.5">
                        <span class="text-[10px] font-semibold uppercase tracking-wider"
                              [class]="layerColor(entry.layer)">
                          {{ entry.layer }}
                        </span>
                        <div class="flex flex-wrap gap-1.5">
                          @for (tech of entry.technologies; track $index) {
                            <span class="rounded-full border border-gray-700/60 bg-gray-800/60
                                         px-2.5 py-1 text-[11px] text-gray-300">
                              {{ tech }}
                            </span>
                          }
                        </div>
                      </div>
                    }
                  } @else if (bp) {
                    <p class="text-xs text-gray-600 italic">No tech stack recorded.</p>
                  }
                </div>
              }
            </div>

            <!-- Feasibility Analysis — full width (use-case-driven blueprints only) -->
            @if (bp && bp.feasibility) {
              <div class="overflow-hidden rounded-2xl border border-indigo-500/20 bg-gray-900/60
                          transition-all duration-300 hover:border-indigo-500/40 lg:col-span-3">
                <div class="flex items-center justify-between border-b border-indigo-500/10
                            bg-indigo-500/5 px-5 py-3.5">
                  <div class="flex items-center gap-2">
                    <lucide-icon name="git-compare" [size]="14" class="text-indigo-400" />
                    <span class="text-xs font-semibold uppercase tracking-wider text-indigo-400">
                      Feasibility Analysis
                    </span>
                  </div>
                  @if (!streaming) {
                    <button (click)="openChat('feasibility', 'Feasibility Analysis', bp.feasibility)"
                      class="flex items-center gap-1 rounded-lg border border-indigo-500/20
                             px-2.5 py-1 text-[10px] font-medium text-violet-300 transition-colors
                             hover:border-indigo-500/40 hover:text-indigo-400">
                      <lucide-icon name="message-circle" [size]="11" />Chat
                    </button>
                  }
                </div>
                <div class="p-5">
                  <p class="text-xs leading-relaxed text-gray-300">{{ bp.feasibility.summary }}</p>
                  @if (bp.feasibility.primaryConcernVerdict) {
                    <p class="mt-2 flex items-start gap-1.5 text-xs leading-relaxed text-gray-400">
                      <lucide-icon name="target" [size]="12" class="mt-0.5 shrink-0 text-amber-400" />
                      <span>{{ bp.feasibility.primaryConcernVerdict }}</span>
                    </p>
                  }
                  <div class="mt-4 overflow-x-auto">
                    <table class="w-full text-xs">
                      <thead>
                        <tr class="border-b border-gray-800">
                          <th class="pb-2.5 pr-4 text-left font-semibold text-gray-400 w-[26%]">Option</th>
                          <th class="pb-2.5 pr-4 text-left font-semibold text-gray-400 w-[16%]">Verdict</th>
                          <th class="pb-2.5 pr-4 text-left font-semibold text-gray-400 w-[8%]">Score</th>
                          <th class="pb-2.5 pr-4 text-left font-semibold text-gray-400 w-[18%]">Effort</th>
                          <th class="pb-2.5 text-left font-semibold text-gray-400">Recommendation</th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (opt of bp.feasibility.options; track $index) {
                          <tr class="border-b border-gray-800/40 last:border-0">
                            <td class="py-3 pr-4 font-medium text-indigo-300 align-top">{{ opt.name }}</td>
                            <td class="py-3 pr-4 align-top">
                              <span class="inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-semibold"
                                    [class]="feasibilityVerdictClass(opt.verdict)">{{ opt.verdict }}</span>
                            </td>
                            <td class="py-3 pr-4 align-top text-gray-300">{{ opt.score }}/10</td>
                            <td class="py-3 pr-4 align-top text-gray-400">{{ opt.effortEstimate }}</td>
                            <td class="py-3 text-gray-500 align-top leading-relaxed">{{ opt.recommendation }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                </div>
              </div>
            }

            <!-- 07. Buy vs Build — full width -->
            <div class="overflow-hidden rounded-2xl border border-rose-500/30 bg-gray-900
                        transition-all duration-300 hover:border-rose-500/50 lg:col-span-3">
              <div class="flex items-center justify-between border-b border-rose-500/10
                          bg-rose-500/5 px-5 py-3.5">
                <div class="flex items-center gap-2">
                  <lucide-icon name="scale" [size]="14" class="text-rose-400" />
                  <span class="text-xs font-semibold uppercase tracking-wider text-rose-400">
                    07 — Buy vs Build
                  </span>
                </div>
                @if (bp && !streaming) {
                  <button (click)="openChat('buy-vs-build', 'Buy vs Build', bp.buyVsBuild)"
                    class="flex items-center gap-1 rounded-lg border border-rose-500/20
                           px-2.5 py-1 text-[10px] font-medium text-violet-300 transition-colors
                           hover:border-rose-500/40 hover:text-rose-400">
                    <lucide-icon name="message-circle" [size]="11" />Chat
                  </button>
                }
              </div>
              @if (streaming) {
                <div class="p-5 space-y-3">
                  @for (_ of [1,2,3,4,5]; track $index) {
                    <div class="flex gap-3">
                      <div class="h-3 w-[14%] rounded-full bg-gray-800 animate-pulse"></div>
                      <div class="h-3 w-[20%] rounded-full bg-gray-800/70 animate-pulse"></div>
                      <div class="h-3 w-[18%] rounded-full bg-gray-800/50 animate-pulse"></div>
                      <div class="h-3 w-[18%] rounded-full bg-gray-800/40 animate-pulse"></div>
                      <div class="h-3 w-[14%] rounded-full bg-gray-800/30 animate-pulse"></div>
                      <div class="h-3 flex-1 rounded-full bg-gray-800/20 animate-pulse"></div>
                    </div>
                  }
                </div>
              } @else {
                <div class="overflow-x-auto p-5">
                  @if (bp && bp.buyVsBuild && bp.buyVsBuild.length > 0) {
                    <table class="w-full text-xs">
                      <thead>
                        <tr class="border-b border-gray-800">
                          <th class="pb-2.5 pr-4 text-left font-semibold text-gray-400 w-[12%]">Component</th>
                          <th class="pb-2.5 pr-4 text-left font-semibold text-gray-400 w-[16%]">
                            Buy
                            <span class="block text-[9px] font-normal text-gray-600 normal-case tracking-normal">options + why</span>
                          </th>
                          <th class="pb-2.5 pr-4 text-left font-semibold text-gray-400 w-[20%]">
                            Build
                            <span class="block text-[9px] font-normal text-gray-600 normal-case tracking-normal">approach + why</span>
                          </th>
                          <th class="pb-2.5 pr-4 text-left font-semibold text-gray-400 w-[10%]">Decision</th>
                          <th class="pb-2.5 text-left font-semibold text-gray-400">Why</th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (opt of bp.buyVsBuild; track $index) {
                          <tr class="border-b border-gray-800/40 last:border-0">
                            <td class="py-3 pr-4 font-medium text-rose-300 align-top">{{ opt.component }}</td>
                            <td class="py-3 pr-4 align-top">
                              <span class="font-medium text-gray-200">{{ opt.buyOption }}</span>
                              @if (opt.buyRationale) {
                                <span class="text-gray-600"> — {{ opt.buyRationale }}</span>
                              }
                            </td>
                            <td class="py-3 pr-4 align-top">
                              <span class="font-medium text-gray-200">{{ opt.buildApproach }}</span>
                              @if (opt.buildRationale) {
                                <span class="text-gray-600"> — {{ opt.buildRationale }}</span>
                              }
                            </td>
                            <td class="py-3 pr-4 align-top">
                              <span class="inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-semibold"
                                    [class]="opt.recommendation === 'Buy'
                                      ? 'bg-emerald-500/15 text-emerald-400 border border-emerald-500/25'
                                      : opt.recommendation === 'Build'
                                        ? 'bg-blue-500/15 text-blue-400 border border-blue-500/25'
                                        : 'bg-amber-500/15 text-amber-400 border border-amber-500/25'">
                                {{ opt.recommendation }}
                              </span>
                            </td>
                            <td class="py-3 text-gray-500 align-top leading-relaxed">{{ opt.recommendationReason }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  } @else if (bp) {
                    <p class="text-xs italic text-gray-600">No buy vs build options recorded.</p>
                  }
                </div>
              }
            </div>

            <!-- 06. Implementation Detail — accordion (hidden while streaming) -->
            <div class="overflow-hidden rounded-2xl border border-amber-500/30 bg-gray-900
                        transition-all duration-300 hover:border-amber-500/50 lg:col-span-3">
              <button
                (click)="toggleDetail()"
                [disabled]="streaming"
                class="flex w-full items-center justify-between border-b border-amber-500/10
                       bg-amber-500/5 px-5 py-3.5 text-left transition-colors hover:bg-amber-500/10
                       disabled:cursor-not-allowed disabled:opacity-50">
                <div class="flex items-center gap-2">
                  <lucide-icon name="code-2" [size]="14" class="text-amber-400" />
                  <span class="text-xs font-semibold uppercase tracking-wider text-amber-400">
                    06 — Implementation Detail
                  </span>
                  <span class="text-[10px] text-gray-500">Topology · Schemas · Endpoints</span>
                </div>
                <div class="flex items-center gap-2">
                  @if (bp && !streaming) {
                    <button (click)="openChat('implementation', 'Implementation Detail', {baseTopology: bp.baseTopology, databaseSchemes: bp.databaseSchemes, endpointManifest: bp.endpointManifest})"
                      class="flex items-center gap-1 rounded-lg border border-amber-500/20
                             px-2.5 py-1 text-[10px] font-medium text-violet-300 transition-colors
                             hover:border-amber-500/40 hover:text-amber-400">
                      <lucide-icon name="message-circle" [size]="11" />Chat
                    </button>
                  }
                  @if (streaming) {
                    <lucide-icon name="loader-2" [size]="12" class="animate-spin text-amber-400/50" />
                    <span class="text-[10px] text-gray-600">Generating…</span>
                  } @else {
                    <span class="text-[10px] text-gray-500">{{ showDetail() ? 'Collapse' : 'Expand' }}</span>
                    <lucide-icon
                      [name]="showDetail() ? 'chevron-up' : 'chevron-down'"
                      [size]="13" class="text-amber-400 transition-transform duration-200" />
                  }
                </div>
              </button>

              @if (!streaming && showDetail() && bp) {
                <div class="divide-y divide-gray-800/60">
                  <div class="p-5">
                    <div class="mb-3 flex items-center justify-between">
                      <div class="flex items-center gap-2">
                        <lucide-icon name="layers" [size]="12" class="text-blue-400" />
                        <span class="text-[11px] font-semibold uppercase tracking-wider text-blue-400">System Topology</span>
                      </div>
                      <button
                        (click)="store.regenerateTopology(bp.id)"
                        [disabled]="store.isRegeneratingTopology()"
                        class="flex items-center gap-1.5 rounded-lg border border-blue-500/20
                               px-2.5 py-1 text-[10px] font-medium text-violet-300 transition-colors
                               hover:border-blue-500/40 hover:text-blue-400
                               disabled:cursor-not-allowed disabled:opacity-40">
                        @if (store.isRegeneratingTopology()) {
                          <lucide-icon name="loader-2" [size]="10" class="animate-spin" />
                          Regenerating…
                        } @else {
                          <lucide-icon name="refresh-cw" [size]="10" />
                          Regenerate
                        }
                      </button>
                    </div>
                    @if (store.isRegeneratingTopology() && store.streamedTopology().length > 0) {
                      <div class="font-mono text-[10px] leading-relaxed text-gray-500 whitespace-pre-wrap
                                  max-h-64 overflow-auto">{{ store.streamedTopology() }}<span
                          class="inline-block h-3 w-0.5 bg-blue-400 animate-pulse align-middle ml-0.5"></span></div>
                    } @else if (store.isRegeneratingTopology()) {
                      <div class="h-40 rounded-lg bg-gray-800/50 animate-pulse"></div>
                    } @else {
                      <div class="md-content" [innerHTML]="bp.baseTopology | markdown" appMermaid></div>
                    }
                  </div>
                  <div class="p-5">
                    <div class="mb-3 flex items-center gap-2">
                      <lucide-icon name="database" [size]="12" class="text-emerald-400" />
                      <span class="text-[11px] font-semibold uppercase tracking-wider text-emerald-400">Database Schemes</span>
                    </div>
                    <div class="md-content" [innerHTML]="bp.databaseSchemes | markdown" appMermaid></div>
                  </div>
                  <div class="p-5">
                    <div class="mb-3 flex items-center gap-2">
                      <lucide-icon name="zap" [size]="12" class="text-violet-400" />
                      <span class="text-[11px] font-semibold uppercase tracking-wider text-violet-400">API Endpoint Manifest</span>
                    </div>
                    <div class="md-content" [innerHTML]="bp.endpointManifest | markdown" appMermaid></div>
                  </div>
                </div>
              }
            </div>

          </div>
        }

        <!-- Blueprint Section Chat Drawer -->
        @if (chatOpen() && bp) {
          <app-blueprint-chat-drawer
            [blueprintId]="bp.id"
            [sectionKey]="chatSectionKey()"
            [sectionLabel]="chatSectionLabel()"
            [sectionData]="chatSectionData()"
            (applyPatch)="handleApplyPatch($event)"
            (closed)="chatOpen.set(false)" />
        }

      </div>
    </div>
  `,
})
export class ArchitecturalBlueprintersComponent {
  protected readonly store = inject(WorkspaceStoreService);

  protected readonly showTypePicker = signal(false);
  protected readonly showDetail     = signal(false);

  // ── Project Notes ─────────────────────────────────────────────────────────
  protected readonly localNotes   = signal('');
  protected readonly isSavingNotes= signal(false);
  protected readonly hasNoteEdits = computed(() => {
    const saved = this.store.compiledBlueprint()?.projectNotes ?? '';
    return this.localNotes() !== saved;
  });

  // ── Section chatbot ───────────────────────────────────────────────────────
  protected readonly chatOpen        = signal(false);
  protected readonly chatSectionKey  = signal('');
  protected readonly chatSectionLabel= signal('');
  protected readonly chatSectionData = signal<unknown>(null);

  // ── Architecture Decisions inline editing ─────────────────────────────────
  protected readonly localDecisions    = signal<ArchDecision[]>([]);
  protected readonly editingRow        = signal<number | null>(null);
  protected readonly editDraftApproach = signal('');
  protected readonly editDraftRationale= signal('');
  protected readonly editDraftAlts     = signal('');
  protected readonly editDraftMits     = signal('');
  protected readonly isSaving          = signal(false);

  protected readonly hasDecisionEdits = computed(() => {
    const orig  = this.store.compiledBlueprint()?.archDecisions ?? [];
    const local = this.localDecisions();
    return JSON.stringify(orig) !== JSON.stringify(local);
  });

  private _syncedBlueprintId = '';

  constructor() {
    // Sync local decisions + notes whenever a new blueprint is loaded
    effect(() => {
      const bp = this.store.compiledBlueprint();
      if (bp && bp.id !== this._syncedBlueprintId) {
        this._syncedBlueprintId = bp.id;
        this.localDecisions.set([...(bp.archDecisions ?? [])]);
        this.localNotes.set(bp.projectNotes ?? '');
        this.editingRow.set(null);
      }
    });
  }

  /** Extracts the coreScenario prose value from the accumulating JSON token stream. */
  protected readonly streamPreview = computed(() => {
    const raw = this.store.blueprintStreamText();
    if (!raw) return '';

    const key = '"coreScenario"';
    const keyIdx = raw.indexOf(key);
    if (keyIdx === -1) return '';

    const colonIdx = raw.indexOf(':', keyIdx + key.length);
    if (colonIdx === -1) return '';

    const openQuote = raw.indexOf('"', colonIdx + 1);
    if (openQuote === -1) return '';

    let result = '';
    let i = openQuote + 1;
    while (i < raw.length) {
      const c = raw[i];
      if (c === '\\' && i + 1 < raw.length) {
        const next = raw[i + 1];
        if (next === 'n')  { result += '\n'; i += 2; continue; }
        if (next === 't')  { result += '\t'; i += 2; continue; }
        if (next === '"')  { result += '"';  i += 2; continue; }
        if (next === '\\') { result += '\\'; i += 2; continue; }
        result += next; i += 2; continue;
      }
      if (c === '"') break;
      result += c;
      i++;
    }
    return result.trim();
  });

  // Keep in sync with SolutionClassifierService.KnownTypes (the API's canonical vocabulary).
  protected readonly solutionTypes = [
    'REST API',
    'GraphQL API',
    'Web App',
    'Static Site',
    'Mobile App',
    'Desktop App',
    'Microservices',
    'Monolith',
    'Azure Serverless',
    'Console App',
    'Batch Processing',
    'Data Pipeline',
    'Streaming / Real-Time',
    'Event-Driven',
    'ML Inference',
    'RAG / Knowledge Retrieval',
    'Agentic AI',
  ];

  protected onGenerate(): void {
    const sol = this.store.selectedSolution();
    if (sol) this.store.generateBlueprintStream(sol);
  }

  protected onCheckReadiness(): void {
    const sol = this.store.selectedSolution();
    if (!sol) return;
    this.appliedReadiness.set(new Set());
    this.store.analyzeBlueprintReadiness(sol).subscribe({ error: () => {} });
  }

  /** Tracks which readiness suggestions have been applied into the pre-gen context (by index). */
  protected readonly appliedReadiness = signal<Set<number>>(new Set());

  /** One-click apply: append a readiness suggestion's scaffold to the pre-generation context textarea. */
  protected applyReadinessSuggestion(s: ImprovementSuggestion, index: number): void {
    const text = s.proposedText?.trim();
    if (!text) return;
    const current = this.store.preGenNotes();
    this.store.preGenNotes.set(current.trim() ? `${current}\n\n${text}` : text);
    this.appliedReadiness.update(set => new Set(set).add(index));
  }

  protected editRow(index: number): void {
    const d = this.localDecisions()[index];
    this.editingRow.set(index);
    this.editDraftApproach.set(d.chosenApproach);
    this.editDraftRationale.set(d.rationale);
    this.editDraftAlts.set(d.alternativesConsidered.join('\n'));
    this.editDraftMits.set(d.risks.join('\n'));
  }

  protected applyRow(index: number): void {
    this.localDecisions.update(ds => ds.map((d, i) => i !== index ? d : {
      ...d,
      chosenApproach:         this.editDraftApproach().trim(),
      rationale:              this.editDraftRationale().trim(),
      alternativesConsidered: this.editDraftAlts().split('\n').map(s => s.trim()).filter(Boolean),
      risks:                  this.editDraftMits().split('\n').map(s => s.trim()).filter(Boolean),
    }));
    this.editingRow.set(null);
  }

  protected cancelRow(): void {
    this.editingRow.set(null);
  }

  protected saveDecisions(): void {
    const bp = this.store.compiledBlueprint();
    if (!bp) return;
    this.isSaving.set(true);
    this.store.patchBlueprint(bp.id, { archDecisions: this.localDecisions() })
      .subscribe({ complete: () => this.isSaving.set(false), error: () => this.isSaving.set(false) });
  }

  protected resetDecisions(): void {
    const bp = this.store.compiledBlueprint();
    if (!bp) return;
    this.localDecisions.set([...(bp.archDecisions ?? [])]);
    this.editingRow.set(null);
  }

  protected saveNotes(): void {
    const bp = this.store.compiledBlueprint();
    if (!bp) return;
    this.isSavingNotes.set(true);
    this.store.patchBlueprint(bp.id, { projectNotes: this.localNotes() })
      .subscribe({ complete: () => this.isSavingNotes.set(false), error: () => this.isSavingNotes.set(false) });
  }

  protected feasibilityVerdictClass(verdict: string): string {
    const v = (verdict || '').toLowerCase();
    if (v.includes('not'))     return 'bg-red-500/15 text-red-400 border border-red-500/25';
    if (v.includes('partial')) return 'bg-amber-500/15 text-amber-400 border border-amber-500/25';
    if (v.includes('effort'))  return 'bg-blue-500/15 text-blue-400 border border-blue-500/25';
    return 'bg-emerald-500/15 text-emerald-400 border border-emerald-500/25';
  }

  protected openChat(key: string, label: string, data: unknown): void {
    if (this.chatSectionKey() !== key) {
      this.chatSectionKey.set(key);
      this.chatSectionLabel.set(label);
      this.chatSectionData.set(data);
    }
    this.chatOpen.set(true);
  }

  protected handleApplyPatch(event: { sectionKey: string; patch: Record<string, unknown> }): void {
    const bp = this.store.compiledBlueprint();
    if (!bp) return;

    // The AI sometimes returns the array directly instead of wrapping it
    // (e.g. [{layer:"Backend",...}] instead of {techRadar:[...]}).
    // Normalise to a proper patch object before sending.
    const sectionKeyMap: Record<string, string> = {
      'tech-radar':     'techRadar',
      'arch-decisions': 'archDecisions',
      'qa-scorecard':   'qualityAttributes',
      'buy-vs-build':   'buyVsBuild',
    };
    const raw = event.patch as unknown;
    const patch: Record<string, unknown> = Array.isArray(raw)
      ? { [sectionKeyMap[event.sectionKey] ?? event.sectionKey]: raw }
      : (event.patch as Record<string, unknown>);

    if (event.sectionKey === 'arch-decisions' && Array.isArray(patch['archDecisions'])) {
      // Pre-fill local decisions for user review before saving
      this.localDecisions.set(patch['archDecisions'] as ArchDecision[]);
      this.chatOpen.set(false);
    } else if (event.sectionKey === 'project-context' && typeof patch['projectNotes'] === 'string') {
      this.localNotes.set(patch['projectNotes'] as string);
      this.chatOpen.set(false);
    } else if (event.sectionKey === 'buy-vs-build') {
      this.store.patchBlueprint(bp.id, patch as any).subscribe();
      this.chatOpen.set(false);
    } else {
      this.store.patchBlueprint(bp.id, patch as any).subscribe();
      this.chatOpen.set(false);
    }
  }

  protected toggleTypePicker(): void {
    this.showTypePicker.update(v => !v);
  }

  protected selectType(type: string | null): void {
    this.showTypePicker.set(false);
    const bp = this.store.compiledBlueprint();
    if (type && bp) {
      // Durably persist the override: the backend stamps confidence 1.0 and returns the updated blueprint,
      // so bp.solutionType becomes the single source of truth (survives refresh). Clear the transient signal.
      this.store.setSolutionTypeOverride(null);
      this.store.patchBlueprint(bp.id, { solutionType: type }).subscribe({ error: () => {} });
    } else {
      // Reset to detected: clear the client-side override → falls back to the last persisted classifier value.
      this.store.setSolutionTypeOverride(null);
    }
  }

  protected toggleDetail(): void {
    this.showDetail.update(v => !v);
  }

  protected summarize(text: string, maxChars = 700): string {
    if (!text || text.length <= maxChars) return text;
    const trimmed = text.slice(0, maxChars);
    const lastNewline = trimmed.lastIndexOf('\n');
    const lastSpace = trimmed.lastIndexOf(' ');
    const cutAt = Math.max(lastNewline, lastSpace);
    return (cutAt > 100 ? trimmed.slice(0, cutAt) : trimmed) + '…';
  }

  protected layerColor(layer: string): string {
    switch (layer.toLowerCase()) {
      case 'frontend': return 'text-sky-400';
      case 'backend':  return 'text-violet-400';
      case 'data':     return 'text-emerald-400';
      case 'infra':    return 'text-blue-400';
      case 'ai':       return 'text-amber-400';
      case 'devops':   return 'text-orange-400';
      case 'mobile':   return 'text-pink-400';
      default:         return 'text-gray-400';
    }
  }
}
