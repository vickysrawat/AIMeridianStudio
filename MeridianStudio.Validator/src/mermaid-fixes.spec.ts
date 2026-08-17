import { describe, it, expect } from 'vitest';
import { REPAIR_CATALOG, ALWAYS_ON, applyFixes } from './mermaid-fixes.js';

const rule = (name: string) => {
  const r = REPAIR_CATALOG.find(x => x.name === name);
  if (!r) throw new Error(`rule ${name} not found`);
  return r;
};

describe('bare-multiword-nodes', () => {
  const fix = rule('bare-multiword-nodes');

  it('rewrites bare multi-word node ids to id["Label"] consistently', () => {
    const out = fix.apply('graph TD\n  User --> Azure Front Door\n  Azure Front Door --> B[OK]');
    expect(out).toContain('AzureFrontDoor["Azure Front Door"]');
    expect(out).not.toMatch(/-->\s*Azure Front Door\s*$/m); // no bare reference left
    // single-token node untouched
    expect(out).toContain('User');
  });

  it('preserves slashes inside edge labels (no "or" normalization)', () => {
    const out = fix.apply('graph TD\n  A -->|Web/API| B');
    expect(out).toContain('Web/API');
    expect(out).not.toContain('Web or API');
  });

  it('leaves a valid diagram structurally intact', () => {
    const src = 'graph TD\n  A[X] --> B[Y]';
    const out = fix.apply(src);
    expect(out).toContain('A[X]');
    expect(out).toContain('B[Y]');
  });
});

describe('trailing-edge-label (merge, do not drop)', () => {
  const fix = rule('trailing-edge-label');

  it('merges a pipe-labelled edge with its trailing annotation', () => {
    const out = fix.apply('graph TD\n  A -->|Federated Identity| B : SAML/OAuth');
    expect(out).toContain('|Federated Identity — SAML/OAuth|');
  });

  it('normalizes an inline-labelled edge to pipe form and merges', () => {
    const out = fix.apply('graph TD\n  A -- Federated Identity --> B : SAML/OAuth');
    expect(out).toContain('-->|Federated Identity — SAML/OAuth|');
  });

  it('moves a bare edge trailing annotation into the label', () => {
    const out = fix.apply('graph TD\n  User --> AZLZ : Access');
    expect(out).toContain('|Access|');
  });

  it('leaves edges without a trailing colon unchanged', () => {
    const src = 'graph TD\n  A --> B';
    expect(fix.apply(src)).toBe(src);
  });
});

describe('subgraph-title-ids', () => {
  it('gives a spaced subgraph title a referenceable id', () => {
    const out = rule('subgraph-title-ids').apply('graph TD\n  subgraph Azure - Primary\n    A\n  end');
    expect(out).toContain('["Azure - Primary"]');
    expect(out).toMatch(/subgraph\s+sg\d+\[/);
  });

  it('quotes an explicit-id subgraph label containing parentheses', () => {
    const out = rule('subgraph-title-ids').apply(
      'graph TD\n  subgraph CloudProvider[Cloud Provider (e.g., Azure)]\n    A\n  end',
    );
    expect(out).toContain('subgraph CloudProvider["Cloud Provider (e.g., Azure)"]');
  });

  it('leaves a breaker-free explicit-id subgraph label unquoted, id preserved', () => {
    const src = 'graph TD\n  subgraph Reporting[Reporting & Analytics]\n    A\n  end';
    const out = rule('subgraph-title-ids').apply(src);
    expect(out).toContain('subgraph Reporting[Reporting & Analytics]');
  });

  it('handles a diagram whose ONLY subgraph is an explicit-id form (no early no-op)', () => {
    const out = rule('subgraph-title-ids').apply(
      'graph TD\n  subgraph P[Provider (X)]\n    A\n  end\n  A --> B',
    );
    expect(out).toContain('subgraph P["Provider (X)"]');
    expect(out).toContain('A --> B');
  });
});

describe('quote-edge-labels', () => {
  const fix = rule('quote-edge-labels');

  it('quotes an edge label containing parentheses', () => {
    const out = fix.apply('graph TD\n  A -->|PR Webhook (JSON)| B');
    expect(out).toContain('|"PR Webhook (JSON)"|');
  });

  it('leaves breaker-free edge labels unquoted', () => {
    const src = 'graph TD\n  A -->|Web/API| B';
    expect(fix.apply(src)).toBe(src);
  });

  it('does not double-quote an already-quoted label', () => {
    const out = fix.apply('graph TD\n  A -->|"PR Webhook (JSON)"| B');
    expect(out).toContain('|"PR Webhook (JSON)"|');
    expect(out).not.toContain('""');
  });
});

describe('ALWAYS_ON', () => {
  it('de-quotes edge labels', () => {
    const out = applyFixes('graph TD\n  A -->|"X"| B', ALWAYS_ON);
    expect(out).toContain('|X|');
    expect(out).not.toContain('|"X"|');
  });

  it('KEEPS quotes on edge labels that contain a breaker char (parse-critical)', () => {
    const out = applyFixes('graph TD\n  A -->|"PR Webhook (JSON)"| B', ALWAYS_ON);
    expect(out).toContain('|"PR Webhook (JSON)"|');
  });

  it('normalises single-quoted breaker labels to double quotes', () => {
    const out = applyFixes("graph TD\n  A -->|'PR Webhook (JSON)'| B", ALWAYS_ON);
    expect(out).toContain('|"PR Webhook (JSON)"|');
  });
});
