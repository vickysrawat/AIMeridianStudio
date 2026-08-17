// =============================================================================
// Navigation
// =============================================================================

export type WorkspaceTab =
  | 'research'
  | 'use-case'
  | 'blueprint'
  | 'execution'
  | 'documents'
  | 'prompts'
  | 'presentation'
  | 'whitepaper'
  | 'compare'
  | 'library'
  | 'insights';

// =============================================================================
// Persisted Artifacts (knowledge hub: persistence, comparison, analytics, export)
// =============================================================================

export type ArtifactKind = 'research' | 'blueprint' | 'taskSpec' | 'document' | 'developerPrompt';
export type ExportFormat = 'markdown' | 'pdf' | 'docx';

export interface ArtifactMetadata {
  artifactId: string;
  kind: ArtifactKind;
  domain?: string;
  subDomain?: string;
  title?: string;
  modelUsed: string;
  requestHash: string;
  createdAt: string;
  version: number;
  lineageId: string;
  parentArtifactId?: string;
  schemaVersion: number;
  tags: string[];
  tenantId: string;
  createdBy?: string;
}

export interface StoredArtifact {
  metadata: ArtifactMetadata;
  payload: unknown;
}

// ── Comparison matrix ─────────────────────────────────────────────────────
export interface ComparisonColumn {
  artifactId: string;
  kind: ArtifactKind;
  title?: string;
  modelUsed: string;
  version: number;
  createdAt: string;
}
export interface ComparisonCell { artifactId: string; value?: string | null; divergent: boolean; }
export interface ComparisonRow { dimension: string; cells: ComparisonCell[]; }
export interface ComparisonMatrix {
  kind: ArtifactKind;
  columns: ComparisonColumn[];
  rows: ComparisonRow[];
}

// ── Cross-run analytics ─────────────────────────────────────────────────────
export interface PainPointCluster {
  title: string;
  occurrences: number;
  avgSeverity: number;
  domains: string[];
  sourceArtifactIds: string[];
}
export interface PainPointAnalytics { runsAnalyzed: number; clusters: PainPointCluster[]; }
export interface CompetitorPattern {
  competitorName: string;
  occurrences: number;
  featureGaps: string[];
  sourceArtifactIds: string[];
}
export interface CompetitorAnalytics { runsAnalyzed: number; patterns: CompetitorPattern[]; }

// ── White paper ─────────────────────────────────────────────────────────────
export interface OutputProvenance {
  modelUsed: string;
  providersAttempted?: string[];
  liveSourcesQueried?: string[];
  sourceCount?: number;
  factChecked?: boolean;
  confidence?: number;
  generatedAtUtc?: string;
}

/** Driven by ONE of: a research run (+optional opportunity), a use-case assessment, or (legacy) artifacts. */
export interface WhitePaperRequest {
  title?: string;
  researchArtifactId?: string;
  opportunityId?: string;
  assessmentId?: string;
  artifactIds?: string[];
  groundWithFreshResearch?: boolean;
  format?: string;
}
export interface WhitePaper {
  id: string;
  title: string;
  content: string;
  sourceArtifactIds: string[];
  modelUsed: string;
  createdAt: string;
  provenance?: OutputProvenance;
  sourcesQueried?: string[];
}

// =============================================================================
// API Error & Model Status
// =============================================================================

export interface ApiError {
  statusCode: number;
  message: string;
  errors?: Record<string, string[]>;
  traceId?: string;
}

export interface ModelStatusEvent {
  type: 'connected' | 'attempting' | 'succeeded' | 'failed' | 'fallback';
  provider: string;   // e.g. "Gemini (gemini-2.5-flash)" | "Heuristic Engine (Offline)"
  operation: string;  // e.g. "research" | "generate-blueprint"
  timestamp: string;
}

export interface ProviderStatusItem {
  name: string;        // e.g. "Gemini (gemini-2.5-flash)"
  priority: number;    // 1 = highest priority in the cascade
  configured: boolean; // true when an API key is present
  status: 'active' | 'idle' | 'failed' | 'quota' | 'unavailable' | 'not-configured' | 'fallback';
  reason: string;      // human-readable explanation
}

