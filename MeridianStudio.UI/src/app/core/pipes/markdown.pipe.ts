import { Pipe, PipeTransform, inject } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';

/**
 * Custom Markdown-to-SafeHtml pipe.
 * Handles: headings, bold/italic/strikethrough, code fences, inline code,
 * blockquotes, ordered/unordered lists, tables, horizontal rules, and links.
 * Uses DomSanitizer.bypassSecurityTrustHtml — input must come from trusted
 * backend sources only.
 */
@Pipe({ name: 'markdown', standalone: true, pure: true })
export class MarkdownPipe implements PipeTransform {
  private readonly san = inject(DomSanitizer);

  transform(raw: string | null | undefined): SafeHtml {
    return this.san.bypassSecurityTrustHtml(raw ? this.render(raw) : '');
  }

  private render(md: string): string {
    // Normalise line endings before splitting so the pipe works regardless of
    // whether the content has actual newlines or literal \n escape sequences
    // (some LLMs double-escape their output for long documents like Detailed Design).
    const normalised = md
      .replace(/\\n/g, '\n')  // literal backslash-n  → newline
      .replace(/\r\n/g, '\n') // Windows CRLF         → newline
      .replace(/\r/g, '\n');  // old-Mac CR           → newline
    const lines = normalised.split('\n');
    const out: string[] = [];

    let inCode = false;
    let codeLang = '';
    const codeAccum: string[] = [];

    let inList = false;
    let listIsOl = false;

    let inTable = false;
    let tableHasHeader = false;

    const flushList = (): void => {
      if (inList) {
        out.push(listIsOl ? '</ol>' : '</ul>');
        inList = false;
      }
    };

    const flushTable = (): void => {
      if (inTable) {
        out.push('</tbody></table>');
        inTable = false;
        tableHasHeader = false;
      }
    };

    for (let i = 0; i < lines.length; i++) {
      let ln = lines[i];

      // ── Code fence ──────────────────────────────────────────────
      if (ln.startsWith('```')) {
        if (inCode) {
          if (codeLang === 'mermaid') {
            // Emit a placeholder — MermaidDirective will replace this with SVG.
            const encoded = encodeURIComponent(codeAccum.join('\n'));
            out.push(`<div class="mermaid-pending" data-src="${encoded}"></div>`);
          } else {
            out.push(
              `<pre class="md-pre" data-lang="${codeLang}">` +
              `<code class="md-code">${codeAccum.join('\n')}</code></pre>`,
            );
          }
          inCode = false;
          codeAccum.length = 0;
          codeLang = '';
        } else {
          flushList();
          flushTable();
          inCode = true;
          codeLang = ln.slice(3).trim().toLowerCase();
        }
        continue;
      }

      if (inCode) {
        // Mermaid diagrams need raw text (no HTML escaping) so the directive
        // can pass the original source to mermaid.render().
        codeAccum.push(codeLang === 'mermaid' ? ln : this.esc(ln));
        continue;
      }

      // Strip trailing hard-break backslashes the model emits at line ends (CommonMark's
      // "\ at EOL = <br>"). This simple renderer would otherwise print them literally, so a
      // stray "\" appears on almost every line. Only OUTSIDE code fences (handled above), so
      // legitimate backslashes in code/mermaid are preserved.
      ln = ln.replace(/\\+[ \t]*$/, '');

      // ── Blank line ──────────────────────────────────────────────
      if (!ln.trim()) {
        flushList();
        flushTable();
        continue;
      }

      // ── Table ───────────────────────────────────────────────────
      if (ln.startsWith('|')) {
        flushList();
        const isSeparator = /^\|[\s|:=-]+\|$/.test(ln);
        if (isSeparator) {
          // This row immediately follows a header row — switch thead/tbody
          if (inTable && !tableHasHeader) {
            // Retag last <tr> as thead
            const lastTr = out.pop();
            if (lastTr) {
              out.push(
                '<thead>' + lastTr.replace(/<td /g, '<th ').replace(/<\/td>/g, '</th>') + '</thead><tbody>',
              );
              tableHasHeader = true;
            }
          }
          continue;
        }

        if (!inTable) {
          out.push('<table class="md-table">');
          inTable = true;
          tableHasHeader = false;
        }

        const cells = ln
          .split('|')
          .slice(1, -1)
          .map(c => `<td class="md-td">${this.inline(c.trim())}</td>`)
          .join('');
        out.push(`<tr class="md-tr">${cells}</tr>`);

        if (!lines[i + 1]?.startsWith('|')) flushTable();
        continue;
      }
      flushTable();

      // ── Heading ─────────────────────────────────────────────────
      const hm = ln.match(/^(#{1,6})\s+(.+)$/);
      if (hm) {
        flushList();
        const lvl = hm[1].length;
        out.push(`<h${lvl} class="md-h${lvl}">${this.inline(hm[2])}</h${lvl}>`);
        continue;
      }

      // ── Horizontal rule ─────────────────────────────────────────
      if (/^[-*_]{3,}$/.test(ln.trim())) {
        flushList();
        out.push('<hr class="md-hr">');
        continue;
      }

      // ── Blockquote ──────────────────────────────────────────────
      const bq = ln.match(/^>\s?(.*)/);
      if (bq) {
        flushList();
        out.push(`<blockquote class="md-bq">${this.inline(bq[1])}</blockquote>`);
        continue;
      }

      // ── Unordered list ──────────────────────────────────────────
      const ul = ln.match(/^[-*+]\s+(.+)/);
      if (ul) {
        if (!inList || listIsOl) {
          flushList();
          out.push('<ul class="md-ul">');
          inList = true;
          listIsOl = false;
        }
        out.push(`<li class="md-li">${this.inline(ul[1])}</li>`);
        continue;
      }

      // ── Ordered list ────────────────────────────────────────────
      const ol = ln.match(/^\d+[.)]\s+(.+)/);
      if (ol) {
        if (!inList || !listIsOl) {
          flushList();
          out.push('<ol class="md-ol">');
          inList = true;
          listIsOl = true;
        }
        out.push(`<li class="md-li">${this.inline(ol[1])}</li>`);
        continue;
      }

      // ── Paragraph ───────────────────────────────────────────────
      flushList();
      out.push(`<p class="md-p">${this.inline(ln)}</p>`);
    }

    // Flush any open containers
    flushList();
    flushTable();
    if (inCode) {
      if (codeLang === 'mermaid') {
        const encoded = encodeURIComponent(codeAccum.join('\n'));
        out.push(`<div class="mermaid-pending" data-src="${encoded}"></div>`);
      } else {
        out.push(`<pre class="md-pre"><code class="md-code">${codeAccum.join('\n')}</code></pre>`);
      }
    }

    return out.join('\n');
  }

  private inline(s: string): string {
    return s
      .replace(/\*\*\*(.+?)\*\*\*/g, '<strong><em>$1</em></strong>')
      .replace(/\*\*(.+?)\*\*/g, '<strong class="md-strong">$1</strong>')
      .replace(/__(.+?)__/g, '<strong class="md-strong">$1</strong>')
      .replace(/~~(.+?)~~/g, '<del class="md-del">$1</del>')
      .replace(/\*(.+?)\*/g, '<em class="md-em">$1</em>')
      .replace(/_([^_]+)_/g, '<em class="md-em">$1</em>')
      .replace(/`([^`]+)`/g, '<code class="md-ic">$1</code>')
      .replace(
        /\[([^\]]+)\]\(([^)]+)\)/g,
        '<a class="md-a" href="$2" target="_blank" rel="noopener noreferrer">$1</a>',
      )
      .replace(
        /(\[REQUIRED:[^\]]*\])/g,
        '<mark class="md-required">$1</mark>',
      );
  }

  private esc(s: string): string {
    return s
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }
}
