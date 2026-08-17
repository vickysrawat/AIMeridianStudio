import { Injectable, inject } from '@angular/core';
import { jsPDF } from 'jspdf';
import autoTable from 'jspdf-autotable';
import {
  AlignmentType,
  BorderStyle,
  Document,
  HeadingLevel,
  ImageRun,
  Packer,
  Paragraph,
  Table,
  TableCell,
  TableRow,
  TextRun,
  WidthType,
} from 'docx';

import { CorporateDocument } from '../models/interfaces';
import { ThemeService } from './theme.service';
import {
  MdBlock,
  MdInline,
  parseMarkdownBlocks,
  runsToPlain,
} from '../utils/markdown-blocks';
import {
  RasterImage,
  beginExportRender,
  renderMermaidPng,
  restoreAppMermaidTheme,
} from '../utils/mermaid-raster';

/**
 * Exports a generated document to native, selectable PDF or Word (DOCX) on a white page.
 *
 * Both formats share one pipeline: parse the Markdown into blocks, rasterize any Mermaid
 * diagrams to light PNGs, then map each block to the target format's native primitives
 * (headings, paragraphs, tables, lists, images). Diagrams are embedded as images because
 * neither format renders Mermaid natively.
 */
@Injectable({ providedIn: 'root' })
export class DocumentExportService {
  private readonly theme = inject(ThemeService);

  // ── Public API ────────────────────────────────────────────────────────────

  async downloadPdf(doc: CorporateDocument): Promise<void> {
    const { blocks, images } = await this.prepare(doc);
    const pdf = this.buildPdf(doc, blocks, images);
    pdf.save(`${this.fileBase(doc.title)}.pdf`);
  }

  async downloadDocx(doc: CorporateDocument): Promise<void> {
    const { blocks, images } = await this.prepare(doc);
    const wordDoc = this.buildDocx(doc, blocks, images);
    const blob = await Packer.toBlob(wordDoc);
    this.triggerDownload(blob, `${this.fileBase(doc.title)}.docx`);
  }

  // ── Shared preparation ─────────────────────────────────────────────────────

  /** Parse blocks and pre-render all Mermaid diagrams to PNGs (keyed by block index). */
  private async prepare(
    doc: CorporateDocument,
  ): Promise<{ blocks: MdBlock[]; images: Map<number, RasterImage | null> }> {
    const blocks = parseMarkdownBlocks(doc.content ?? '');
    const images = new Map<number, RasterImage | null>();

    const mermaidIdx = blocks
      .map((b, i) => (b.kind === 'mermaid' ? i : -1))
      .filter((i) => i >= 0);

    if (mermaidIdx.length) {
      beginExportRender();
      try {
        for (const i of mermaidIdx) {
          const block = blocks[i] as Extract<MdBlock, { kind: 'mermaid' }>;
          images.set(i, await renderMermaidPng(block.src));
        }
      } finally {
        // Always restore the on-screen (inverted) config for the current app theme.
        restoreAppMermaidTheme(this.theme.theme());
      }
    }

    return { blocks, images };
  }

  // ── PDF ─────────────────────────────────────────────────────────────────────