// =============================================================================
// Domain: Research  (POST /api/research)
// =============================================================================

export interface CompetitorInsight {
  competitorName: string;
  featureGap: string;
  impactScore: string;        // e.g. "8.5/10"
  strategicPlaybook: string;
}

// ── Dimension Weights ────────────────────────────────────────────────────────

export interface DimensionWeights {
  businessValue: number;            // all weights must sum to 100
  marketUrgency: number;
  feasibility: number;
  competitiveGap: number;
  implementationDifficulty: number; // inverted in composite: lower difficulty = better
  regulatoryTailwind: number;
  strategicFit: number;
  aiFitness: number;
}

// Domain profile → weight profile mapping
const HR = { businessValue:18, marketUrgency:14, feasibility:12, competitiveGap:10, implementationDifficulty:7,  regulatoryTailwind:20, strategicFit:6, aiFitness:13 };
const AI = { businessValue:18, marketUrgency:15, feasibility:20, competitiveGap:12, implementationDifficulty:8,  regulatoryTailwind:3,  strategicFit:8, aiFitness:16 };
const PS = { businessValue:21, marketUrgency:15, feasibility:14, competitiveGap:13, implementationDifficulty:8,  regulatoryTailwind:13, strategicFit:7, aiFitness:9  };
const OP = { businessValue:22, marketUrgency:16, feasibility:17, competitiveGap:13, implementationDifficulty:9,  regulatoryTailwind:6,  strategicFit:5, aiFitness:12 };
const BA = { businessValue:20, marketUrgency:16, feasibility:15, competitiveGap:12, implementationDifficulty:8,  regulatoryTailwind:8,  strategicFit:8, aiFitness:13 };

export const DOMAIN_WEIGHTS: Record<string, DimensionWeights> = {
  'Healthcare': HR, 'Pharmaceutical': HR, 'Financial Services': HR,
  'Insurance': HR, 'Government & Public Sector': HR, 'Tax': HR,
  'IT Services': AI, 'Telecommunications': AI, 'Manufacturing': AI,
  'Law': PS, 'Audit': PS, 'Advisory': PS, 'HR & Workforce': PS,
  'Retail & E-Commerce': OP, 'Supply Chain & Logistics': OP, 'Energy & Utilities': OP,
  'Real Estate': OP, 'Construction': OP, 'Agriculture': OP,
  'Travel & Hospitality': OP, 'Media & Entertainment': OP,
  'Education & EdTech': BA,
};

export function defaultWeightsForDomain(domain: string): DimensionWeights {
  return DOMAIN_WEIGHTS[domain] ?? BA;
}

// ── Dimension Importance (user-facing 5-level scale) ─────────────────────────
// Users set a coarse Off/Low/Medium/High/Critical per dimension instead of a 100-point budget.
// Levels are the edit surface; importanceToWeights() converts to a normalized DimensionWeights
// (scoring is proportion-invariant, so only the relative ratios matter).

export type ImportanceLevel = 'off' | 'low' | 'medium' | 'high' | 'critical';
export type DimensionImportance = Record<keyof DimensionWeights, ImportanceLevel>;

export const IMPORTANCE_LEVELS: ImportanceLevel[] = ['off', 'low', 'medium', 'high', 'critical'];
export const IMPORTANCE_LABELS: Record<ImportanceLevel, string> =
  { off: 'Off', low: 'Low', medium: 'Medium', high: 'High', critical: 'Critical' };
const IMPORTANCE_UNITS: Record<ImportanceLevel, number> = { off: 0, low: 1, medium: 2, high: 4, critical: 6 };

const DIMENSION_KEYS: (keyof DimensionWeights)[] = [
  'businessValue', 'marketUrgency', 'feasibility', 'competitiveGap',
  'implementationDifficulty', 'regulatoryTailwind', 'strategicFit', 'aiFitness',
];

