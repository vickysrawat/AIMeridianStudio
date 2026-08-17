# ADR-017 — Light/Dark Theming via `html.light` Class with Specificity-Layered Utility Overrides

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

MeridianStudio.UI was built dark-first: components hard-code dark-palette Tailwind utilities directly in markup (`bg-gray-950`, `text-gray-100`, `text-gray-500`, accent shades like `text-indigo-400`, etc.). A light theme was needed without rewriting every template.

The standard Tailwind approach would be the `dark:` variant (`class="bg-white dark:bg-gray-950"`), but that requires touching every utility in every template and was not how the existing markup was written. Other options were CSS custom properties / design tokens (a larger refactor) or a runtime style swap.

We needed a theme system that (a) layers on top of the existing dark-first markup with no per-component edits, (b) works with the out-of-band Tailwind build ([ADR-016](ADR-016-tailwind-standalone-cli-build.md)), and (c) is driven by an Angular-idiomatic, signal-based service.

## Decision

**The theme is toggled by adding/removing a single `light` class on `<html>`, and the light palette is implemented as higher-specificity CSS overrides of the compiled dark utilities.**

### Runtime toggle — `ThemeService`
- `theme = signal<'dark' | 'light'>(...)`, `providedIn: 'root'`.
- `toggle()` flips the signal and calls `applyTheme()`, which does `document.documentElement.classList.toggle('light', …)` and persists to `localStorage` under `meridian-theme`.
- On construction the service applies the saved theme; `loadTheme()` reads `localStorage`, falling back to the `prefers-color-scheme` media query, then to `dark`.
- Components read `themeService.theme()` directly (e.g. the workspace header sun/moon toggle).

### Palette — override rules in `src/styles.base.css`
All light-theme rules are scoped under `html.light` and placed **after** `@tailwind utilities`, so they win the cascade by both source order and specificity (`html.light .util` = two classes + one element beats the single-class generated `.util`) — no `!important` needed. The rule families are:

- **Backgrounds / borders:** invert the dark ramp — `html.light .bg-gray-950 → #f1f5f9`, `.bg-gray-900 → #fff`, etc.
- **Neutral text ramp:** mapped to a darkened, readable, monotonic hierarchy (`text-gray-400 → #334155`, `text-gray-500 → #475569`, `text-gray-600 → #64748b`, `text-gray-700 → #94a3b8`) so heavily-used muted grays meet contrast on light backgrounds.
- **Accent foreground text:** the light 300/400 accent weights are too pale on white, so they are darkened to 600/700 (`text-indigo-400 → #4f46e5`, `text-emerald-400 → #059669`, etc.). Accent *backgrounds*/*borders* (the `/10`–`/25` tints) are left unchanged.
- **White-on-accent restore:** the blanket `html.light .text-white → #0f172a` (used to flip white headings dark) would also darken button/badge labels sitting on indigo/violet/gradient fills, so compound and descendant selectors (`html.light .bg-indigo-600.text-white`, `html.light .bg-gradient-to-r .text-white`, …) restore those to `#ffffff`.
- **Inputs, scrollbars, markdown prose** get matching light variants.

A separate set of **plain (unscoped) `.text-gray-*` rules** shifts the most-used grays one step lighter for dark-mode legibility; the `html.light` versions override these in light mode by specificity.

## Consequences

### Positive
- Zero per-component edits to add the theme — existing dark-first markup keeps working; light mode is a pure CSS layer plus one class toggle.
- The signal-based `ThemeService` needs no subscriptions and applies the saved preference synchronously on startup, avoiding a flash of the wrong theme.
- Centralizing the palette in one section of `styles.base.css` makes contrast tuning a single-file change.
- Accent colors adapt automatically: because icons/labels use `text-indigo-400`, they render light indigo in dark mode and deep indigo in light mode with no conditional markup (relied on by the tab-icon coloring).

### Negative / Trade-offs
- **The override table must track the utilities actually used in markup.** Any new dark utility introduced in a template (a new `bg-gray-*`, `text-*`, accent shade) needs a matching `html.light` rule or it will look wrong in light mode. This is an ongoing maintenance coupling between templates and `styles.base.css`.
- Blanket overrides like `text-white → dark` require curated exception lists (the white-on-accent restore) — easy to miss a new accent background and get dark text on a colored button.
- This is not a token system: there is no semantic naming (`--color-surface`), so the meaning of each mapping lives in comments, not in structure.

### Failure Modes
- Edits here only take effect after the Tailwind CLI recompiles `styles.css` ([ADR-016](ADR-016-tailwind-standalone-cli-build.md)) — the original "theme button does nothing" report was exactly this.
- A newly-used accent background placed under `text-white` without adding it to the restore list produces unreadable dark-on-color labels in light mode.
- Relying on source-order-after-utilities means the rules must stay physically after `@tailwind utilities` in `styles.base.css`; reordering the file can silently weaken the overrides for any same-specificity cases.
