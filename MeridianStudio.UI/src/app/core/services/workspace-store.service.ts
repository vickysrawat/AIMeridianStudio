import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { EMPTY, Observable, of, throwError } from 'rxjs';
import { HttpContext } from '@angular/common/http';
import { catchError, finalize, map, switchMap, tap } from 'rxjs/operators';
import { API_BASE_URL } from '../tokens/api-base-url.token';
import { SILENT_ERROR } from '../interceptors/http-context.tokens';
import {
  ArchDecision,
  ApiError,
  Assessment,
  AssessmentRequest,
  UseCaseReadiness,
  DocumentSource,
  RecommendedDocument,
  SelectedSubdomain,
  DimensionWeights,
  DimensionImportance,
  defaultImportanceForDomain,
  importanceToWeights,
  CorporateDocument,
  DeveloperPrompt,
  DomainCategory,
  DomainSuggestions,
  ExecuteTaskRequest,
  GenerateBlueprintRequest,
  GenerateComponentPromptRequest,
  GenerateDocumentRequest,
  DocumentReview,
  FreshnessResult,
  StructuredDocument,
  GenerateProjectRequest,
  MissionSuggestions,
  MissionSuggestionsRequest,
  ModelStatusEvent,
  ProviderStatusItem,
  PrioritizedItem,
  RecordSelectionRequest,
  ResearchRequest,
  ResearchResponse,
  Slide,
  SystemBlueprint,
  TaskSpec,
  WorkspaceTab,
  ArtifactKind,
  ArtifactMetadata,
  StoredArtifact,
  ComparisonMatrix,
  PainPointAnalytics,
  CompetitorAnalytics,
  WhitePaperRequest,
  WhitePaper,
  ExportFormat,
} from '../models/interfaces';

@Injectable({ providedIn: 'root' })
export class WorkspaceStoreService {
  private readonly http     = inject(HttpClient);
  private readonly apiBase  = inject(API_BASE_URL);

  // ── Navigation ────────────────────────────────────────────────────────────
  readonly activeWorkspace  = signal<WorkspaceTab>('research');
  readonly searchQuery      = signal<string>('');

  // ── Research ──────────────────────────────────────────────────────────────
  readonly currentResearchData  = signal<ResearchResponse | null>(null);
  readonly selectedSolution     = signal<PrioritizedItem | null>(null);
  private readonly _researchPage = signal<number>(1);

  // ── Blueprint ─────────────────────────────────────────────────────────────
  readonly isGeneratingBlueprint = signal<boolean>(false);
  readonly compiledBlueprint     = signal<SystemBlueprint | null>(null);
  readonly blueprintStreamText   = signal<string>('');
  // User override for the auto-detected solution type
  readonly solutionTypeOverride  = signal<string | null>(null);
  readonly effectiveSolutionType = computed(
    () => this.solutionTypeOverride() ?? this.compiledBlueprint()?.solutionType ?? '',
  );
  /** User-authored pre-generation context/constraints (populated by acting on the readiness critic).
   *  Threaded into the generate/stream/readiness requests as projectNotes. */
  readonly preGenNotes = signal<string>('');

  // ── Use-Case Assessment ─────────────────────────────────────────────────────
  readonly isGeneratingAssessment = signal<boolean>(false);
  readonly currentAssessment      = signal<Assessment | null>(null);
  readonly assessmentStreamText   = signal<string>('');
  readonly isAnalyzingUseCase     = signal<boolean>(false);
  readonly useCaseReadiness       = signal<UseCaseReadiness | null>(null);
  readonly isAnalyzingBlueprint   = signal<boolean>(false);
  readonly blueprintReadiness     = signal<UseCaseReadiness | null>(null);
  readonly isReviewingDocument    = signal<boolean>(false);
  readonly documentReview         = signal<DocumentReview | null>(null);
  readonly documentFreshness      = signal<FreshnessResult | null>(null);
  /** When set (from a recommended deliverable), Document Studio pre-selects and auto-runs it. */
  readonly pendingDocumentTemplate = signal<{ templateType: string; title: string } | null>(null);

  /**
   * Normalised grounding source for the Documents tab: the active assessment takes
   * precedence (Use Case workflow), otherwise the compiled blueprint.
   */
  readonly documentSource = computed<DocumentSource | null>(() => {
    const a = this.currentAssessment();
    if (a) {
      const ctx = [a.executiveSummary, ...a.sections.map(s => `## ${s.title}\n${s.body}`)]
        .filter(Boolean).join('\n\n');
      return { kind: 'assessment', id: a.id, title: a.title, domain: a.domain, solutionType: '', context: ctx };
    }
    const bp = this.compiledBlueprint();
    if (bp) {
      return { kind: 'blueprint', id: bp.id, title: bp.solutionName, domain: bp.domain,
               solutionType: bp.solutionType, context: bp.coreScenario ?? '' };
    }
    return null;
  });

  // ── Execution ─────────────────────────────────────────────────────────────
  readonly currentTaskSpec  = signal<TaskSpec | null>(null);
  readonly isExecutingTask  = signal<boolean>(false);
  readonly logsQueue        = signal<string[]>([]);

  // ── Documents ─────────────────────────────────────────────────────────────
  readonly currentDocument      = signal<CorporateDocument | null>(null);
  readonly isGeneratingDocument = signal<boolean>(false);

