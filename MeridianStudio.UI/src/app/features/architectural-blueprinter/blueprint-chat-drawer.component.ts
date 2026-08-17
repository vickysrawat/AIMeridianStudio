import { Component, effect, inject, input, output, signal, ElementRef, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LucideAngularModule } from 'lucide-angular';
import { MarkdownPipe } from '../../core/pipes/markdown.pipe';
import { MermaidDirective } from '../../core/directives/mermaid.directive';
import { API_BASE_URL } from '../../core/tokens/api-base-url.token';

interface ChatMsg {
  role: 'user' | 'assistant';
  text: string;
  streaming: boolean;
}

@Component({
  selector: 'app-blueprint-chat-drawer',
  standalone: true,
  imports: [CommonModule, LucideAngularModule, MarkdownPipe, MermaidDirective],
  template: `
    <!-- Drawer — no backdrop so blueprint panels stay readable as context -->
    <div class="fixed inset-y-0 right-0 z-50 flex flex-col
                border-l border-gray-700/40 bg-gray-950 shadow-[-8px_0_32px_rgba(0,0,0,0.6)]"
         [style.width.px]="drawerWidth()">

      <!-- Resize handle — drag left edge to widen / narrow -->
      <div class="group/resize absolute inset-y-0 left-0 z-10 w-2 cursor-col-resize"
           (mousedown)="onResizeStart($event)">
        <div class="absolute inset-y-0 left-0.5 w-px bg-gray-700/40
                    transition-all group-hover/resize:w-0.5 group-hover/resize:bg-violet-500/60"></div>
      </div>

      <!-- Header -->
      <div class="flex shrink-0 items-center justify-between border-b border-gray-800
                  bg-gray-900 px-4 py-2.5">
        <div class="flex items-center gap-2">
          <lucide-icon name="message-circle" [size]="12" class="text-violet-400" />
          <span class="text-[11px] font-semibold text-white">Architect Chat</span>
          <span class="text-[10px] text-gray-600">·</span>
          <span class="text-[10px] text-gray-500">{{ sectionLabel() }}</span>
        </div>
        <button (click)="closed.emit()"
          class="rounded p-1 text-gray-600 transition-colors hover:text-gray-400">
          <lucide-icon name="x" [size]="13" />
        </button>
      </div>

      <!-- Messages -->
      <div #scrollArea class="flex-1 overflow-y-auto bg-gray-950 px-3 py-3 space-y-2.5">
        @if (messages().length === 1) {
          <p class="text-[9px] text-gray-700 italic px-1">
            Enter to send · Shift+Enter for new line · confirm a change to see "Apply"
          </p>
        }
        @for (msg of messages(); track $index) {
          <div class="flex gap-2" [class.flex-row-reverse]="msg.role === 'user'">
            <!-- Avatar -->
            <div class="flex h-5 w-5 shrink-0 items-center justify-center rounded-full text-[9px] font-medium"
                 [class]="msg.role === 'user'
                   ? 'bg-indigo-500/20 text-indigo-400'
                   : 'bg-violet-500/20 text-violet-400'">
              {{ msg.role === 'user' ? 'U' : 'AI' }}
            </div>
            <!-- Bubble -->
            <div class="flex-1 min-w-0 rounded-xl px-2.5 py-1.5 text-[9px] leading-snug"
                 [class]="msg.role === 'user'
                   ? 'rounded-tr-sm bg-indigo-500/15 text-gray-200'
                   : 'rounded-tl-sm bg-gray-800/80 text-gray-300'">
              @if (msg.streaming) {
                <div class="md-content [&_*]:!text-[9px] [&_p]:mb-0.5 [&_ul]:pl-3 [&_li]:mb-0"
                     [innerHTML]="stripApplyTag(msg.text) | markdown" appMermaid></div>
                <span class="inline-block h-2.5 w-0.5 bg-violet-400 align-middle animate-pulse ml-0.5"></span>
              } @else {
                <div class="md-content [&_*]:!text-[9px] [&_p]:mb-0.5 [&_ul]:pl-3 [&_li]:mb-0"
                     [innerHTML]="stripApplyTag(msg.text) | markdown" appMermaid></div>
              }
            </div>
          </div>
        }
      </div>

      <!-- Apply suggestion banner -->
      @if (pendingApply()) {
        <div class="shrink-0 border-t border-amber-500/20 bg-amber-500/5 px-3 py-2">
          <div class="flex items-center justify-between gap-2">
            <div class="flex items-center gap-1.5">
              <lucide-icon name="sparkles" [size]="11" class="text-amber-400" />
              <span class="text-[9px] font-medium text-amber-300">Not applied yet — click Apply to update</span>
            </div>
            <div class="flex items-center gap-2">
              <button (click)="dismissApply()"
                class="text-[9px] text-gray-600 transition-colors hover:text-gray-400">
                Dismiss
              </button>
              <button (click)="emitApply()"
                class="flex items-center gap-1 rounded border border-amber-500/30
                       bg-amber-500/15 px-2 py-1 text-[9px] font-medium text-amber-300
                       transition-colors hover:bg-amber-500/25">
                <lucide-icon name="check-check" [size]="10" />
                Apply
              </button>
            </div>
          </div>
        </div>
      }

      <!-- Input -->
      <div class="shrink-0 border-t border-gray-800 bg-gray-900 px-3 py-2">
        <div class="flex items-end gap-1.5">
          <textarea
            #inputEl
            [value]="inputText()"
            (input)="inputText.set($any($event.target).value)"
            (keydown.enter)="onEnter($any($event))"
            rows="1"
            placeholder="Ask about this section…"
            [disabled]="isStreaming()"
            class="flex-1 resize-none rounded-lg border border-gray-700/60 bg-gray-800/80
                   px-2.5 py-1.5 text-[10px] text-gray-200 placeholder-gray-600
                   focus:border-violet-500/40 focus:outline-none
                   disabled:cursor-not-allowed disabled:opacity-50"></textarea>
          <button
            (click)="sendMessage()"
            [disabled]="!inputText().trim() || isStreaming()"
            class="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg
                   bg-violet-500/20 text-violet-400 transition-colors
                   hover:bg-violet-500/30 disabled:cursor-not-allowed disabled:opacity-40">
            @if (isStreaming()) {
              <lucide-icon name="loader-2" [size]="12" class="animate-spin" />
            } @else {
              <lucide-icon name="send" [size]="12" />
            }
          </button>
        </div>
      </div>
    </div>
  `,
})
export class BlueprintChatDrawerComponent {
  private readonly apiBase = inject(API_BASE_URL);

