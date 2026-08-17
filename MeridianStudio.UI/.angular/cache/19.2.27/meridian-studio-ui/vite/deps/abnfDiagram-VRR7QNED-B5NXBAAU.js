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
import {
  createRailroadAbnfServices
} from "./chunk-R6DAIY2W.js";
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

// node_modules/mermaid/dist/chunks/mermaid.core/abnfDiagram-VRR7QNED.mjs
var langiumParser = createRailroadAbnfServices().RailroadAbnf.parser.LangiumParser;
var transformAlternation = __name((alt) => {
  const alternatives = alt.alternatives.map(transformConcatenation);
  if (alternatives.length === 1) {
    return alternatives[0];
  }
  return {
    type: "choice",
    alternatives
  };
}, "transformAlternation");
var transformConcatenation = __name((concat) => {
  const elements = concat.elements.map(transformElement);
  if (elements.length === 1) {
    return elements[0];
  }
  return {
    type: "sequence",
    elements
  };
}, "transformConcatenation");
var parseRepeat = __name((repeat) => {
  if (repeat.includes("*")) {
    const [minStr, maxStr] = repeat.split("*");
    const min = minStr ? parseInt(minStr, 10) : 0;
    const max = maxStr ? parseInt(maxStr, 10) : Infinity;
    return {
      min,
      max
    };
  }
  const exact = parseInt(repeat, 10);
  return {
    min: exact,
    max: exact
  };
}, "parseRepeat");
var transformElement = __name((element) => {
  const inner = transformPrimary(element.primary);
  if (!element.repeat) {
    return inner;
  }
  const {
    min,
    max
  } = parseRepeat(element.repeat);
  if (min === 0 && max === 1) {
    return {
      type: "optional",
      element: inner
    };
  }
  return {
    type: "repetition",
    element: inner,
    min,
    max
  };
}, "transformElement");
var transformPrimary = __name((primary) => {
  switch (primary.$type) {
    case "AbnfStringLiteral":
      return {
        type: "terminal",
        value: primary.value
      };
    case "AbnfNumVal":
      return {
        type: "terminal",
        value: primary.value
      };
    case "AbnfRuleName":
      return {
        type: "nonterminal",
        name: primary.name
      };
    case "AbnfGroup":
      return transformAlternation(primary.element);
    case "AbnfOptionalGroup":
      return {
        type: "optional",
        element: transformAlternation(primary.element)
      };
    default:
      throw new Error(`Unsupported ABNF primary node: ${primary.$type}`);
  }
}, "transformPrimary");
var transformRule = __name((rule) => {
  return {
    name: rule.name,
    definition: transformAlternation(rule.definition)
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
    log.debug("[ABNF Parser] Starting Langium parse");
    const result = langiumParser.parse(input);
    if (result.lexerErrors.length > 0 || result.parserErrors.length > 0) {
      throw new MermaidParseError(result);
    }
    const ast = result.value;
    log.debug("[ABNF Parser] Parsed rules:", ast.rules.length);
    populateDb(ast);
    log.debug("[ABNF Parser] Parse complete");
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
//# sourceMappingURL=abnfDiagram-VRR7QNED-B5NXBAAU.js.map