  // ── Prompt Studio ─────────────────────────────────────────────────────────
  readonly currentPrompt    = signal<DeveloperPrompt | null>(null);
  readonly isGeneratingPrompt = signal<boolean>(false);

  // ── Model Status (SSE) ────────────────────────────────────────────────────
  readonly currentModelStatus = signal<ModelStatusEvent | null>(null);

  // ── Provider Status Modal ─────────────────────────────────────────────────
  readonly isProviderModalOpen = signal<boolean>(false);
  readonly providerStatuses    = signal<ProviderStatusItem[]>([]);

  // ── Domain Management ─────────────────────────────────────────────────────
  readonly isDomainSettingsOpen  = signal<boolean>(false);
  readonly discoveredDomains     = signal<DomainCategory[]>([]);
  readonly preferredDomains      = signal<string[]>([]);
  readonly activeDomain          = signal<string>('');
  readonly isDiscoveringDomains  = signal<boolean>(false);

  // ── Structured subdomain research (new) ──────────────────────────────────
  readonly selectedSubdomains    = signal<SelectedSubdomain[]>([]);
  readonly activeSubdomain       = signal<SelectedSubdomain | null>(null);
  // Per-subdomain dimension importance (5-level scale = source of truth): key = "domain|subdomain".
  // Weights sent to the API are derived from these via importanceToWeights().
  private readonly _subdomainImportance = new Map<string, DimensionImportance>();

  // ── Presentation (UI-only, no API backing) ────────────────────────────────
  readonly activeSlideDeck  = signal<Slide[]>([]);

  // ── Global ────────────────────────────────────────────────────────────────
  readonly isLoading = signal<boolean>(false);
  readonly error     = signal<ApiError | null>(null);

  // ── Computed ──────────────────────────────────────────────────────────────
  readonly hasResearchResults = computed(
    () => (this.currentResearchData()?.items.length ?? 0) > 0,
  );

  readonly canLoadMore = computed(
    () => this.currentResearchData() !== null,
  );

  readonly taskProgress = computed(
    () => this.currentTaskSpec()?.progressScore ?? 0,
  );

  // ── Persistence helpers ───────────────────────────────────────────────────
  private static readonly PREFS_KEY    = 'meridian-workspace-prefs';
  private static readonly PREFS_TTL_MS = 24 * 60 * 60 * 1000;

  // In-memory per-domain result cache — keyed by domain name string
  private readonly _domainResultsCache = new Map<string, ResearchResponse>();

  constructor() {
    this._loadPersistedPrefs();
    this._setupModelStatusStream();
  }

  private _loadPersistedPrefs(): void {
    try {
      const raw = localStorage.getItem(WorkspaceStoreService.PREFS_KEY);
      if (!raw) return;
      const prefs = JSON.parse(raw) as {
        domains?: string[];
        lastDomain?: string;
        expiresAt?: number;
        selectedSubdomains?: { domain: string; subdomain: string }[];
      };
      if (typeof prefs.expiresAt === 'number' && prefs.expiresAt < Date.now()) {
        localStorage.removeItem(WorkspaceStoreService.PREFS_KEY);
        return;
      }
      if (Array.isArray(prefs.domains) && prefs.domains.length > 0)
        this.preferredDomains.set(prefs.domains);
      if (prefs.lastDomain)
        this.activeDomain.set(prefs.lastDomain);
      // Restore research area subdomains
      if (Array.isArray(prefs.selectedSubdomains) && prefs.selectedSubdomains.length > 0) {
        prefs.selectedSubdomains.forEach(({ domain, subdomain }) =>
          this.addSubdomain(domain, subdomain));
        const first = this.selectedSubdomains()[0];
        if (first) this.setActiveSubdomain(first.domain, first.subdomain);
      }
    } catch { /* ignore */ }
  }

  private _setupModelStatusStream(): void {
    const connect = () => {
      const es = new EventSource(`${this.apiBase}/api/events/model-status`);
      es.onmessage = ({ data }) => {
        try {
          this.currentModelStatus.set(JSON.parse(data) as ModelStatusEvent);
        } catch { /* ignore malformed frames */ }
      };
      es.onerror = () => {
        es.close();
        setTimeout(connect, 5000);
      };
    };
    connect();
  }

  private _persistPrefs(): void {
    try {
      localStorage.setItem(
        WorkspaceStoreService.PREFS_KEY,
        JSON.stringify({
          domains:            this.preferredDomains(),
          lastDomain:         this.activeDomain(),
          selectedSubdomains: this.selectedSubdomains().map(s => ({ domain: s.domain, subdomain: s.subdomain })),
          expiresAt:          Date.now() + WorkspaceStoreService.PREFS_TTL_MS,
        }),
      );
    } catch { /* ignore */ }
  }

  // ── Navigation ────────────────────────────────────────────────────────────

  setActiveWorkspace(tab: WorkspaceTab): void {
    this.activeWorkspace.set(tab);
  }

  openDomainSettings(): void {
    this.isDomainSettingsOpen.set(true);
  }

  closeDomainSettings(): void {
    this.isDomainSettingsOpen.set(false);
  }

  openProviderModal(): void {
    this.providerStatuses.set([]);
    this.isProviderModalOpen.set(true);
    this.http
      .get<ProviderStatusItem[]>(`${this.apiBase}/api/providers/status`, {
        context: new HttpContext().set(SILENT_ERROR, true),
      })
      .pipe(catchError(() => of([])))
      .subscribe(statuses => this.providerStatuses.set(statuses));
  }

