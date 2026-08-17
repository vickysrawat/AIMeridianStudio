import {
  AbstractMermaidTokenBuilder,
  CommonValueConverter,
  EmptyFileSystem,
  GitGraphGrammarGeneratedModule,
  MermaidGeneratedSharedModule,
  __name,
  createDefaultCoreModule,
  createDefaultSharedCoreModule,
  inject
} from "./chunk-7RUMY3Q4.js";

// node_modules/@mermaid-js/parser/dist/chunks/mermaid-parser.core/chunk-CYSBUYHQ.mjs
var GitGraphTokenBuilder = class extends AbstractMermaidTokenBuilder {
  static {
    __name(this, "GitGraphTokenBuilder");
  }
  constructor() {
    super(["gitGraph"]);
  }
};
var GitGraphModule = {
  parser: {
    TokenBuilder: __name(() => new GitGraphTokenBuilder(), "TokenBuilder"),
    ValueConverter: __name(() => new CommonValueConverter(), "ValueConverter")
  }
};
function createGitGraphServices(context = EmptyFileSystem) {
  const shared = inject(createDefaultSharedCoreModule(context), MermaidGeneratedSharedModule);
  const GitGraph = inject(createDefaultCoreModule({
    shared
  }), GitGraphGrammarGeneratedModule, GitGraphModule);
  shared.ServiceRegistry.register(GitGraph);
  return {
    shared,
    GitGraph
  };
}
__name(createGitGraphServices, "createGitGraphServices");

export {
  GitGraphModule,
  createGitGraphServices
};
//# sourceMappingURL=chunk-PPGRPG47.js.map
