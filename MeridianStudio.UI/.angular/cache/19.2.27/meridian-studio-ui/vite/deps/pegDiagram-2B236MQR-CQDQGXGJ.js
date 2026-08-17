import {
  db,
  getStyles,
  renderer
} from "./chunk-EL3K7FJE.js";
import {
  populateCommonDb
} from "./chunk-7HYPFY75.js";
import {
  MermaidParseError
} from "./chunk-F3TYDIAJ.js";
import "./chunk-EDQBFHJY.js";
import "./chunk-5U4VC5HW.js";
import "./chunk-S6MKQXIC.js";
import "./chunk-CY5MTJ6W.js";
import "./chunk-CKGEODJK.js";
import "./chunk-R6DAIY2W.js";
import {
  createRailroadPegServices
} from "./chunk-A6KMPXEF.js";
import "./chunk-YPGRHQHW.js";
import "./chunk-ABSTUPLW.js";
import "./chunk-WURXKM65.js";
import "./chunk-N3HX5AVH.js";
import "./chunk-2PHUGBO2.js";
import "./chunk-KVFJJQKX.js";
import "./chunk-Q4KO32EG.js";
import "./chunk-PPGRPG47.js";
import "./chunk-7RUMY3Q4.js";
import "./chunk-E7QZZSDJ.js";
import "./chunk-GMG6TM6O.js";
import "./chunk-BXI6AT5O.js";
import {
  log
} from "./chunk-J73RWVFM.js";
import {
  __name
} from "./chunk-R5274TMJ.js";
import "./chunk-7RSYZEEK.js";

// node_modules/mermaid/dist/chunks/mermaid.core/pegDiagram-2B236MQR.mjs
var langiumParser = createRailroadPegServices().RailroadPeg.parser.LangiumParser;
var transformOrderedChoice = __name((choice) => {
  const alternatives = choice.alternatives.map(transformSequence);
  if (alternatives.length === 1) {
    return alternatives[0];
  }
  return {
    type: "choice",
    alternatives
  };
}, "transformOrderedChoice");
var transformSequence = __name((sequence) => {
  const elements = sequence.elements.map(transformPrefix);
  if (elements.length === 1) {
    return elements[0];
  }
  return {
    type: "sequence",
    elements
  };
}, "transformSequence");
var transformPrefix = __name((prefix) => {
  const inner = transformSuffix(prefix.suffix);
  if (!prefix.operator) {
    return inner;
  }
  const label = prefix.operator === "&" ? `&${nodeToLabel(inner)}` : `!${nodeToLabel(inner)}`;
  return {
    type: "special",
    text: label
  };
}, "transformPrefix");
var nodeToLabel = __name((node) => {
  switch (node.type) {
    case "terminal":
      return `"${node.value}"`;
    case "nonterminal":
      return node.name;
    case "special":
      return node.text;
    default:
      return "(...)";
  }
}, "nodeToLabel");
var transformSuffix = __name((suffix) => {
  const inner = transformPrimary(suffix.primary);
  if (!suffix.operator) {
    return inner;
  }
  switch (suffix.operator) {
    case "?":
      return {
        type: "optional",
        element: inner
      };
    case "*":
      return {
        type: "repetition",
        element: inner,
        min: 0,
        max: Infinity
      };
    case "+":
      return {
        type: "repetition",
        element: inner,
        min: 1,
        max: Infinity
      };
    default:
      throw new Error(`Unsupported PEG suffix operator: ${suffix.operator}`);
  }
}, "transformSuffix");
var transformPrimary = __name((primary) => {
  switch (primary.$type) {
    case "PegLiteral":
      return {
        type: "terminal",
        value: primary.value
      };
    case "PegIdentifier":
      return {
        type: "nonterminal",
        name: primary.name
      };
    case "PegGroup":
      return transformOrderedChoice(primary.element);
    case "PegAny":
      return {
        type: "special",
        text: primary.dot
      };
    default:
      throw new Error(`Unsupported PEG primary node: ${primary.$type}`);
  }
}, "transformPrimary");
var transformRule = __name((rule) => {
  return {
    name: rule.name,
    definition: transformOrderedChoice(rule.definition)
  };
}, "transformRule");
var populateDb = __name((ast) => {
  populateCommonDb(ast, db);
  if (ast.title) {
    db.setTitle(ast.title);
  }
  ast.rules.map((rule) => db.addRule(transformRule(rule)));
}, "populateDb");
var parser = {
  parse: __name((input) => {
    db.clear();
    log.debug("[PEG Parser] Starting Langium parse");
    const result = langiumParser.parse(input);
    if (result.lexerErrors.length > 0 || result.parserErrors.length > 0) {
      throw new MermaidParseError(result);
    }
    const ast = result.value;
    log.debug("[PEG Parser] Parsed rules:", ast.rules.length);
    populateDb(ast);
    log.debug("[PEG Parser] Parse complete");
  }, "parse"),
  parser: {
    yy: db
  }
};
var diagram = {
  parser,
  db,
  renderer,
  styles: getStyles
};
export {
  diagram
};
//# sourceMappingURL=pegDiagram-2B236MQR-CQDQGXGJ.js.map
