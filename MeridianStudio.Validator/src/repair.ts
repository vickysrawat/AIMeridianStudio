/**
 * Deterministic validate→repair loop. Applies ALWAYS_ON, validates; on failure walks REPAIR_CATALOG
 * (flow rules filtered by diagram type), re-validating AFTER EACH rule (early exit on first parse
 * success), bounded to MAX_PASSES with a fixpoint stop. Never calls an LLM.
 */
import { ALWAYS_ON, REPAIR_CATALOG, applyFixes, diagramType } from './mermaid-fixes.js';
import { validateDiagram } from './validate.js';

const MAX_PASSES = 3;

export interface RepairResult {
  ok: boolean;
  repaired: string;
  rulesApplied: string[];
  error?: string;
  errorSignature?: string;
}

export async function repairDiagram(source: string): Promise<RepairResult> {
  const rulesApplied: string[] = [];

  // Always-on, semantics-preserving pre-pass, then validate.
  let src = applyFixes(source, ALWAYS_ON);
  let check = await validateDiagram(src);
  if (check.ok) return { ok: true, repaired: src, rulesApplied };

  const type = diagramType(src);
  const rules = REPAIR_CATALOG.filter(r => r.appliesTo === 'any' || type === 'flow');

  for (let pass = 0; pass < MAX_PASSES; pass++) {
    let changedThisPass = false;
    for (const rule of rules) {
      const next = rule.apply(src);
      if (next === src) continue;
      src = next;
      changedThisPass = true;
      rulesApplied.push(rule.name);
      check = await validateDiagram(src);
      if (check.ok) return { ok: true, repaired: src, rulesApplied };
    }
    if (!changedThisPass) break; // fixpoint — no rule altered the source this pass
  }

  return {
    ok: false,
    repaired: src,
    rulesApplied,
    error: check.error,
    errorSignature: check.errorSignature,
  };
}