/** Convert 5-level importance → DimensionWeights that sum to 100. All-Off falls back to all-Medium. */
export function importanceToWeights(imp: DimensionImportance): DimensionWeights {
  let units = DIMENSION_KEYS.map(k => IMPORTANCE_UNITS[imp[k]]);
  if (units.every(u => u === 0)) units = DIMENSION_KEYS.map(() => IMPORTANCE_UNITS.medium);
  const total = units.reduce((s, u) => s + u, 0);
  const raw = units.map(u => (u / total) * 100);
  const floored = raw.map(Math.floor);
  const remainder = 100 - floored.reduce((s, v) => s + v, 0);
  // Distribute the rounding remainder to the largest fractional parts.
  const order = raw.map((v, i) => [v - floored[i], i] as [number, number]).sort((a, b) => b[0] - a[0]);
  for (let i = 0; i < remainder; i++) floored[order[i % order.length][1]]++;
  const out = {} as DimensionWeights;
  DIMENSION_KEYS.forEach((k, i) => { out[k] = floored[i]; });
  return out;
}

/** Quantize a numeric weight (0–100 scale, avg ≈ 12.5) into a 5-level bucket — seeds levels from DOMAIN_WEIGHTS. */
export function weightToLevel(w: number): ImportanceLevel {
  if (w <= 0) return 'off';
  if (w <= 7) return 'low';
  if (w <= 13) return 'medium';
  if (w <= 19) return 'high';
  return 'critical';
}

export function weightsToImportance(w: DimensionWeights): DimensionImportance {
  const out = {} as DimensionImportance;
  DIMENSION_KEYS.forEach(k => { out[k] = weightToLevel(w[k]); });
  return out;
}

export function defaultImportanceForDomain(domain: string): DimensionImportance {
  return weightsToImportance(defaultWeightsForDomain(domain));
}

/** One-click intent presets (all-Medium base + emphasis). "Domain default" is applied separately. */
export interface ImportancePreset { id: string; label: string; importance: DimensionImportance; }
function imp(overrides: Partial<DimensionImportance>): DimensionImportance {
  const base = {} as DimensionImportance;
  DIMENSION_KEYS.forEach(k => { base[k] = 'medium'; });
  return { ...base, ...overrides };
}
export const PRESET_IMPORTANCE: ImportancePreset[] = [
  { id: 'balanced',      label: 'Balanced',         importance: imp({}) },
  { id: 'speed',         label: 'Speed to market',  importance: imp({ marketUrgency: 'critical', feasibility: 'high', businessValue: 'high', regulatoryTailwind: 'low', strategicFit: 'low' }) },
  { id: 'defensibility', label: 'Defensibility',    importance: imp({ competitiveGap: 'critical', strategicFit: 'high', businessValue: 'high', regulatoryTailwind: 'low' }) },
  { id: 'feasibility',   label: 'Feasibility-first', importance: imp({ feasibility: 'critical', implementationDifficulty: 'high', businessValue: 'high', competitiveGap: 'low', regulatoryTailwind: 'low' }) },
];

export function compositeScore(item: PrioritizedItem, w: DimensionWeights): number {
  const bv  = item.businessValue            ?? 5;
  const ur  = item.marketUrgency            ?? item.urgency;
  const fe  = item.feasibility              ?? (item.feasibilityScore || 5);
  const cg  = item.competitiveGap           ?? 5;
  const di  = item.implementationDifficulty ?? (10 - item.difficulty);
  const rt  = item.regulatoryTailwind       ?? 5;
  const sf  = item.strategicFit             ?? 5;
  const af  = item.aiFitness               ?? 5;
  return (bv * w.businessValue + ur * w.marketUrgency + fe * w.feasibility
        + cg * w.competitiveGap + (10 - di) * w.implementationDifficulty
        + rt * w.regulatoryTailwind + sf * w.strategicFit + af * w.aiFitness) / 100;
}

export function priorityBadge(composite: number, urgency: number): 'Critical' | 'High' | 'Medium' | 'Low' {
  if (composite >= 8.5 && urgency >= 8) return 'Critical';
  if (composite >= 7.0) return 'High';
  if (composite >= 5.0) return 'Medium';
  return 'Low';
}

// ── Pain Points ───────────────────────────────────────────────────────────────

