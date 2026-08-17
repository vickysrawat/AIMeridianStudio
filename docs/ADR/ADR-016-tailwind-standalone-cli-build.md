# ADR-016 — Tailwind Compiled Out-of-Band via Standalone CLI

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

MeridianStudio.UI styles its components with Tailwind utility classes. There are two common ways to wire Tailwind into an Angular build:

1. **Through the Angular/PostCSS build** — add `@tailwindcss/postcss` (v4) or `tailwindcss` (v3) to `postcss.config` so the Angular dev server and `ng build` compile utilities on every rebuild.
2. **Out-of-band** — run the Tailwind standalone CLI as a separate watcher that compiles a source file into a plain CSS file, which the Angular build then treats as an ordinary stylesheet.

The project also carries a naming/version mismatch that needs to be recorded: the root `CLAUDE.md` and `README.md` describe the stack as "Tailwind CSS v4 (PostCSS plugin, no config file)", but the installed dependency is `tailwindcss@^3.4.19` driven by a `tailwind.config.js`. The actual, working configuration is what this ADR documents.

## Decision

**Tailwind is compiled out-of-band by the standalone CLI, not by the Angular build.**

- `src/styles.base.css` is the **source**. It begins with the v3 directives `@tailwind base; @tailwind components; @tailwind utilities;` followed by all custom CSS (design tokens, markdown prose styles, scrollbars, and the theme overrides — see [ADR-017](ADR-017-html-light-theme-overrides.md)).
- `src/styles.css` is the **compiled output**, and it is the file Angular actually loads (`angular.json` → `"styles": ["src/styles.css"]`). It is a generated artifact that is committed to the repo.
- Compilation is driven by npm scripts:
  - `npm run tw:build` → `tailwindcss -i src/styles.base.css -o src/styles.css`
  - `npm run tw:watch` → same with `--watch`
- `tailwind.config.js` sets `content: ['./src/**/*.{html,ts}']` so the CLI tree-shakes utilities by scanning component templates. It also defines the `brand`/`surface` color and font-family extensions.
- `postcss.config.mjs` is intentionally minimal — it runs **autoprefixer only**. Tailwind is *not* a PostCSS plugin here.

The development workflow is therefore **two processes**: `npm start` (`ng serve`) for the app, and `npm run tw:watch` in a second terminal for CSS.

## Consequences

### Positive
- The compiled `styles.css` is a plain stylesheet, so the Angular build stays simple and fast — it never invokes Tailwind.
- Custom CSS written after `@tailwind utilities` reliably wins the cascade against generated utility classes by source order, which is what makes the theme-override strategy in [ADR-017](ADR-017-html-light-theme-overrides.md) work without `!important`.
- The CSS pipeline is decoupled from the Angular toolchain version, avoiding PostCSS-plugin compatibility churn.

### Negative / Trade-offs
- **Two processes must run during development.** Editing `styles.base.css` without `tw:watch` running (or without re-running `tw:build`) leaves `styles.css` stale — the app silently serves old CSS.
- The compiled `styles.css` is a committed generated file. Diffs are large and noisy, and it can drift from its source if someone edits `styles.css` directly instead of `styles.base.css`.
- The documented stack ("v4, no config file") disagrees with reality ("v3.4.19, `tailwind.config.js`"). New contributors following the docs will look for the wrong setup.

### Failure Modes
- **Stale-CSS bug (observed):** the dark/light theme toggle appeared "broken" because `styles.base.css` had new `html.light` rules but `styles.css` had not been recompiled. The button worked; the styles it depended on simply weren't in the served output. Always run `npm run tw:build` after editing `styles.base.css`.
- Editing `src/styles.css` by hand is silently overwritten on the next `tw:build`/`tw:watch` run.
- If `tailwind.config.js` `content` globs stop matching a new file location, classes used only there get tree-shaken away and silently produce unstyled markup.
