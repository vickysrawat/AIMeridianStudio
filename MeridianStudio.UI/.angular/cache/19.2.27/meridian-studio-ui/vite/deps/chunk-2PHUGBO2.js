import {
  AbstractMermaidTokenBuilder,
  AbstractMermaidValueConverter,
  EmptyFileSystem,
  MermaidGeneratedSharedModule,
  PieGrammarGeneratedModule,
  __name,
  createDefaultCoreModule,
  createDefaultSharedCoreModule,
  inject
} from "./chunk-7RUMY3Q4.js";

// node_modules/@mermaid-js/parser/dist/chunks/mermaid-parser.core/chunk-YOTPTUD7.mjs
var PieTokenBuilder = class extends AbstractMermaidTokenBuilder {
  static {
    __name(this, "PieTokenBuilder");
  }
  constructor() {
    super(["pie", "showData"]);
  }
};
var PieValueConverter = class extends AbstractMermaidValueConverter {
  static {
    __name(this, "PieValueConverter");
  }
  runCustomConverter(rule, input, _cstNode) {
    if (rule.name !== "PIE_SECTION_LABEL") {
      return void 0;
    }
    return input.replace(/"/g, "").trim();
  }
};
var PieModule = {
  parser: {
    TokenBuilder: __name(() => new PieTokenBuilder(), "TokenBuilder"),
    ValueConverter: __name(() => new PieValueConverter(), "ValueConverter")
  }
};
function createPieServices(context = EmptyFileSystem) {
  const shared = inject(createDefaultSharedCoreModule(context), MermaidGeneratedSharedModule);
  const Pie = inject(createDefaultCoreModule({
    shared
  }), PieGrammarGeneratedModule, PieModule);
  shared.ServiceRegistry.register(Pie);
  return {
    shared,
    Pie
  };
}
__name(createPieServices, "createPieServices");

export {
  PieModule,
  createPieServices
};
//# sourceMappingURL=chunk-2PHUGBO2.js.map