export interface PainPoint {
  id: string;
  title: string;
  description: string;
  affectedSegment: string;
  severity: number;
  frequency: 'Widespread' | 'Common' | 'Occasional';
  relatedOpportunityIds: string[];
  liveSource?: string;
}

// ── Selected subdomain (Research tab left pane) ───────────────────────────────

export interface SelectedSubdomain {
  domain: string;
  subdomain: string;
  weights?: DimensionWeights;
  hasResults: boolean;
  isStale: boolean;   // results older than 60 minutes
}

export interface PrioritizedItem {
  id: string;
  name: string;
  description: string;
  urgency: number;             // 1-10 — market timing
  difficulty: number;          // 1-10 — implementation complexity
  value: number;               // 1-10 — business impact
  rationale: string;
  realLifeValue: string;
  integrationSteps: string;
  feasibilityScore: number;
  feasibilityAnalysis: string;
  // 8 dimension scores (present when DimensionWeights were used)
  businessValue?: number;
  marketUrgency?: number;
  feasibility?: number;
  competitiveGap?: number;
  implementationDifficulty?: number;
  regulatoryTailwind?: number;
  strategicFit?: number;
  aiFitness?: number;
}

export interface ResearchResponse {
  domain: string;
  domainsList: string[];
  competitorInsights: CompetitorInsight[];
  items: PrioritizedItem[];
  modelUsed: string;
  painPoints?: PainPoint[];
  liveSourcesQueried?: string[];
}

export interface ResearchRequest {
  keywords: string;
  userFeedback?: string;
  isRerun?: boolean;
  loadMore?: boolean;
  page?: number;
  existingItemIds?: string[];
  // new structured fields
  subDomain?: string;
  domain?: string;
  weights?: DimensionWeights;
}

// =============================================================================
// Domain: System Blueprint  (POST /api/generate-blueprint)
// =============================================================================

export interface ArchDecision {
  decision: string;
  chosenApproach: string;
  rationale: string;
  alternativesConsidered: string[];
  risks: string[];
}

export interface QualityAttribute {
  attribute: string;
  target: string;
  measurement: string;
}

export interface TechRadarEntry {
  layer: string;
  technologies: string[];
}

export interface BuyVsBuildOption {
  component: string;
  buyOption: string;
  buyRationale: string;
  buildApproach: string;
  buildRationale: string;
  recommendation: 'Buy' | 'Build' | 'Hybrid';
  recommendationReason: string;
}

export interface FeasibilityOption {
  name: string;
  verdict: string;          // "Feasible" | "Feasible with effort" | "Partial" | "Not recommended"
  score: number;            // 1–10
  effortEstimate: string;
  challenges: string[];
  roadblocks: string[];
  recommendation: string;
}

export interface FeasibilityAnalysis {
  useCase: string;
  summary: string;
  primaryConcernVerdict: string;
  options: FeasibilityOption[];
}

// =============================================================================
// Domain: Use-Case Assessment  (POST /api/assessment/stream)
// =============================================================================

export interface AssessmentSection {
  title: string;
  body: string;   // concise Markdown
}

export interface RecommendedDocument {
  expectedOutcome: string;
  title: string;
  templateType: string;   // executive-summary | technical-specification | proposal | governance-adr | ...
  rationale: string;
}

export interface Assessment {
  id: string;
  title: string;
  domain: string;
  useCase: string;
  context: string;
  problemStatement: string;
  objective: string;
  scopeOfWork: string;
  expectedOutcome: string;
  executiveSummary: string;
  sections: AssessmentSection[];
  recommendations: string[];
  risks: string[];
  nextSteps: string[];
  feasibility?: FeasibilityAnalysis | null;
  recommendedDocuments: RecommendedDocument[];
  modelUsed: string;
}

export interface AssessmentRequest {
  useCaseScenario?: string;   // quick free-form mode
  useCase?: string;
  context?: string;
  problemStatement?: string;
  objective?: string;
  scopeOfWork?: string;
  expectedOutcome?: string;
  domain?: string;
  /** When true (default), runs live web search first and grounds the assessment in real sources. */
  groundInLiveResearch?: boolean;
}

