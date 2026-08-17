import {
  AbstractMermaidTokenBuilder,
  CommonValueConverter,
  EmptyFileSystem,
  InfoGrammarGeneratedModule,
  MermaidGeneratedSharedModule,
  __name,
  createDefaultCoreModule,
  createDefaultSharedCoreModule,
  inject
} from "./chunk-7RUMY3Q4.js";

// node_modules/@mermaid-js/parser/dist/chunks/mermaid-parser.core/chunk-BIQX33UG.mjs
var InfoTokenBuilder = class extends AbstractMermaidTokenBuilder {
  static {
    __name(this, "InfoTokenBuilder");
  }
  constructor() {
    super(["info", "showInfo"]);
  }
};
var InfoModule = {
  parser: {
    TokenBuilder: __name(() => new InfoTokenBuilder(), "TokenBuilder"),
    ValueConverter: __name(() => new CommonValueConverter(), "ValueConverter")
  }
};
function createInfoServices(context = EmptyFileSystem) {
  const shared = inject(createDefaultSharedCoreModule(context), MermaidGeneratedSharedModule);
  const Info = inject(createDefaultCoreModule({
    shared
  }), InfoGrammarGeneratedModule, InfoModule);
  shared.ServiceRegistry.register(Info);
  return {
    shared,
    Info
  };
}
__name(createInfoServices, "createInfoServices");

export {
  InfoModule,
  createInfoServices
};
//# sourceMappingURL=chunk-WURXKM65.js.map