  closeProviderModal(): void {
    this.isProviderModalOpen.set(false);
  }

  setActiveDomain(domain: string): void {
    const next = this.activeDomain() === domain ? '' : domain;
    this.activeDomain.set(next);
    // Restore cached results for this domain if available
    const cached = next ? this._domainResultsCache.get(next) : null;
    this.currentResearchData.set(cached ?? null);
    // Keep searchQuery in sync with the active domain: rerun / load-more build their request
    // from searchQuery, so leaving it empty here makes the API reject them (400 "Keywords").
    if (next) this.searchQuery.set(next);
  }

  setSearchQuery(query: string): void {
    this.searchQuery.set(query);
  }

  setSelectedSolution(item: PrioritizedItem | null): void {
    const current = this.selectedSolution();
    // Clear the blueprint when the user picks a different trend/solution so the
    // Blueprint tab shows a clean state rather than the last generated blueprint.
    // (The user explicitly chose a new solution — showing an old blueprint is misleading.)
    if (item?.id !== current?.id) {
      const cachedBp = this.compiledBlueprint();
      // Only clear if the current blueprint belongs to a different solution
      if (cachedBp && cachedBp.solutionId !== item?.id) {
        this.compiledBlueprint.set(null);
        this.blueprintStreamText.set('');
      }
    }
    this.selectedSolution.set(item);
  }

  clearError(): void {
    this.error.set(null);
  }

  // ── Research API  POST /api/research ─────────────────────────────────────

  submitResearchQuery(
    keywords: string,
    userFeedback?: string,
    isRerun = false,
  ): Observable<ResearchResponse> {
    if (isRerun) this._domainResultsCache.delete(keywords);
    const body: ResearchRequest = {
      keywords,
      userFeedback,
      isRerun,
      loadMore: false,
      page: 1,
    };

    this.isLoading.set(true);
    this.error.set(null);
    this._researchPage.set(1);

    return this.http
      .post<ResearchResponse>(`${this.apiBase}/api/research`, body)
      .pipe(
        tap(response => {
          this.currentResearchData.set(response);
          this.searchQuery.set(keywords);
          // Cache results by domain so switching back shows them instantly
          this._domainResultsCache.set(keywords, response);
          // Promote the analyzed keyword to the active domain + persist
          this.activeDomain.set(keywords);
          this._persistPrefs();
        }),
        catchError(err => this.handleError(err)),
        finalize(() => this.isLoading.set(false)),
      );
  }

  rerunResearchQuery(): Observable<ResearchResponse> {
    const keywords = this.searchQuery();
    if (!keywords) return EMPTY;

    const body: ResearchRequest = { keywords, isRerun: true };
    this.isLoading.set(true);
    this.error.set(null);

    return this.http
      .post<ResearchResponse>(`${this.apiBase}/api/research`, body)
      .pipe(
        tap(response => {
          this.currentResearchData.set(response);
          this._researchPage.set(1);
        }),
        catchError(err => this.handleError(err)),
        finalize(() => this.isLoading.set(false)),
      );
  }

  loadMoreSolutions(): Observable<ResearchResponse> {
    const current = this.currentResearchData();
    if (!current) return EMPTY;

    // Derive keywords robustly: searchQuery is the primary source, but if results were
    // restored from a cached/active domain it may be empty — fall back to the active domain
    // and finally the response's own domain. Guard against an empty value so we never fire a
    // request the API will reject with a 400 "Keywords" error.
    const keywords = (this.searchQuery() || this.activeDomain() || current.domain || '').trim();
    if (!keywords) return EMPTY;

    const existingIds = current.items.map(i => i.id);
    const nextPage    = this._researchPage() + 1;

    const body: ResearchRequest = {
      keywords,
      loadMore:        true,
      page:            nextPage,
      existingItemIds: existingIds,
    };

    this.isLoading.set(true);

    return this.http
      .post<ResearchResponse>(`${this.apiBase}/api/research`, body)
      .pipe(
        tap(response => {
          const existingIdSet = new Set(existingIds);
          const uniqueItems   = response.items.filter(i => !existingIdSet.has(i.id));
          this.currentResearchData.update(prev =>
            prev
              ? { ...prev, items: [...prev.items, ...uniqueItems] }
              : response,
          );
          this._researchPage.set(nextPage);
        }),
        catchError(err => this.handleError(err)),
        finalize(() => this.isLoading.set(false)),
      );
  }

  setSolutionTypeOverride(type: string | null): void {
    this.solutionTypeOverride.set(type);
  }

  // ── Blueprint API  POST /api/generate-blueprint ───────────────────────────

  generateBlueprint(item: PrioritizedItem): Observable<SystemBlueprint> {
    const override = this.solutionTypeOverride();
    const body: GenerateBlueprintRequest = {
      solutionId:           item.id,
      solutionName:         item.name,
      projectNotes:         this.preGenNotes().trim() || undefined,
      ...(override ? { overrideSolutionType: override } : {}),
    };

    this.isGeneratingBlueprint.set(true);
    this.solutionTypeOverride.set(null); // reset override after use
    this.error.set(null);

    return this.http
      .post<SystemBlueprint>(`${this.apiBase}/api/generate-blueprint`, body)
      .pipe(
        tap(blueprint => this.compiledBlueprint.set(blueprint)),
        catchError(err => this.handleError(err)),
        finalize(() => this.isGeneratingBlueprint.set(false)),
      );
  }

