import {
  __name
} from "./chunk-7RUMY3Q4.js";
import {
  __async
} from "./chunk-7RSYZEEK.js";

// node_modules/@mermaid-js/parser/dist/mermaid-parser.core.mjs
var parsers = {};
var initializers = {
  info: __name(() => __async(null, null, function* () {
    const {
      createInfoServices: createInfoServices2
    } = yield import("./info-DKCQHKI2-LN7QMOKC.js");
    const parser = createInfoServices2().Info.parser.LangiumParser;
    parsers.info = parser;
  }), "info"),
  packet: __name(() => __async(null, null, function* () {
    const {
      createPacketServices: createPacketServices2
    } = yield import("./packet-7NZHBO7P-L3Z7M2IH.js");
    const parser = createPacketServices2().Packet.parser.LangiumParser;
    parsers.packet = parser;
  }), "packet"),
  pie: __name(() => __async(null, null, function* () {
    const {
      createPieServices: createPieServices2
    } = yield import("./pie-RZYD4A2V-RXUZHSHE.js");
    const parser = createPieServices2().Pie.parser.LangiumParser;
    parsers.pie = parser;
  }), "pie"),
  treeView: __name(() => __async(null, null, function* () {
    const {
      createTreeViewServices: createTreeViewServices2
    } = yield import("./treeView-QDETBFTQ-TIOLQ74N.js");
    const parser = createTreeViewServices2().TreeView.parser.LangiumParser;
    parsers.treeView = parser;
  }), "treeView"),
  architecture: __name(() => __async(null, null, function* () {
    const {
      createArchitectureServices: createArchitectureServices2
    } = yield import("./architecture-TIHT7OUA-YNIW7AXF.js");
    const parser = createArchitectureServices2().Architecture.parser.LangiumParser;
    parsers.architecture = parser;
  }), "architecture"),
  gitGraph: __name(() => __async(null, null, function* () {
    const {
      createGitGraphServices: createGitGraphServices2
    } = yield import("./gitGraph-TEB2WS4Q-XQYF4DHL.js");
    const parser = createGitGraphServices2().GitGraph.parser.LangiumParser;
    parsers.gitGraph = parser;
  }), "gitGraph"),
  eventmodeling: __name(() => __async(null, null, function* () {
    const {
      createEventModelingServices: createEventModelingServices2
    } = yield import("./eventmodeling-45OFAUF4-JZUK367D.js");
    const parser = createEventModelingServices2().EventModel.parser.LangiumParser;
    parsers.eventmodeling = parser;
  }), "eventmodeling"),
  radar: __name(() => __async(null, null, function* () {
    const {
      createRadarServices: createRadarServices2
    } = yield import("./radar-I7S5WNFK-RNBLIEEY.js");
    const parser = createRadarServices2().Radar.parser.LangiumParser;
    parsers.radar = parser;
  }), "radar"),
  railroad: __name(() => __async(null, null, function* () {
    const {
      createRailroadServices: createRailroadServices2
    } = yield import("./railroad-3IZDKUUU-WEUAIG7J.js");
    const parser = createRailroadServices2().Railroad.parser.LangiumParser;
    parsers.railroad = parser;
  }), "railroad"),
  railroadEbnf: __name(() => __async(null, null, function* () {
    const {
      createRailroadEbnfServices: createRailroadEbnfServices2
    } = yield import("./railroad-ebnf-EBAXGLYW-JFJV7SH7.js");
    const parser = createRailroadEbnfServices2().RailroadEbnf.parser.LangiumParser;
    parsers.railroadEbnf = parser;
  }), "railroadEbnf"),
  railroadAbnf: __name(() => __async(null, null, function* () {
    const {
      createRailroadAbnfServices: createRailroadAbnfServices2
    } = yield import("./railroad-abnf-AHOZXSZD-QYXA7N5W.js");
    const parser = createRailroadAbnfServices2().RailroadAbnf.parser.LangiumParser;
    parsers.railroadAbnf = parser;
  }), "railroadAbnf"),
  railroadPeg: __name(() => __async(null, null, function* () {
    const {
      createRailroadPegServices: createRailroadPegServices2
    } = yield import("./railroad-peg-LSFZ7HO6-QORTFNXR.js");
    const parser = createRailroadPegServices2().RailroadPeg.parser.LangiumParser;
    parsers.railroadPeg = parser;
  }), "railroadPeg"),
  treemap: __name(() => __async(null, null, function* () {
    const {
      createTreemapServices: createTreemapServices2
    } = yield import("./treemap-6X3UGDF4-53MPR47J.js");
    const parser = createTreemapServices2().Treemap.parser.LangiumParser;
    parsers.treemap = parser;
  }), "treemap"),
  wardley: __name(() => __async(null, null, function* () {
    const {
      createWardleyServices: createWardleyServices2
    } = yield import("./wardley-OPB4EBWU-IKBCBNAK.js");
    const parser = createWardleyServices2().Wardley.parser.LangiumParser;
    parsers.wardley = parser;
  }), "wardley"),
  cynefin: __name(() => __async(null, null, function* () {
    const {
      createCynefinServices: createCynefinServices2
    } = yield import("./cynefin-VYW2F7L2-CA777RYK.js");
    const parser = createCynefinServices2().Cynefin.parser.LangiumParser;
    parsers.cynefin = parser;
  }), "cynefin")
};
function parse(diagramType, text) {
  return __async(this, null, function* () {
    const initializer = initializers[diagramType];
    if (!initializer) {
      throw new Error(`Unknown diagram type: ${diagramType}`);
    }
    if (!parsers[diagramType]) {
      yield initializer();
    }
    const parser = parsers[diagramType];
    const result = parser.parse(text);
    if (result.lexerErrors.length > 0 || result.parserErrors.length > 0) {
      throw new MermaidParseError(result);
    }
    return result.value;
  });
}
__name(parse, "parse");
var MermaidParseError = class extends Error {
  constructor(result) {
    const lexerErrors = result.lexerErrors.map((err) => {
      const line = err.line !== void 0 && !isNaN(err.line) ? err.line : "?";
      const column = err.column !== void 0 && !isNaN(err.column) ? err.column : "?";
      return `Lexer error on line ${line}, column ${column}: ${err.message}`;
    }).join("\n");
    const parserErrors = result.parserErrors.map((err) => {
      const line = err.token.startLine !== void 0 && !isNaN(err.token.startLine) ? err.token.startLine : "?";
      const column = err.token.startColumn !== void 0 && !isNaN(err.token.startColumn) ? err.token.startColumn : "?";
      return `Parse error on line ${line}, column ${column}: ${err.message}`;
    }).join("\n");
    super(`Parsing failed: ${lexerErrors} ${parserErrors}`);
    this.result = result;
  }
  static {
    __name(this, "MermaidParseError");
  }
};

export {
  parse,
  MermaidParseError
};
//# sourceMappingURL=chunk-F3TYDIAJ.js.map
