# ADR-005 — Angular Signals + Standalone Components, No NgModules

**Status:** Accepted
**Date:** June 2026
**Deciders:** MeridianStudio team

## Context

MeridianStudio's UI is a single-workspace application where many components need to react to shared state (selected solution, compiled blueprint, generated document, model status). The classic Angular approach — services with `BehaviorSubject` + `async` pipe — works but requires understanding RxJS operators, subscription lifecycle, and the `takeUntilDestroyed` pattern for every component.

Angular 19 introduced stable Signals as an alternative reactive primitive. The question was whether to use Signals throughout, use a hybrid approach, or stick with RxJS-only.

Separately, Angular 14+ introduced standalone components that do not require `NgModule` declarations. The question was whether to adopt standalone fully or keep `NgModule` for feature areas.

## Decision

**All components are standalone** — no `NgModule` declarations exist anywhere in the project. Components declare their own `imports` array directly.

**Angular Signals are the primary reactive primitive** for UI state:
- All state in `WorkspaceStoreService` is held in `signal<T>()` primitives
- Derived values use `computed()` — they update automatically when dependencies change
- Side effects that span signals (e.g., SSE event → iteration counter) use `effect()` in component constructors
- Observable API calls still use RxJS (`HttpClient` returns `Observable`) but are converted to signal updates via `tap()` → `signal.set()`

`WorkspaceStoreService` is the single source of truth — components inject it and read signals directly, without piping through the async pipe.

Lazy-loaded routes use `loadComponent()` pointing to standalone component classes.

## Consequences

### Positive
- No subscription management: signals never need `unsubscribe()`, `takeUntilDestroyed`, or `async` pipe — they are garbage-collected automatically
- Template syntax is simpler: `store.isLoading()` instead of `isLoading$ | async`
- `computed()` replaces boilerplate `combineLatest` / `switchMap` for derived state
- Standalone components make the import chain explicit — no hidden transitive module imports
- Eliminates a class of "component declared in wrong module" bugs entirely

### Negative / Trade-offs
- `effect()` has subtle ordering semantics — it runs after the current change detection cycle, which can cause one-frame delays if used for DOM updates
- Signals cannot replace all RxJS patterns — complex async sequences (debounce, switchMap, race) still require RxJS; the hybrid approach means developers need to understand both
- `WorkspaceStoreService` grows large as features are added — it currently holds 20+ signals; consider splitting into domain-specific stores if it exceeds 600 lines
- Standalone components require manually listing every dependency in `imports` — forgetting a pipe or module produces a runtime error rather than a compile-time error in some setups

### Failure Modes
- An `effect()` that reads a signal and calls `signal.set()` on a related signal can create an infinite loop if not guarded — Angular will throw `ExpressionChangedAfterChecked` or `effect_cannot_set_signal_in_computed` errors
- The `of(null)` escape hatch in `getMissionSuggestions` (to handle errors without breaking the observable chain) silently swallows API errors — callers must check for `null` return