  readonly blueprintId  = input.required<string>();
  /** API segment to chat against: 'blueprint' (default) or 'assessment'. */
  readonly basePath     = input<string>('blueprint');
  readonly sectionKey   = input.required<string>();
  readonly sectionLabel = input.required<string>();
  readonly sectionData  = input<unknown>(null);

  readonly applyPatch = output<{ sectionKey: string; patch: Record<string, unknown> }>();
  readonly closed     = output<void>();

  protected readonly messages     = signal<ChatMsg[]>([]);
  protected readonly pendingApply = signal<Record<string, unknown> | null>(null);
  protected readonly inputText    = signal('');
  protected readonly isStreaming  = signal(false);
  protected readonly drawerWidth  = signal(380);

  private _isDragging   = false;
  private _dragStartX   = 0;
  private _dragStartW   = 0;
  private _onMove!: (e: MouseEvent) => void;
  private _onUp!:   ()              => void;

  private readonly scrollArea = viewChild<ElementRef<HTMLDivElement>>('scrollArea');
  private readonly inputEl    = viewChild<ElementRef<HTMLTextAreaElement>>('inputEl');

  constructor() {
    // Inject a context summary as the first assistant message whenever the section changes
    effect(() => {
      const key   = this.sectionKey();
      const data  = this.sectionData();
      const label = this.sectionLabel();
      if (key) {
        this.messages.set([{
          role: 'assistant',
          text: this.buildSectionSummary(key, label, data),
          streaming: false,
        }]);
        this.pendingApply.set(null);
      }
    });
  }

  protected onResizeStart(event: MouseEvent): void {
    this._isDragging  = true;
    this._dragStartX  = event.clientX;
    this._dragStartW  = this.drawerWidth();
    event.preventDefault();

    this._onMove = (e: MouseEvent) => {
      if (!this._isDragging) return;
      const delta   = this._dragStartX - e.clientX;   // drag left = wider
      const clamped = Math.min(700, Math.max(280, this._dragStartW + delta));
      this.drawerWidth.set(clamped);
    };

    this._onUp = () => {
      this._isDragging = false;
      document.removeEventListener('mousemove', this._onMove);
      document.removeEventListener('mouseup',   this._onUp);
    };

    document.addEventListener('mousemove', this._onMove);
    document.addEventListener('mouseup',   this._onUp);
  }

