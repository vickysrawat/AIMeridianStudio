import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LucideAngularModule } from 'lucide-angular';
import { WorkspaceStoreService } from '../../core/services/workspace-store.service';
import { DomainCategory } from '../../core/models/interfaces';

@Component({
  selector: 'app-domain-settings',
  standalone: true,
  imports: [CommonModule, LucideAngularModule],
  template: `
    <div class="flex h-full flex-col">

      <!-- ── Header ─────────────────────────────────────────────── -->
      <div class="flex shrink-0 items-center justify-between border-b border-gray-800/60 px-5 py-4">
        <div class="flex items-center gap-2.5">
          <div class="flex h-7 w-7 items-center justify-center rounded-lg bg-indigo-500/15">
            <lucide-icon name="globe" [size]="14" class="text-indigo-400" />
          </div>
          <span class="text-sm font-semibold text-white">Domain Preferences</span>
        </div>
        <button (click)="store.closeDomainSettings()"
          class="flex h-7 w-7 items-center justify-center rounded-md text-gray-400
                 transition-colors hover:bg-gray-800 hover:text-gray-300">
          <lucide-icon name="x" [size]="14" />
        </button>
      </div>

      <!-- ── Discover button ────────────────────────────────────── -->
      <div class="shrink-0 border-b border-gray-800/40 px-5 py-4">
        <button (click)="onDiscover()"
          [disabled]="store.isDiscoveringDomains()"
          class="flex w-full items-center justify-center gap-2 rounded-lg
                 bg-gradient-to-r from-indigo-600 to-violet-600 px-4 py-2.5
                 text-xs font-medium text-white shadow-md shadow-indigo-600/20
                 transition-all hover:from-indigo-500 hover:to-violet-500
                 disabled:cursor-not-allowed disabled:opacity-50">
          @if (store.isDiscoveringDomains()) {
            <lucide-icon name="loader-2" [size]="13" class="animate-spin" />
            <span>Discovering domains…</span>
          } @else {
            <lucide-icon name="globe" [size]="13" />
            <span>Discover Domains</span>
          }
        </button>
        @if (store.discoveredDomains().length > 0) {
          <p class="mt-2 text-center text-[10px] text-gray-400">
            {{ store.discoveredDomains().length }} categories · expand to select sub-domains
          </p>
        } @else if (!store.isDiscoveringDomains()) {
          <p class="mt-2 text-center text-[10px] text-gray-400">
            Click to load an AI-curated industry hierarchy
          </p>
        }
      </div>

      <!-- ── Domain accordion list ──────────────────────────────── -->
      <div class="min-h-0 flex-1 overflow-y-auto">
        @if (store.discoveredDomains().length === 0) {
          <div class="flex flex-col items-center gap-4 px-5 py-16 text-center">
            <lucide-icon name="layers" [size]="36" class="text-gray-800" />
            <p class="text-xs leading-relaxed text-gray-400">
              Discover domains to see a curated hierarchy<br>
              of industry categories and sub-domains.
            </p>
          </div>
        } @else {
          <div class="flex flex-col p-3 gap-0.5">
            @for (category of store.discoveredDomains(); track $index) {

              <!-- Parent row -->
              <button [class]="parentRowClass(category)"
                      (click)="toggleAccordion(category.name)">
                <div class="flex items-center gap-2 min-w-0">
                  <lucide-icon name="layers" [size]="13" class="shrink-0 text-gray-400" />
                  <span class="truncate text-[12px] font-medium text-gray-300">
                    {{ category.name }}
                  </span>
                </div>
                <div class="flex shrink-0 items-center gap-2">
                  @if (checkedCount(category) > 0) {
                    <span class="rounded-full bg-indigo-500/20 px-1.5 py-0.5 text-[9px]
                                 font-bold text-indigo-400">
                      {{ checkedCount(category) }}
                    </span>
                  }
                  <lucide-icon name="chevron-right" [size]="12"
                    class="text-gray-600 transition-transform duration-200"
                    [class.rotate-90]="expandedDomain() === category.name" />
                </div>
              </button>

              <!-- Sub-domains (accordion body) -->
              @if (expandedDomain() === category.name) {
                <div class="mb-1 flex flex-col gap-0.5 rounded-b-lg border-x border-b
                            border-gray-800/60 bg-gray-900/30 px-2 pb-2 pt-1">
                  @for (sub of (category.subDomains); track $index) {
                    <label class="group flex cursor-pointer items-center gap-2.5 rounded-md
                                  px-2 py-1.5 transition-colors hover:bg-gray-800/40">
                      <div [class]="checkboxClass(sub)" (click)="toggle(sub)">
                        @if (isChecked(sub)) {
                          <lucide-icon name="check" [size]="9" class="text-white" />
                        }
                      </div>
                      <span class="flex-1 text-[11px] text-gray-400 transition-colors
                                   group-hover:text-gray-200"
                            (click)="toggle(sub)">
                        {{ sub }}
                      </span>
                    </label>
                  }
                </div>
              }

            }
          </div>
        }
      </div>

      <!-- ── Footer ─────────────────────────────────────────────── -->
      <div class="shrink-0 border-t border-gray-800/60 px-5 py-4">
        <div class="mb-3 flex items-center justify-between">
          <span class="text-xs text-gray-400">Selected sub-domains</span>
          <span class="rounded-full bg-indigo-500/20 px-2 py-0.5 text-xs font-bold text-indigo-400">
            {{ _checked().size }}
          </span>
        </div>
        <button (click)="onSave()"
          [disabled]="_checked().size === 0"
          class="flex w-full items-center justify-center gap-2 rounded-lg border
                 border-indigo-500/30 bg-indigo-500/10 px-4 py-2.5 text-xs font-medium
                 text-indigo-300 transition-all
                 hover:border-indigo-500/50 hover:bg-indigo-500/20
                 disabled:cursor-not-allowed disabled:opacity-40">
          <lucide-icon name="save" [size]="13" />
          <span>Save Preferences</span>
        </button>
      </div>

    </div>
  `,
})
export class DomainSettingsComponent {
  protected readonly store = inject(WorkspaceStoreService);