/** Readiness review of a use-case brief (POST /api/assessment/analyze) — advisory, non-blocking. */
export interface UseCaseReadiness {
  readinessScore: number;              // 0–100
  verdict: string;
  fields: FieldReview[];
  clarifyingQuestions: string[];
  suggestions: ImprovementSuggestion[];
  modelUsed: string;
}

export interface FieldReview {
  field: string;                       // useCaseScenario | useCase | context | problemStatement | objective | scopeOfWork | expectedOutcome
  status: 'missing' | 'weak' | 'strong';
  comment: string;
}

export interface ImprovementSuggestion {
  field: string;                       // which input field this improves
  suggestion: string;
  proposedText?: string;               // paste-ready scaffold for one-click Apply
}

/** Normalised source that the Documents tab can generate from: a blueprint or an assessment. */
export interface DocumentSource {
  kind: 'blueprint' | 'assessment';
  id: string;
  title: string;
  domain: string;
  solutionType: string;
  context: string;   // grounding prose (~first 1500 chars sent as blueprintContext)
}

export interface SystemBlueprint {
  id: string;
  solutionId: string;
  solutionName: string;
  domain: string;
  coreScenario: string;         // Markdown — 300+ word technical narrative
  baseTopology: string;         // Markdown — ASCII architecture diagram
  databaseSchemes: string;      // Markdown — SQL DDL with dynamic table names
  endpointManifest: string;     // Markdown — REST endpoint table
  resilienceStrategies: string; // Markdown — circuit breaker, retry, cascade config
  modelUsed: string;
  solutionType: string;           // e.g. "REST API" | "Azure Serverless" | "Event-Driven"
  solutionTypeConfidence: number; // 0.0–1.0
  archDecisions: ArchDecision[];
  qualityAttributes: QualityAttribute[];
  techRadar: TechRadarEntry[];
  buyVsBuild: BuyVsBuildOption[];
  projectNotes: string;
  feasibility?: FeasibilityAnalysis | null;  // populated only for use-case-driven blueprints
}

export interface GenerateBlueprintRequest {
  solutionId: string;
  solutionName: string;
  domain?: string;
  subDomain?: string;
  solutionDescription?: string;
  integrationSteps?: string;
  prioritySignal?: string;
  overrideSolutionType?: string;
  /** Persisted research artifact id — lets the server re-fetch the full opportunity and ground the
   *  blueprint prompt in its rich material (fidelity fix). Falls back to solutionDescription if absent. */
  researchArtifactId?: string;
  /** The selected opportunity's id (PrioritizedItem.id) within that research run. */
  opportunityId?: string;
  /** User-authored pre-generation context/constraints (typically from acting on the readiness critic).
   *  Woven into the prompt as an authoritative PROJECT CONTEXT block and persisted onto the blueprint. */
  projectNotes?: string;
}

// =============================================================================
// Domain: Task Execution  (POST /api/execute-task)
// =============================================================================

export interface TaskSpec {
  id: string;
  taskName: string;
  status: string;               // always "Completed" from the API
  progressScore: number;        // always 100 from the API
  systemicValue: string;
  estimatedEffort: string;
  generatedCodeTemplate: string; // compilable C# 13 service class
  outputLogs: string[];          // timestamped log lines
  modelUsed: string;
}

export interface ExecuteTaskRequest {
  taskName: string;
  systemicValue?: string;
  estimatedEffort?: string;
  context?: string;
  language?: string;  // csharp | typescript | python | java | go
  /** Ground the generated code in this blueprint's design (tech stack, endpoints, schema, resilience). */
  blueprintId?: string;
  assessmentId?: string;
}

// =============================================================================
// Domain: Corporate Document  (POST /api/generate-document)
// =============================================================================

