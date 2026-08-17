import { describe, it, expect } from 'vitest';
import { repairDiagram } from './repair.js';
import { validateDiagram } from './validate.js';

// Integration: exercises the real mermaid + jsdom validator (first parse warms mermaid, so allow time).
describe('repair loop (headless mermaid + jsdom)', () => {
  it('repairs the reported bare-node diagram until it parses', async () => {
    const broken = [
      'graph TD',
      '    User -->|Web/API| Azure Front Door',
      '    Azure Front Door -->|HTTPS| Azure App Gateway',
      '    subgraph Azure - Primary Hyperscaler',
      '        Azure Front Door',
      '        Azure App Gateway',
      '    end',
    ].join('\n');

    const r = await repairDiagram(broken);
    expect(r.ok).toBe(true);
    expect(r.rulesApplied).toContain('bare-multiword-nodes');
    // repaired output actually parses
    expect((await validateDiagram(r.repaired)).ok).toBe(true);
  }, 30_000);

  it('leaves a valid diagram unchanged (no rules applied)', async () => {
    const good = 'graph TD\n  A[Start] --> B[End]';
    const r = await repairDiagram(good);
    expect(r.ok).toBe(true);
    expect(r.rulesApplied).toEqual([]);
  }, 30_000);
});
