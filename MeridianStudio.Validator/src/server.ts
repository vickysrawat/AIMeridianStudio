/**
 * Fastify HTTP surface for the browserless validate-and-repair sidecar.
 *   GET  /healthz              → liveness
 *   POST /validate/diagram     { source }            → { ok, error?, errorSignature? }
 *   POST /repair/diagram       { source }            → { ok, repaired, rulesApplied[], error?, errorSignature? }
 *   POST /validate/document    { markdown }          → { ok, diagrams[], issues[] }
 * Pure/deterministic — no LLM here (the .NET API owns the tiered LLM tier).
 */
import Fastify from 'fastify';
import { validateDiagram, validateDocument } from './validate.js';
import { repairDiagram } from './repair.js';

const app = Fastify({ logger: true, bodyLimit: 1_000_000 });

app.get('/healthz', async () => ({ status: 'ok', service: 'meridian-validator' }));

app.post<{ Body: { source?: string } }>('/validate/diagram', async (req, reply) => {
  const source = req.body?.source;
  if (typeof source !== 'string' || source.trim() === '')
    return reply.code(400).send({ error: 'source is required' });
  return validateDiagram(source);
});

app.post<{ Body: { source?: string } }>('/repair/diagram', async (req, reply) => {
  const source = req.body?.source;
  if (typeof source !== 'string' || source.trim() === '')
    return reply.code(400).send({ error: 'source is required' });
  return repairDiagram(source);
});

app.post<{ Body: { markdown?: string } }>('/validate/document', async (req, reply) => {
  const markdown = req.body?.markdown;
  if (typeof markdown !== 'string')
    return reply.code(400).send({ error: 'markdown is required' });
  return validateDocument(markdown);
});

const port = Number(process.env.PORT ?? 5177);
const host = process.env.HOST ?? '127.0.0.1';
app.listen({ port, host }).catch((err) => {
  app.log.error(err);
  process.exit(1);
});
