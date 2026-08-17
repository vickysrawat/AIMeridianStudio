import {
  AbstractMermaidTokenBuilder,
  CommonValueConverter,
  EmptyFileSystem,
  MermaidGeneratedSharedModule,
  RadarGrammarGeneratedModule,
  __name,
  createDefaultCoreModule,
  createDefaultSharedCoreModule,
  inject
} from "./chunk-7RUMY3Q4.js";

// node_modules/@mermaid-js/parser/dist/chunks/mermaid-parser.core/chunk-QBLGF6JB.mjs
var RadarTokenBuilder = class extends AbstractMermaidTokenBuilder {
  static {
    __name(this, "RadarTokenBuilder");
  }
  constructor() {
    super(["radar-beta"]);
  }
};
var RadarModule = {
  parser: {
    TokenBuilder: __name(() => new RadarTokenBuilder(), "TokenBuilder"),
    ValueConverter: __name(() => new CommonValueConverter(), "ValueConverter")
  }
};
function createRadarServices(context = EmptyFileSystem) {
  const shared = inject(createDefaultSharedCoreModule(context), MermaidGeneratedSharedModule);
  const Radar = inject(createDefaultCoreModule({
    shared
  }), RadarGrammarGeneratedModule, RadarModule);
  shared.ServiceRegistry.register(Radar);
  return {
    shared,
    Radar
  };
}
__name(createRadarServices, "createRadarServices");

export {
  RadarModule,
  createRadarServices
};
//# sourceMappingURL=chunk-S6MKQXIC.js.map