  // ── Blueprint Patch  PATCH /api/blueprint/{id} ───────────────────────────

  patchBlueprint(blueprintId: string, patch: Partial<SystemBlueprint>): Observable<SystemBlueprint> {
    return this.http
      .patch<SystemBlueprint>(`${this.apiBase}/api/blueprint/${blueprintId}`, patch)
      .pipe(tap(bp => this.compiledBlueprint.set(bp)));
  }

  // ── Topology Regeneration  POST /api/blueprint/{id}/regenerate-topology ───

  readonly isRegeneratingTopology = signal(false);
  readonly streamedTopology       = signal('');

  regenerateTopology(blueprintId: string): void {
    this.isRegeneratingTopology.set(true);
    this.streamedTopology.set('');

    fetch(`${this.apiBase}/api/blueprint/${blueprintId}/regenerate-topology`, {
      method: 'POST',
      headers: { Accept: 'text/event-stream' },
    }).then(res => {
      if (!res.ok || !res.body) { this.isRegeneratingTopology.set(false); return; }
      const reader  = res.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '', currentEvent = '';

      const pump = (): Promise<void> =>
        reader.read().then(({ done, value }) => {
          if (done) { this.isRegeneratingTopology.set(false); return; }
          buffer += decoder.decode(value, { stream: true });
          const lines = buffer.split('\n');
          buffer = lines.pop() ?? '';
          for (const line of lines) {
            if (line.startsWith('event: ')) { currentEvent = line.slice(7).trim(); }
            else if (line.startsWith('data: ')) {
              const raw = line.slice(6);
              try {
                if (currentEvent === 'chunk') {
                  const text = JSON.parse(raw) as string;
                  this.streamedTopology.update(t => t + text);
                } else if (currentEvent === 'complete') {
                  this.compiledBlueprint.set(JSON.parse(raw) as SystemBlueprint);
                  this.streamedTopology.set('');
                  this.isRegeneratingTopology.set(false);
                }
              } catch { /* skip */ }
            }
          }
          return pump();
        }).catch(() => this.isRegeneratingTopology.set(false));

      pump();
    }).catch(() => this.isRegeneratingTopology.set(false));
  }

  // ── Blueprint Streaming  POST /api/generate-blueprint/stream ─────────────

  generateBlueprintStream(item: PrioritizedItem): void {
    const override    = this.solutionTypeOverride();
    const research    = this.currentResearchData();
    // activeDomain is the selected sub-domain (e.g. "Cloud Infrastructure");
    // research.domain is the top-level category (e.g. "IT Services" / "Core Software & Tech")
    const subDomain   = this.activeDomain() || undefined;
    const topDomain   = research?.domain ?? undefined;
    // Build opportunity context from the item so the LLM specialises to this specific trend
    const descParts   = [item.description, item.rationale, item.realLifeValue].filter(Boolean);
    const description = descParts.join(' ').slice(0, 600) || undefined;
    // Forward the intended implementation approach + how research prioritised this opportunity
    const integrationSteps = item.integrationSteps?.slice(0, 600) || undefined;
    const prioritySignal   = [item.urgency, item.difficulty, item.value].every(v => typeof v === 'number')
      ? `Urgency ${item.urgency}/10 · Difficulty ${item.difficulty}/10 · Value ${item.value}/10`
      : undefined;
    const body: GenerateBlueprintRequest = {
      solutionId:          item.id,
      solutionName:        item.name,
      domain:              topDomain,
      subDomain:           subDomain,
      solutionDescription: description,
      integrationSteps:    integrationSteps,
      prioritySignal:      prioritySignal,
      opportunityId:       item.id,
      projectNotes:        this.preGenNotes().trim() || undefined,
      ...(override ? { overrideSolutionType: override } : {}),
    };

    // Resolve the persisted research artifact so the server re-fetches the FULL opportunity material
    // (fidelity fix). Fail-soft: if it can't be resolved, stream without it — the server falls back to
    // solutionDescription. Never blocks generation.
    this.resolveResearchArtifactId().subscribe(researchArtifactId =>
      this.streamBlueprint(researchArtifactId ? { ...body, researchArtifactId } : body));
  }

  /** Resolve the current run's persisted research artifact id (subdomain-matched, else latest). Fail-soft → undefined. */
  private resolveResearchArtifactId(): Observable<string | undefined> {
    const domain = this.currentResearchData()?.domain;
    const sub = this.activeSubdomain()?.subdomain;
    return this.listArtifacts({ kind: 'research', domain, latestOnly: true, take: 50 }).pipe(
      map(rows => {
        const match = (sub
          ? rows.find(r => (r.subDomain ?? '').toLowerCase() === sub.toLowerCase()
                        || (r.title ?? '').toLowerCase() === sub.toLowerCase())
          : undefined) ?? rows[0];
        return match?.artifactId;
      }),
      catchError(() => of(undefined)),
    );
  }