export interface CorporateDocument {
  id: string;
  blueprintId: string;
  title: string;
  content: string;       // full Markdown document, 600+ words
  templateType: string;
  createdAt: string;     // ISO 8601
  modelUsed: string;
  // Goal-directed generation fields
  goalAchievementPct: number;   // 0–100
  goalAchieved: boolean;
  iterationsUsed: number;       // 1–3
  passedCriteria: string[];
  failedCriteria: string[];
  failureReasons: Record<string, string>; // criterion → why it failed
  effectiveGoal: string;        // the goal string that drove generation
  effectiveCriteria: string[];
  wasRefined: boolean;          // user edited a suggestion
  /** True only when a live model produced the doc AND it passed the goal/faithfulness judge.
   *  False for offline/heuristic output and single-pass legacy output — surface as "unverified". */
  factChecked: boolean;
  /** Stable id used to target by-id fixes. */
  documentId?: string;
  /** Canonical structured document — echo back on a Fix; the scorecard reads its criteria. */
  structured?: StructuredDocument;
  /** Fingerprint of the grounding blueprint at generation (drives GET /artifacts/{id}/freshness). */
  groundedBlueprintFingerprint?: string;
}

/** Result of POST /api/documents/freshness — is the doc current with its grounding blueprint? */
export interface FreshnessResult {
  fresh: boolean | null;   // null = unknown (legacy/assessment or blueprint not cached)
  reason: string;          // 'current' | 'stale' | 'unknown'
  detail: string;
}

/** Advisory post-document review (POST /api/documents/review) — domain / opportunity / faithfulness. */
export interface DocumentReview {
  reviewScore: number;   // 0–100
  verdict: string;
  findings: DocumentFinding[];
  modelUsed: string;
}
export interface DocumentFinding {
  axis: string;          // 'relevance' | 'opportunity-fidelity' | 'faithfulness'
  severity: string;      // 'high' | 'medium' | 'low'
  detail: string;
  suggestedFix?: string;
}

// ── Structured-native document (the document IS this; content is its render) ──
export interface DocumentSectionModel {
  id: string;
  heading: string;
  level: number;
  body: string;
  criterionIds: string[];
  citationIds: string[];
}
export interface CriterionState {
  id: string;
  text: string;
  passed: boolean;
  failureReason?: string | null;
  targetSectionIds: string[];
}
export interface SourceRef {
  id: string;            // "S1", "S2", …
  title: string;
  url?: string | null;
  origin: string;        // blueprint | assessment | research
  fetchedAt?: string | null;
  excerpt?: string | null;
}
export interface StructuredDocument {
  documentId: string;
  title: string;
  templateType: string;
  domain: string;
  subDomain: string;
  blueprintId?: string | null;
  assessmentId?: string | null;
  goal: string;
  blueprintContext?: string | null;
  sections: DocumentSectionModel[];
  criteria: CriterionState[];
  sources: SourceRef[];
}

export type DocumentTemplateType =
  | 'executive-summary'
  | 'market-analysis'
  | 'technical-specification'
  | 'proposal'
  | 'governance-adr'
  | 'developer-handbook'
  | 'detailed-design';

export interface GenerateDocumentRequest {
  blueprintId?: string;     // either blueprintId or assessmentId must be set
  assessmentId?: string;
  title: string;
  templateType: DocumentTemplateType;
  domain?: string;
  /** Specific sub-domain within the domain — drives sub-domain-specific few-shot retrieval. */
  subDomain?: string;
  solutionType?: string;
  /** First ~1500 chars of the blueprint's coreScenario — grounds the LLM in the correct tech stack. */
  blueprintContext?: string;
  isRerun?: boolean;
  // Mission fields — from user's selection (possibly refined)
  selectedTone?: string;
  selectedGoal?: string;
  selectedCriteria?: string[];
  wasRefined?: boolean;
  /**
   * Real competitor insights from the Research phase.
   * Only sent for market-analysis documents.
   * The API uses these to ground the LLM — it must not invent other competitors.
   */
  competitorInsights?: Array<{
    competitorName: string;
    featureGap: string;
    impactScore: string;
    strategicPlaybook: string;
  }>;
  /** Real research sources from the Research phase — grounds the doc and is cited as [S#]. */
  researchSources?: Array<{
    title: string;
    url?: string;
    source?: string;
    excerpt?: string;
  }>;
  /** When true (default), fact-heavy templates run live web grounding so vendor claims are cited. */
  groundInLiveResearch?: boolean;
  /** When set, the backend skips full generation and patches this content against the failed criteria. */
  existingContent?: string;
  /** Failure reasons from a prior run — fed to the patch prompt for targeted fixes. */
  knownFailureReasons?: Record<string, string>;
}

