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
import {
  createRailroadServices
} from "./chunk-CY5MTJ6W.js";
import "./chunk-CKGEODJK.js";
import "./chunk-R6DAIY2W.js";
import "./chunk-A6KMPXEF.js";
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

// node_modules/mermaid/dist/chunks/mermaid.core/railroadDiagram-RFXS5EU6.mjs
var langiumParser = createRailroadServices().Railroad.parser.LangiumParser;
var transformExpression = __name((expr) => {
  switch (expr.$type) {
    case "RailroadTerminalExpr":
      return {
        type: "terminal",
        value: expr.value
      };
    case "RailroadNonTerminalExpr":
      return {
        type: "nonterminal",
        name: expr.name
      };
    case "RailroadSpecialExpr":
      return {
        type: "special",
        text: expr.text
      };
    case "RailroadSequenceExpr": {
      const elements = expr.elements.map(transformExpression);
      return elements.length === 1 ? elements[0] : {
        type: "sequence",
        elements
      };
    }
    case "RailroadChoiceExpr": {
      const alternatives = expr.alternatives.map(transformExpression);
      return alternatives.length === 1 ? alternatives[0] : {
        type: "choice",
        alternatives
      };
    }
    case "RailroadOptionalExpr":
      return {
        type: "optional",
        element: transformExpression(expr.element)
      };
    case "RailroadOneOrMoreExpr":
      return {
        type: "repetition",
        element: transformExpression(expr.element),
        min: 1,
        max: Infinity
      };
    case "RailroadZeroOrMoreExpr":
      return {
        type: "repetition",
        element: transformExpression(expr.element),
        min: 0,
        max: Infinity
      };
    default:
      throw new Error(`Unsupported railroad expression: ${expr.$type}`);
  }
}, "transformExpression");
var transformRule = __name((rule) => {
  return {
    name: rule.name,
    definition: transformExpression(rule.definition)
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
    log.debug("[Railroad Parser] Starting Langium parse");
    const result = langiumParser.parse(input);
    if (result.lexerErrors.length > 0 || result.parserErrors.length > 0) {
      throw new MermaidParseError(result);
    }
    const ast = result.value;
    log.debug("[Railroad Parser] Parsed rules:", ast.rules.length);
    populateDb(ast);
    log.debug("[Railroad Parser] Parse complete");
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
var railroadDiagram_default = diagram;
export {
  railroadDiagram_default as default,
  diagram
};
//# sourceMappingURL=railroadDiagram-RFXS5EU6-WAH6PO3L.js.map