  private streamBlueprint(body: GenerateBlueprintRequest): void {
    this.isGeneratingBlueprint.set(true);
    this.blueprintStreamText.set('');
    this.compiledBlueprint.set(null);   // clear stale blueprint while streaming
    this.solutionTypeOverride.set(null);
    this.error.set(null);

    fetch(`${this.apiBase}/api/generate-blueprint/stream`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
      body: JSON.stringify(body),
    }).then(res => {
      if (!res.ok || !res.body) {
        this.isGeneratingBlueprint.set(false);
        return;
      }

      const reader  = res.body.getReader();
      const decoder = new TextDecoder();
      let buffer    = '';
      let currentEvent = '';

      const pump = (): Promise<void> =>
        reader.read().then(({ done, value }) => {
          if (done) {
            this.isGeneratingBlueprint.set(false);
            return;
          }

          buffer += decoder.decode(value, { stream: true });
          const lines = buffer.split('\n');
          buffer = lines.pop() ?? '';

          for (const line of lines) {
            if (line.startsWith('event: ')) {
              currentEvent = line.slice(7).trim();
            } else if (line.startsWith('data: ')) {
              const raw = line.slice(6);
              try {
                if (currentEvent === 'chunk') {
                  // Server JSON-encodes chunk text to safely handle newlines
                  const text = JSON.parse(raw) as string;
                  this.blueprintStreamText.update(t => t + text);
                } else if (currentEvent === 'complete') {
                  this.compiledBlueprint.set(JSON.parse(raw) as SystemBlueprint);
                  this.blueprintStreamText.set('');
                  this.isGeneratingBlueprint.set(false);
                  // Pre-gen context is now persisted on the blueprint (Project Context) — clear the draft.
                  this.preGenNotes.set('');
                } else if (currentEvent === 'error') {
                  this.isGeneratingBlueprint.set(false);
                }
              } catch { /* malformed SSE data — skip */ }
            }
          }

          return pump();
        }).catch(() => this.isGeneratingBlueprint.set(false));

      pump();
    }).catch(() => this.isGeneratingBlueprint.set(false));
  }

  // ── Use-Case Assessment  POST /api/assessment/stream ─────────────────────

  /** Generate a standalone Assessment from a free-form scenario or a structured brief. */
  generateAssessment(request: AssessmentRequest): void {
    this.isGeneratingAssessment.set(true);
    this.assessmentStreamText.set('');
    this.currentAssessment.set(null);
    this.error.set(null);

    fetch(`${this.apiBase}/api/assessment/stream`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
      body: JSON.stringify(request),
    }).then(res => {
      if (!res.ok || !res.body) { this.isGeneratingAssessment.set(false); return; }

      const reader  = res.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';
      let currentEvent = '';

      const pump = (): Promise<void> =>
        reader.read().then(({ done, value }) => {
          if (done) { this.isGeneratingAssessment.set(false); return; }

          buffer += decoder.decode(value, { stream: true });
          const lines = buffer.split('\n');
          buffer = lines.pop() ?? '';

          for (const line of lines) {
            if (line.startsWith('event: ')) {
              currentEvent = line.slice(7).trim();
            } else if (line.startsWith('data: ')) {
              const raw = line.slice(6);
              try {
                if (currentEvent === 'chunk') {
                  this.assessmentStreamText.update(t => t + (JSON.parse(raw) as string));
                } else if (currentEvent === 'complete') {
                  this.currentAssessment.set(JSON.parse(raw) as Assessment);
                  this.assessmentStreamText.set('');
                  this.isGeneratingAssessment.set(false);
                } else if (currentEvent === 'error') {
                  this.isGeneratingAssessment.set(false);
                }
              } catch { /* malformed SSE data — skip */ }
            }
          }
          return pump();
        }).catch(() => this.isGeneratingAssessment.set(false));

      pump();
    }).catch(() => this.isGeneratingAssessment.set(false));
  }

  /** Apply chat/edit overrides to the cached assessment. */
  patchAssessment(assessmentId: string, patch: Partial<Assessment>): Observable<Assessment> {
    return this.http
      .patch<Assessment>(`${this.apiBase}/api/assessment/${assessmentId}`, patch)
      .pipe(tap(a => this.currentAssessment.set(a)));
  }

  /** Analyze a use-case brief for readiness (advisory) — POST /api/assessment/analyze. */
  analyzeUseCase(request: AssessmentRequest): Observable<UseCaseReadiness> {
    this.isAnalyzingUseCase.set(true);
    return this.http
      .post<UseCaseReadiness>(`${this.apiBase}/api/assessment/analyze`, request)
      .pipe(
        tap(r => { this.useCaseReadiness.set(r); this.isAnalyzingUseCase.set(false); }),
        catchError(err => { this.isAnalyzingUseCase.set(false); return throwError(() => err); }),
      );
  }

  /** Check whether a document is still current with its grounding blueprint — POST /api/documents/freshness. Fail-soft. */
  checkDocumentFreshness(doc: CorporateDocument): void {
    if (!doc.groundedBlueprintFingerprint || !doc.blueprintId) { this.documentFreshness.set(null); return; }
    this.http.post<FreshnessResult>(`${this.apiBase}/api/documents/freshness`, {
      blueprintId:         doc.blueprintId,
      groundedFingerprint: doc.groundedBlueprintFingerprint,
    }).pipe(catchError(() => of(null))).subscribe(r => this.documentFreshness.set(r));
  }

  /** Advisory review of the CURRENT document (domain / opportunity-fidelity / faithfulness) — POST /api/documents/review. */
  reviewDocument(): Observable<DocumentReview> {
    const doc = this.currentDocument()!;   // caller guards on currentDocument()
    const src = this.documentSource();
    this.isReviewingDocument.set(true);
    const body = {
      content:      doc.content,
      title:        doc.title,
      domain:       src?.domain,
      subDomain:    this.activeSubdomain()?.subdomain || undefined,
      templateType: doc.templateType,
      ...(src?.kind === 'assessment'
          ? { assessmentId: src.id }
          : { blueprintId: doc.blueprintId || src?.id }),
    };
    return this.http.post<DocumentReview>(`${this.apiBase}/api/documents/review`, body).pipe(
      tap(r => { this.documentReview.set(r); this.isReviewingDocument.set(false); }),
      catchError(err => { this.isReviewingDocument.set(false); return throwError(() => err); }),
    );
  }

  /** Analyze a research opportunity for blueprint-readiness (advisory) — POST /api/generate-blueprint/readiness.
   *  Resolves the persisted research artifact so the server critiques the FULL opportunity material. */
  analyzeBlueprintReadiness(item: PrioritizedItem): Observable<UseCaseReadiness> {
    this.isAnalyzingBlueprint.set(true);
    const research  = this.currentResearchData();
    const descParts = [item.description, item.rationale, item.realLifeValue].filter(Boolean);
    const body: GenerateBlueprintRequest = {
      solutionId:          item.id,
      solutionName:        item.name,
      domain:              research?.domain ?? undefined,
      subDomain:           this.activeDomain() || undefined,
      solutionDescription: descParts.join(' ').slice(0, 600) || undefined,
      integrationSteps:    item.integrationSteps?.slice(0, 600) || undefined,
      opportunityId:       item.id,
      projectNotes:        this.preGenNotes().trim() || undefined,
    };
    return this.resolveResearchArtifactId().pipe(
      switchMap(researchArtifactId =>
        this.http.post<UseCaseReadiness>(
          `${this.apiBase}/api/generate-blueprint/readiness`,
          researchArtifactId ? { ...body, researchArtifactId } : body)),
      tap(r => { this.blueprintReadiness.set(r); this.isAnalyzingBlueprint.set(false); }),
      catchError(err => { this.isAnalyzingBlueprint.set(false); return throwError(() => err); }),
    );
  }

  /** Launch a recommended deep deliverable as a Document grounded in the current assessment. */
  generateDocumentFromAssessment(rec: RecommendedDocument): void {
    this.pendingDocumentTemplate.set({ templateType: rec.templateType, title: rec.title });
    this.setActiveWorkspace('documents');
  }

  // ── Mission Suggestions  POST /api/mission-suggestions ───────────────────

  getMissionSuggestions(request: MissionSuggestionsRequest): Observable<MissionSuggestions> {
    return this.http
      .post<MissionSuggestions>(`${this.apiBase}/api/mission-suggestions`, request)
      .pipe(catchError(() => of(null as unknown as MissionSuggestions)));
  }

  recordMissionSelection(request: RecordSelectionRequest): void {
    // Fire-and-forget training signal — errors are silently ignored
    this.http
      .post(`${this.apiBase}/api/mission-suggestions/record`, request)
      .pipe(catchError(() => EMPTY))
      .subscribe();
  }

  // ── Execution API  POST /api/execute-task ─────────────────────────────────

  executeTask(request: ExecuteTaskRequest): Observable<TaskSpec> {
    this.isExecutingTask.set(true);
    this.error.set(null);
    this.clearLogs();

    return this.http
      .post<TaskSpec>(`${this.apiBase}/api/execute-task`, request)
      .pipe(
        tap(spec => {
          this.currentTaskSpec.set(spec);
          this.logsQueue.set(spec.outputLogs);
        }),
        catchError(err => this.handleError(err)),
        finalize(() => this.isExecutingTask.set(false)),
      );
  }

  appendLog(message: string): void {
    const ts = new Date().toISOString();
    this.logsQueue.update(logs => [...logs, `[${ts}] ${message}`]);
  }

  clearLogs(): void {
    this.logsQueue.set([]);
  }

  // ── Document API  POST /api/generate-document ─────────────────────────────

  generateDocument(request: GenerateDocumentRequest): Observable<CorporateDocument> {
    this.isGeneratingDocument.set(true);
    this.error.set(null);
    // Fresh generation (not an in-place patch) → drop the previous document so its stale content and
    // "Goal achieved / Fact-checked" badges don't linger while the new document is being generated.
    // The patch path (existingContent set) and fixCriterion refine in place and must NOT clear.
    if (!request.existingContent) { this.currentDocument.set(null); this.documentReview.set(null); this.documentFreshness.set(null); }

    return this.http
      .post<CorporateDocument>(`${this.apiBase}/api/generate-document`, request)
      .pipe(
        tap(doc => this.currentDocument.set(doc)),
        catchError(err => this.handleError(err)),
        finalize(() => this.isGeneratingDocument.set(false)),
      );
  }

  /** Deterministic by-id fix: repair one section against a single criterion. Echoes the
   *  structured document the server returned. Replaces the current document with the result. */
  fixCriterion(structured: StructuredDocument, criterionId: string): Observable<CorporateDocument> {
    this.isGeneratingDocument.set(true);
    this.error.set(null);

    return this.http
      .post<CorporateDocument>(`${this.apiBase}/api/documents/fix`, { document: structured, criterionId })
      .pipe(
        tap(doc => this.currentDocument.set(doc)),
        catchError(err => this.handleError(err)),
        finalize(() => this.isGeneratingDocument.set(false)),
      );
  }

  // ── Prompt API  POST /api/generate-component-prompt ──────────────────────

  generateComponentPrompt(
    request: GenerateComponentPromptRequest,
  ): Observable<DeveloperPrompt> {
    this.isGeneratingPrompt.set(true);
    this.error.set(null);

    return this.http
      .post<DeveloperPrompt>(
        `${this.apiBase}/api/generate-component-prompt`,
        request,
      )
      .pipe(
        tap(prompt => this.currentPrompt.set(prompt)),
        catchError(err => this.handleError(err)),
        finalize(() => this.isGeneratingPrompt.set(false)),
      );
  }

  // ── Project Download  POST /api/generate-project ─────────────────────────

  downloadProject(request: GenerateProjectRequest): Observable<Blob> {
    return this.http
      .post(`${this.apiBase}/api/generate-project`, request, { responseType: 'blob' })
      .pipe(catchError(err => this.handleError(err)));
  }

  // ── Structured Subdomain Research ────────────────────────────────────────

  addSubdomain(domain: string, subdomain: string): void {
    const key = `${domain}|${subdomain}`;
    if (this.selectedSubdomains().some(s => `${s.domain}|${s.subdomain}` === key)) return;
    const entry: SelectedSubdomain = { domain, subdomain, hasResults: false, isStale: false };
    this.selectedSubdomains.update(list => [...list, entry]);
    if (!this._subdomainImportance.has(key))
      this._subdomainImportance.set(key, defaultImportanceForDomain(domain));
    this._persistPrefs();
  }

  removeSubdomain(domain: string, subdomain: string): void {
    const key = `${domain}|${subdomain}`;
    this.selectedSubdomains.update(list => list.filter(s => `${s.domain}|${s.subdomain}` !== key));
    this._subdomainImportance.delete(key);
    if (this.activeSubdomain()?.subdomain === subdomain
        && this.activeSubdomain()?.domain === domain) {
      const remaining = this.selectedSubdomains();
      this.activeSubdomain.set(remaining.length > 0 ? remaining[0] : null);
    }
    this._persistPrefs();
  }

  setActiveSubdomain(domain: string, subdomain: string): void {
    const entry = this.selectedSubdomains().find(
      s => s.domain === domain && s.subdomain === subdomain) ?? null;
    this.activeSubdomain.set(entry);
  }

  /** The 5-level importance per dimension (source of truth), defaulting to the domain profile. */
  getSubdomainImportance(domain: string, subdomain: string): DimensionImportance {
    return this._subdomainImportance.get(`${domain}|${subdomain}`)
        ?? defaultImportanceForDomain(domain);
  }

  setSubdomainImportance(domain: string, subdomain: string, importance: DimensionImportance): void {
    this._subdomainImportance.set(`${domain}|${subdomain}`, importance);
  }

  /** Derived numeric weights (levels → normalized, sum-100) for scoring/sorting and the API request. */
  getSubdomainWeights(domain: string, subdomain: string): DimensionWeights {
    return importanceToWeights(this.getSubdomainImportance(domain, subdomain));
  }

  analyzeSubdomain(domain: string, subdomain: string): void {
    const weights = this.getSubdomainWeights(domain, subdomain);
    const request: ResearchRequest = {
      keywords: subdomain,
      subDomain: subdomain,
      domain,
      weights,
    };
    this.isLoading.set(true);
    this.error.set(null);
    this.http.post<ResearchResponse>(`${this.apiBase}/api/research`, request)
      .pipe(
        tap(response => {
          this.currentResearchData.set(response);
          this._domainResultsCache.set(`${domain}|${subdomain}`, response);
          this.selectedSubdomains.update(list => list.map(s =>
            s.domain === domain && s.subdomain === subdomain
              ? { ...s, hasResults: true, isStale: false }
              : s));
        }),
        catchError(err => this.handleError(err)),
        finalize(() => this.isLoading.set(false)),
      ).subscribe();
  }

  // ── Domain Management ─────────────────────────────────────────────────────

  savePreferredDomains(domains: string[]): void {
    this.preferredDomains.set(domains);
    this._persistPrefs();
  }

  discoverDomains(): Observable<DomainSuggestions> {
    this.isDiscoveringDomains.set(true);
    this.error.set(null);
    return this.http
      .post<DomainSuggestions>(`${this.apiBase}/api/domains/discover`, {})
      .pipe(
        tap(r => this.discoveredDomains.set(r.domains)),
        catchError(err => this.handleError(err)),
        finalize(() => this.isDiscoveringDomains.set(false)),
      );
  }

  // ── Presentation (UI-only) ────────────────────────────────────────────────

  setSlideDeck(slides: Slide[]): void {
    this.activeSlideDeck.set(slides);
  }

  // ── Knowledge hub: artifacts, comparison, analytics, white paper, export ────

  listArtifacts(opts: { kind?: ArtifactKind; domain?: string; latestOnly?: boolean; take?: number } = {})
    : Observable<ArtifactMetadata[]> {
    let params = new HttpParams();
    if (opts.kind)   params = params.set('kind', opts.kind);
    if (opts.domain) params = params.set('domain', opts.domain);
    params = params.set('latestOnly', String(opts.latestOnly ?? true));
    if (opts.take)   params = params.set('take', String(opts.take));
    return this.http
      .get<ArtifactMetadata[]>(`${this.apiBase}/api/artifacts`, { params })
      .pipe(catchError(err => this.handleError(err)));
  }

  getArtifact(id: string): Observable<StoredArtifact> {
    return this.http
      .get<StoredArtifact>(`${this.apiBase}/api/artifacts/${id}`)
      .pipe(catchError(err => this.handleError(err)));
  }

  getArtifactVersions(lineageId: string): Observable<ArtifactMetadata[]> {
    return this.http
      .get<ArtifactMetadata[]>(`${this.apiBase}/api/artifacts/lineages/${encodeURIComponent(lineageId)}/versions`)
      .pipe(catchError(err => this.handleError(err)));
  }

  deleteArtifact(id: string): Observable<void> {
    return this.http
      .delete<void>(`${this.apiBase}/api/artifacts/${id}`)
      .pipe(catchError(err => this.handleError(err)));
  }

  compareArtifacts(artifactIds: string[]): Observable<ComparisonMatrix> {
    return this.http
      .post<ComparisonMatrix>(`${this.apiBase}/api/artifacts/compare`, { artifactIds })
      .pipe(catchError(err => this.handleError(err)));
  }

  analyticsPainPoints(domain?: string): Observable<PainPointAnalytics> {
    let params = new HttpParams();
    if (domain) params = params.set('domain', domain);
    return this.http
      .get<PainPointAnalytics>(`${this.apiBase}/api/analytics/pain-points`, { params })
      .pipe(catchError(err => this.handleError(err)));
  }

  analyticsCompetitors(domain?: string): Observable<CompetitorAnalytics> {
    let params = new HttpParams();
    if (domain) params = params.set('domain', domain);
    return this.http
      .get<CompetitorAnalytics>(`${this.apiBase}/api/analytics/competitors`, { params })
      .pipe(catchError(err => this.handleError(err)));
  }

  generateWhitePaper(request: WhitePaperRequest): Observable<WhitePaper> {
    return this.http
      .post<WhitePaper>(`${this.apiBase}/api/whitepaper`, request)
      .pipe(catchError(err => this.handleError(err)));
  }

  /**
   * Pending driven white-paper context set by an entry point (research/opportunity/assessment).
   * The White Paper view reads this on activation to prefill + auto-generate, then clears it.
   */
  readonly pendingWhitePaper = signal<WhitePaperRequest | null>(null);

  /** Launch the White Paper view for the current research run (optionally focused on one opportunity). */
  startWhitePaperFromResearch(opportunityId?: string): void {
    const domain = this.currentResearchData()?.domain;
    const sub = this.activeSubdomain()?.subdomain;
    this.listArtifacts({ kind: 'research', domain, latestOnly: true, take: 50 }).subscribe({
      next: rows => {
        // Prefer the artifact matching the active subdomain; else the most recent research run.
        const match = (sub
          ? rows.find(r => (r.subDomain ?? '').toLowerCase() === sub.toLowerCase()
                        || (r.title ?? '').toLowerCase() === sub.toLowerCase())
          : undefined) ?? rows[0];
        this.pendingWhitePaper.set(match
          ? { researchArtifactId: match.artifactId, opportunityId, groundWithFreshResearch: true }
          : null);
        this.setActiveWorkspace('whitepaper');
      },
      error: () => { this.pendingWhitePaper.set(null); this.setActiveWorkspace('whitepaper'); },
    });
  }

  /** Launch the White Paper view for the current use-case assessment. */
  startWhitePaperFromAssessment(): void {
    const a = this.currentAssessment();
    this.pendingWhitePaper.set(a ? { assessmentId: a.id, groundWithFreshResearch: true } : null);
    this.setActiveWorkspace('whitepaper');
  }

  consumePendingWhitePaper(): WhitePaperRequest | null {
    const p = this.pendingWhitePaper();
    this.pendingWhitePaper.set(null);
    return p;
  }

  /** Downloads an artifact in the requested format via the server-side exporter and saves it. */
  exportArtifact(id: string, format: ExportFormat, filename: string): Observable<void> {
    return this.http
      .get(`${this.apiBase}/api/artifacts/${id}/export`, {
        params: new HttpParams().set('format', format),
        responseType: 'blob',
      })
      .pipe(
        tap(blob => this.saveBlob(blob, filename)),
        // Return void
        catchError(err => this.handleError(err)),
      ) as unknown as Observable<void>;
  }

  /** Stateless markdown → file export (for content not saved as an artifact, e.g. assessments). */
  exportMarkdown(title: string, markdown: string, format: ExportFormat, filename: string): Observable<void> {
    return this.http
      .post(`${this.apiBase}/api/export`, { title, markdown, format }, { responseType: 'blob' })
      .pipe(
        tap(blob => this.saveBlob(blob, filename)),
        catchError(err => this.handleError(err)),
      ) as unknown as Observable<void>;
  }

  /** Triggers a browser download for a blob (matches the generate-project pattern). */
  saveBlob(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }

  // ── Private helpers ───────────────────────────────────────────────────────

  private handleError(err: unknown): Observable<never> {
    const apiError = this.isApiError(err) ? err : this.toApiError(err);
    this.error.set(apiError);
    return throwError(() => apiError);
  }

  private isApiError(err: unknown): err is ApiError {
    return (
      typeof err === 'object' &&
      err !== null &&
      'statusCode' in err &&
      'message' in err
    );
  }

  private toApiError(err: unknown): ApiError {
    if (typeof err === 'object' && err !== null && 'status' in err) {
      const httpErr = err as { status: number; message?: string };
      return {
        statusCode: httpErr.status,
        message:    httpErr.message ?? 'Request failed',
      };
    }
    return { statusCode: 0, message: 'An unexpected error occurred' };
  }
}
