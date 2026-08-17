import { Injectable, signal } from '@angular/core';

export type Theme = 'dark' | 'light';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly theme = signal<Theme>(this.loadTheme());

  constructor() {
    // Apply on startup so the saved preference takes effect immediately
    this.applyTheme(this.theme());
  }

  toggle(): void {
    const next: Theme = this.theme() === 'dark' ? 'light' : 'dark';
    this.theme.set(next);
    this.applyTheme(next);
  }

  private applyTheme(t: Theme): void {
    document.documentElement.classList.toggle('light', t === 'light');
    try { localStorage.setItem('meridian-theme', t); } catch { /* SSR/private mode */ }
  }

  private loadTheme(): Theme {
    try {
      const stored = localStorage.getItem('meridian-theme') as Theme | null;
      if (stored === 'light' || stored === 'dark') return stored;
      return window.matchMedia?.('(prefers-color-scheme: light)').matches
        ? 'light'
        : 'dark';
    } catch {
      return 'dark';
    }
  }
}