// =============================================================================
// Domain: Mission Suggestions  (POST /api/mission-suggestions)
// =============================================================================

export interface ToneOption {
  label: string;
  fullPhrase: string;
}

export interface GoalOption {
  label: string;
  text: string;
}

export interface CriteriaOption {
  label: string;
  criteria: string[];
}

export interface MissionSuggestions {
  persona: string;
  secondaryAudience: string;
  toneOptions: ToneOption[];
  goalOptions: GoalOption[];
  criteriaOptions: CriteriaOption[];
  modelUsed: string;
}

export interface MissionSuggestionsRequest {
  templateType: string;
  domain?: string;
  solutionType?: string;
  blueprintContext?: string;
}

export interface RecordSelectionRequest {
  templateType: string;
  domain?: string;
  solutionType?: string;
  selectedTone: string;
  selectedGoal: string;
  selectedCriteria: string[];
  wasRefined: boolean;
}

// =============================================================================
// Domain: Developer Prompt  (POST /api/generate-component-prompt)
// =============================================================================

export interface DeveloperPrompt {
  id: string;
  componentName: string;
  promptText: string;    // 500+ word structured developer handoff prompt
  targetLLM: string;
  directives: string;    // 6 numbered directives
  modelUsed: string;
}

export interface GenerateComponentPromptRequest {
  componentName: string;
  targetLLM?: string;
  context?: string;
}

// =============================================================================
// Domain: Project Download  (POST /api/generate-project → application/zip)
// =============================================================================

export interface GenerateProjectRequest {
  solutionName:     string;
  description?:     string;
  integrationSteps: string[];
  stepCodes:        string[];
  language?:        string;   // csharp | typescript | python | java | go
  domain?:          string;
  realLifeValue?:   string;
}

// =============================================================================
// Domain: Domain Discovery  (POST /api/domains/discover)
// =============================================================================

export interface DomainCategory {
  name: string;
  subDomains: string[];
}

export interface DomainSuggestions {
  domains: DomainCategory[];
  modelUsed: string;
}

// =============================================================================
// UI-only: Presentation Slides (built from blueprint data — no API call)
// =============================================================================

export type SlideLayout =
  | 'Title'
  | 'Content'
  | 'TwoColumn'
  | 'Diagram'
  | 'Comparison'
  | 'Quote'
  | 'Blank';

export type ContentBlockType = 'text' | 'bullets' | 'code' | 'diagram' | 'image' | 'table';

export interface ContentBlock {
  type: ContentBlockType;
  value: string | string[][];
  language?: string;
  caption?: string;
}

export interface Slide {
  id: string;
  title: string;
  subtitle?: string;
  slideNumber: number;
  layout: SlideLayout;
  contentBlocks: ContentBlock[];
  speakerNotes?: string;
  backgroundColor?: string;
  transitionType?: string;
}

// =============================================================================
// UI-only: Blueprint display helpers (used in architectural-blueprinter component)
// These are parsed/derived from SystemBlueprint markdown fields for display.
// =============================================================================

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';

export interface ApiEndpoint {
  method: HttpMethod;
  path: string;
  description: string;
  auth: string;
  requestPayload?: string;
  responsePayload?: string;
  tags: string[];
}

export type DbTechnology = 'PostgreSQL' | 'Redis' | 'Qdrant' | 'MongoDB' | 'SQLite';

export interface DatabaseSchema {
  name: string;
  technology: DbTechnology;
  script: string;
}

export type ResiliencePriority = 'Critical' | 'High' | 'Medium';

export interface ResilienceStrategy {
  name: string;
  pattern: string;
  description: string;
  failoverRoute?: string;
  priority: ResiliencePriority;
}

// =============================================================================
// UI-only: Prompt Studio local state
// =============================================================================

export type AiModel =
  | 'claude-sonnet-4-6'
  | 'gemini-2.5-flash'
  | 'llama-3.3-70b-versatile';