  protected readonly expandedDomain = signal<string | null>(null);

  protected readonly _checked = signal<Set<string>>(
    new Set(this.store.preferredDomains()),
  );

  protected toggleAccordion(name: string): void {
    this.expandedDomain.set(this.expandedDomain() === name ? null : name);
  }

  protected toggle(sub: string): void {
    this._checked.update(set => {
      const next = new Set(set);
      if (next.has(sub)) next.delete(sub);
      else next.add(sub);
      return next;
    });
  }

  protected isChecked(sub: string): boolean {
    return this._checked().has(sub);
  }

  protected checkedCount(category: DomainCategory): number {
    return (category.subDomains).filter(s => this._checked().has(s)).length;
  }

  protected parentRowClass(category: DomainCategory): string {
    const hasChecked = this.checkedCount(category) > 0;
    const isOpen = this.expandedDomain() === category.name;
    const base = 'flex w-full items-center justify-between gap-2 rounded-lg px-3 py-2.5 text-left transition-all duration-150';
    if (isOpen)
      return `${base} rounded-b-none border border-b-0 border-gray-700/60 bg-gray-800/60`;
    if (hasChecked)
      return `${base} border border-indigo-500/20 bg-indigo-500/5 hover:bg-indigo-500/10`;
    return `${base} border border-transparent hover:bg-gray-800/40`;
  }

  protected checkboxClass(sub: string): string {
    const base = 'flex h-3.5 w-3.5 shrink-0 items-center justify-center rounded border transition-all';
    return this.isChecked(sub)
      ? `${base} border-indigo-500 bg-indigo-600`
      : `${base} border-gray-700 bg-gray-900`;
  }

  protected onDiscover(): void {
    this.store.discoverDomains().subscribe();
  }

  protected onSave(): void {
    this.store.savePreferredDomains([...this._checked()]);

    // Also add each checked subdomain to the Research Areas left pane,
    // looking up the parent domain from the discovered hierarchy.
    const checked = this._checked();
    for (const category of this.store.discoveredDomains()) {
      for (const sub of (category.subDomains ?? [])) {
        if (checked.has(sub)) {
          this.store.addSubdomain(category.name, sub);
        }
      }
    }

    // Activate the first newly added subdomain if nothing is active
    if (!this.store.activeSubdomain() && this.store.selectedSubdomains().length > 0) {
      const first = this.store.selectedSubdomains()[0];
      this.store.setActiveSubdomain(first.domain, first.subdomain);
    }

    this.store.closeDomainSettings();
  }
}