  private buildSectionSummary(key: string, label: string, data: unknown): string {
    switch (key) {
      case 'arch-decisions': {
        const decisions = data as { decision: string; chosenApproach: string }[] | null;
        if (!decisions?.length) return `I'm ready to help with **${label}**. No decisions have been recorded yet — ask me to generate some based on your client's context.`;
        const topics = decisions.map(d => `**${d.decision}** → ${d.chosenApproach}`).join('\n- ');
        return `I can see **${decisions.length} architecture decisions** in this section:\n\n- ${topics}\n\nTell me if any decision needs updating, or ask me to add a new one based on your client's actual technology landscape.`;
      }
      case 'qa-scorecard': {
        const attrs = data as { attribute: string; target: string }[] | null;
        if (!attrs?.length) return `I'm ready to help with **${label}**. No quality targets recorded yet.`;
        const lines = attrs.map(q => `**${q.attribute}**: ${q.target}`).join(' · ');
        return `I can see **${attrs.length} quality targets**: ${lines}.\n\nLet me know if any target needs adjusting to match your client's actual SLA, compliance regime, or capacity constraints.`;
      }
      case 'tech-radar': {
        const layers = data as { layer: string; technologies: string[] }[] | null;
        if (!layers?.length) return `I'm ready to help with **${label}**. No tech stack recorded yet.`;
        const lines = layers.map(l => `**${l.layer}**: ${l.technologies.join(', ')}`).join('\n- ');
        return `I can see the tech stack across **${layers.length} layers**:\n\n- ${lines}\n\nTell me if your client uses different technologies or has constraints (licensing, existing infrastructure, team expertise) I should factor in.`;
      }
      case 'core-scenario':
        return `I can see the **Core Scenario** narrative for this blueprint. Ask me to:\n- Refine the actor flow or system overview\n- Adjust non-functional targets (SLA, latency, compliance)\n- Reframe it for a specific audience or client context`;
      case 'solution-profile': {
        const profile = data as { solutionType?: string; domain?: string } | null;
        const type = profile?.solutionType ?? 'unknown';
        return `The solution is currently classified as **${type}**. If you can share more about the actual architecture — for example the hosting model, primary interaction pattern, or deployment target — I can update the classification and confidence to better reflect your client's context.`;
      }
      case 'implementation':
        return `I can see the **Implementation Detail** — system topology, database schemas, and API endpoint manifest. Ask me to:\n- Adjust the topology to match your client's infrastructure\n- Modify database tables or add new entities\n- Update or add API endpoints`;
      case 'feasibility': {
        const fa = data as { summary?: string; options?: { name: string; verdict: string }[] } | null;
        if (!fa?.options?.length) return `The **Feasibility Analysis** is empty. Describe what you want to evaluate and I'll help you compare the options.`;
        const summary = fa.options.map(o => `**${o.name}**: ${o.verdict}`).join(' · ');
        return `I can see the **Feasibility Analysis** with ${fa.options.length} options: ${summary}.\n\nAsk me to:\n- Change a verdict or score\n- Add or remove challenges/roadblocks for any option\n- Add a new option to compare\n- Update the overall summary\n\nWhen you confirm a change, I'll show **Apply suggested changes** to pre-fill the edit form.`;
      }
      case 'buy-vs-build': {
        const opts = data as { component: string; recommendation: string }[] | null;
        if (!opts?.length) return `The **Buy vs Build** panel is empty. Tell me about the components you're evaluating and I'll help assess whether to buy a product or build custom for each one.`;
        const buys   = opts.filter(o => o.recommendation === 'Buy').map(o => o.component).join(', ');
        const builds = opts.filter(o => o.recommendation === 'Build').map(o => o.component).join(', ');
        const hybrid = opts.filter(o => o.recommendation === 'Hybrid').map(o => o.component).join(', ');
        const lines  = [buys && `**Buy**: ${buys}`, builds && `**Build**: ${builds}`, hybrid && `**Hybrid**: ${hybrid}`].filter(Boolean).join(' · ');
        return `I can see **${opts.length} buy vs build decisions** (${lines}).\n\nAsk me to revisit any decision — for example if your client has existing licenses, a strong engineering team, or a vendor preference that changes the calculus.`;
      }
      case 'project-context': {
        const notes = data as string | null;
        if (!notes?.trim()) return `The **Project Context** is empty. Tell me about your client's environment and I'll help you draft it — existing technology, compliance requirements, team size, budget constraints, or anything else the AI should know.`;
        return `I can see the **Project Context** notes:\n\n> ${notes.slice(0, 300)}${notes.length > 300 ? '…' : ''}\n\nAsk me to refine the language, add missing sections, or rewrite any part. Once you confirm, I'll update the notes so all future documents and AI suggestions reflect this context.`;
      }
      default:
        return `I have the current **${label}** content as context. What would you like to discuss or update?`;
    }
  }