  private buildPdf(
    doc: CorporateDocument,
    blocks: MdBlock[],
    images: Map<number, RasterImage | null>,
  ): jsPDF {
    const pdf = new jsPDF({ unit: 'pt', format: 'a4' });
    const pageW = pdf.internal.pageSize.getWidth();
    const pageH = pdf.internal.pageSize.getHeight();
    const margin = 56;
    const contentW = pageW - margin * 2;
    let y = margin;

    const INK: [number, number, number] = [15, 23, 42]; // slate-900
    const MUTED: [number, number, number] = [100, 116, 139]; // slate-500

    const ensure = (h: number) => {
      if (y + h > pageH - margin) {
        pdf.addPage();
        y = margin;
      }
    };
    const write = (
      text: string,
      size: number,
      opts: { style?: 'normal' | 'bold' | 'italic'; font?: string; indent?: number; color?: [number, number, number]; gap?: number } = {},
    ) => {
      const font = opts.font ?? 'helvetica';
      pdf.setFont(font, opts.style ?? 'normal');
      pdf.setFontSize(size);
      pdf.setTextColor(...(opts.color ?? INK));
      const indent = opts.indent ?? 0;
      const lh = size * 1.4;
      const lines = pdf.splitTextToSize(text, contentW - indent) as string[];
      for (const ln of lines) {
        ensure(lh);
        pdf.text(ln, margin + indent, y);
        y += lh;
      }
      y += opts.gap ?? size * 0.5;
    };

    // Title + meta
    write(doc.title, 20, { style: 'bold', gap: 4 });
    const created = doc.createdAt ? new Date(doc.createdAt).toLocaleString() : '';
    write([created, doc.modelUsed].filter(Boolean).join('  ·  '), 9, { color: MUTED, gap: 10 });

    const headingSize = (lvl: number) => [20, 17, 14, 12, 11, 11][Math.min(lvl, 6) - 1];

    blocks.forEach((block, i) => {
      switch (block.kind) {
        case 'heading':
          y += 6;
          write(runsToPlain(block.runs), headingSize(block.level), { style: 'bold', gap: 3 });
          break;
        case 'paragraph':
          write(runsToPlain(block.runs), 10.5, { gap: 6 });
          break;
        case 'list':
          block.items.forEach((item, idx) => {
            const marker = block.ordered ? `${idx + 1}.` : '•';
            write(`${marker}  ${runsToPlain(item)}`, 10.5, { indent: 14, gap: 2 });
          });
          y += 4;
          break;
        case 'blockquote':
          write(runsToPlain(block.runs), 10.5, { style: 'italic', indent: 14, color: MUTED, gap: 6 });
          break;
        case 'code':
          block.text.split('\n').forEach((ln) => write(ln || ' ', 9, { font: 'courier', indent: 8, gap: 0 }));
          y += 6;
          break;
        case 'hr':
          ensure(12);
          pdf.setDrawColor(203, 213, 225);
          pdf.line(margin, y, margin + contentW, y);
          y += 12;
          break;
        case 'table':
          autoTable(pdf, {
            startY: y,
            head: [block.headers.map(runsToPlain)],
            body: block.rows.map((r) => r.map(runsToPlain)),
            margin: { left: margin, right: margin },
            styles: { fontSize: 9, cellPadding: 4, textColor: INK, lineColor: [226, 232, 240], lineWidth: 0.5 },
            headStyles: { fillColor: [241, 245, 249], textColor: INK, fontStyle: 'bold' },
            theme: 'grid',
          });
          y = ((pdf as unknown as { lastAutoTable?: { finalY: number } }).lastAutoTable?.finalY ?? y) + 12;
          break;
        case 'mermaid': {
          const img = images.get(i);
          if (img) {
            let w = img.width;
            let h = img.height;
            if (w > contentW) { h = (h * contentW) / w; w = contentW; }
            const maxH = pageH - margin * 2;
            if (h > maxH) { w = (w * maxH) / h; h = maxH; }
            ensure(h + 12);
            pdf.addImage(img.dataUrl, 'PNG', margin, y, w, h);
            y += h + 12;
          } else {
            block.src.split('\n').forEach((ln) => write(ln || ' ', 9, { font: 'courier', indent: 8, gap: 0 }));
            y += 6;
          }
          break;
        }
      }
    });

    // Footer note on the last page
    ensure(24);
    y += 8;
    write(`Generated by Meridian Studio · ${doc.modelUsed}`, 8, { color: MUTED });
    return pdf;
  }

  // ── DOCX ──────────────────────────────────────────────────────────────────