  protected stripApplyTag(text: string): string {
    return text.replace(/<apply>[\s\S]*?<\/apply>/g, '').trim();
  }

  protected onEnter(event: KeyboardEvent): void {
    if (!event.shiftKey) {
      event.preventDefault();
      this.sendMessage();
    }
  }

  protected sendMessage(): void {
    const text = this.inputText().trim();
    if (!text || this.isStreaming()) return;

    // Skip the auto-generated summary (first assistant message) — it's display-only context
    const history = this.messages()
      .slice(1)
      .map(m => ({ role: m.role, content: m.text }));
    this.messages.update(msgs => [...msgs, { role: 'user', text, streaming: false }]);
    this.messages.update(msgs => [...msgs, { role: 'assistant', text: '', streaming: true }]);
    this.inputText.set('');
    this.inputEl()?.nativeElement.focus();
    this.isStreaming.set(true);
    this.pendingApply.set(null);
    this.scrollToBottom();

    const body = JSON.stringify({
      sectionKey: this.sectionKey(),
      messages: [...history, { role: 'user', content: text }],
    });

    fetch(`${this.apiBase}/api/${this.basePath()}/${this.blueprintId()}/chat`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
      body,
    }).then(res => {
      if (!res.ok || !res.body) { this.isStreaming.set(false); return; }

      const reader  = res.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';
      let currentEvent = '';

      const pump = (): Promise<void> =>
        reader.read().then(({ done, value }) => {
          if (done) {
            this.finaliseLastMessage();
            this.isStreaming.set(false);
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
                  const chunk = JSON.parse(raw) as string;
                  this.appendToLastMessage(chunk);
                  this.scrollToBottom();
                } else if (currentEvent === 'apply') {
                  const patch = JSON.parse(raw) as { sectionKey: string; patch: Record<string, unknown> };
                  this.pendingApply.set(patch.patch ?? patch);
                } else if (currentEvent === 'done') {
                  this.finaliseLastMessage();
                  this.isStreaming.set(false);
                } else if (currentEvent === 'error') {
                  this.appendToLastMessage(`\n\n⚠️ ${raw}`);
                  this.isStreaming.set(false);
                }
              } catch { /* malformed SSE — skip */ }
            }
          }

          return pump();
        }).catch(() => {
          this.isStreaming.set(false);
        });

      pump();
    }).catch(() => this.isStreaming.set(false));
  }

  protected emitApply(): void {
    const patch = this.pendingApply();
    if (!patch) return;
    this.applyPatch.emit({ sectionKey: this.sectionKey(), patch });
    this.pendingApply.set(null);
  }

  protected dismissApply(): void {
    this.pendingApply.set(null);
  }

  private appendToLastMessage(chunk: string): void {
    this.messages.update(msgs => {
      if (msgs.length === 0) return msgs;
      const copy = [...msgs];
      copy[copy.length - 1] = { ...copy[copy.length - 1], text: copy[copy.length - 1].text + chunk };
      return copy;
    });
  }

  private finaliseLastMessage(): void {
    this.messages.update(msgs => {
      if (msgs.length === 0) return msgs;
      const copy = [...msgs];
      copy[copy.length - 1] = { ...copy[copy.length - 1], streaming: false };
      return copy;
    });
  }

  private scrollToBottom(): void {
    const el = this.scrollArea()?.nativeElement;
    if (el) el.scrollTop = el.scrollHeight;
  }
}