  private buildDocx(
    doc: CorporateDocument,
    blocks: MdBlock[],
    images: Map<number, RasterImage | null>,
  ): Document {
    const children: (Paragraph | Table)[] = [];

    children.push(new Paragraph({ heading: HeadingLevel.TITLE, children: [new TextRun({ text: doc.title, bold: true })] }));
    const created = doc.createdAt ? new Date(doc.createdAt).toLocaleString() : '';
    children.push(
      new Paragraph({
        spacing: { after: 240 },
        children: [new TextRun({ text: [created, doc.modelUsed].filter(Boolean).join('  ·  '), color: '64748B', size: 18 })],
      }),
    );

    const HEADINGS = [
      HeadingLevel.HEADING_1,
      HeadingLevel.HEADING_2,
      HeadingLevel.HEADING_3,
      HeadingLevel.HEADING_4,
      HeadingLevel.HEADING_5,
      HeadingLevel.HEADING_6,
    ];

    blocks.forEach((block, i) => {
      switch (block.kind) {
        case 'heading':
          children.push(new Paragraph({ heading: HEADINGS[Math.min(block.level, 6) - 1], children: this.docxRuns(block.runs) }));
          break;
        case 'paragraph':
          children.push(new Paragraph({ spacing: { after: 120 }, children: this.docxRuns(block.runs) }));
          break;
        case 'list':
          block.items.forEach((item, idx) => {
            children.push(
              new Paragraph({
                bullet: block.ordered ? undefined : { level: 0 },
                children: block.ordered
                  ? [new TextRun({ text: `${idx + 1}. ` }), ...this.docxRuns(item)]
                  : this.docxRuns(item),
                indent: block.ordered ? { left: 360 } : undefined,
              }),
            );
          });
          break;
        case 'blockquote':
          children.push(
            new Paragraph({
              indent: { left: 360 },
              border: { left: { style: BorderStyle.SINGLE, size: 12, color: 'C7D2FE', space: 8 } },
              children: this.docxRuns(block.runs, { italics: true, color: '475569' }),
            }),
          );
          break;
        case 'code':
          children.push(
            new Paragraph({
              shading: { fill: 'F1F5F9' },
              spacing: { after: 120 },
              children: block.text.split('\n').flatMap((ln, idx) => [
                ...(idx ? [new TextRun({ break: 1 })] : []),
                new TextRun({ text: ln, font: 'Courier New', size: 18 }),
              ]),
            }),
          );
          break;
        case 'hr':
          children.push(new Paragraph({ border: { bottom: { style: BorderStyle.SINGLE, size: 6, color: 'CBD5E1', space: 1 } }, children: [] }));
          break;
        case 'table':
          children.push(this.docxTable(block));
          break;
        case 'mermaid': {
          const img = images.get(i);
          if (img) {
            let w = img.width;
            let h = img.height;
            const maxW = 600;
            if (w > maxW) { h = (h * maxW) / w; w = maxW; }
            children.push(
              new Paragraph({
                alignment: AlignmentType.CENTER,
                spacing: { before: 120, after: 120 },
                children: [
                  new ImageRun({
                    type: 'png',
                    data: this.dataUrlToBytes(img.dataUrl),
                    transformation: { width: Math.round(w), height: Math.round(h) },
                  }),
                ],
              }),
            );
          } else {
            children.push(
              new Paragraph({
                shading: { fill: 'F1F5F9' },
                children: block.src.split('\n').flatMap((ln, idx) => [
                  ...(idx ? [new TextRun({ break: 1 })] : []),
                  new TextRun({ text: ln, font: 'Courier New', size: 18 }),
                ]),
              }),
            );
          }
          break;
        }
      }
    });

    children.push(
      new Paragraph({
        spacing: { before: 480 },
        alignment: AlignmentType.CENTER,
        children: [new TextRun({ text: `Generated by Meridian Studio · ${doc.modelUsed}`, color: '94A3B8', size: 16 })],
      }),
    );

    return new Document({ sections: [{ children }] });
  }

  private docxRuns(
    runs: MdInline[],
    extra: { bold?: boolean; italics?: boolean; color?: string } = {},
  ): TextRun[] {
    return runs.map(
      (r) =>
        new TextRun({
          text: r.text,
          bold: r.bold || extra.bold,
          italics: r.italic || extra.italics,
          strike: r.strike,
          font: r.code ? 'Courier New' : undefined,
          color: extra.color ?? (r.href ? '4F46E5' : undefined),
          underline: r.href ? {} : undefined,
        }),
    );
  }

  private docxTable(block: Extract<MdBlock, { kind: 'table' }>): Table {
    const headerRow = new TableRow({
      tableHeader: true,
      children: block.headers.map(
        (cell) =>
          new TableCell({
            shading: { fill: 'F1F5F9' },
            children: [new Paragraph({ children: this.docxRuns(cell, { bold: true }) })],
          }),
      ),
    });
    const bodyRows = block.rows.map(
      (row) =>
        new TableRow({
          children: row.map((cell) => new TableCell({ children: [new Paragraph({ children: this.docxRuns(cell) })] })),
        }),
    );
    return new Table({
      width: { size: 100, type: WidthType.PERCENTAGE },
      rows: [headerRow, ...bodyRows],
    });
  }

  // ── Helpers ─────────────────────────────────────────────────────────────────

  private dataUrlToBytes(dataUrl: string): Uint8Array {
    const b64 = dataUrl.split(',')[1] ?? '';
    const bin = atob(b64);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    return bytes;
  }

  private fileBase(title: string): string {
    return (title || 'document').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '') || 'document';
  }

  private triggerDownload(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }
}
