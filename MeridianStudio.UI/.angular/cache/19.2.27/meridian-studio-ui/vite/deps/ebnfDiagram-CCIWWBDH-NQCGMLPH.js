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
import {
  createRailroadEbnfServices
} from "./chunk-CKGEODJK.js";
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

// node_modules/mermaid/dist/chunks/mermaid.core/ebnfDiagram-CCIWWBDH.mjs
var langiumParser = createRailroadEbnfServices().RailroadEbnf.parser.LangiumParser;
var transformChoice = __name((choice) => {
  const alternatives = choice.alternatives.map(transformSequence);
  if (alternatives.length === 1) {
    return alternatives[0];
  }
  return {
    type: "choice",
    alternatives
  };
}, "transformChoice");
var transformSequence = __name((sequence) => {
  const elements = sequence.elements.map(transformTerm);
  if (elements.length === 1) {
    return elements[0];
  }
  return {
    type: "sequence",
    elements
  };
}, "transformSequence");
var transformPrimary = __name((primary) => {
  switch (primary.$type) {
    case "EbnfTerminal":
      return {
        type: "terminal",
        value: primary.value
      };
    case "EbnfNonTerminal":
      return {
        type: "nonterminal",
        name: primary.name
      };
    case "EbnfSpecial":
      return {
        type: "special",
        text: primary.text
      };
    case "EbnfGroup":
      return transformChoice(primary.element);
    case "EbnfOptional":
      return {
        type: "optional",
        element: transformChoice(primary.element)
      };
    case "EbnfRepetition":
      return {
        type: "repetition",
        element: transformChoice(primary.element),
        min: 0,
        max: Infinity
      };
    default:
      throw new Error(`Unsupported EBNF primary node: ${primary.$type}`);
  }
}, "transformPrimary");
var transformPostfix = __name((node, postfix) => {
  switch (postfix.$type) {
    case "EbnfOptionalPostfix":
      return {
        type: "optional",
        element: node
      };
    case "EbnfZeroOrMorePostfix":
      return {
        type: "repetition",
        element: node,
        min: 0,
        max: Infinity
      };
    case "EbnfOneOrMorePostfix":
      return {
        type: "repetition",
        element: node,
        min: 1,
        max: Infinity
      };
    case "EbnfExceptionPostfix":
      return {
        type: "sequence",
        elements: [node, {
          type: "terminal",
          value: "-"
        }, transformPrimary(postfix.except)]
      };
    default:
      throw new Error(`Unsupported EBNF postfix node: ${postfix.$type}`);
  }
}, "transformPostfix");
var transformTerm = __name((term) => {
  return term.postfixes.reduce((currentNode, postfix) => {
    return transformPostfix(currentNode, postfix);
  }, transformPrimary(term.base));
}, "transformTerm");
var transformRule = __name((rule) => {
  return {
    name: rule.name,
    definition: transformChoice(rule.definition)
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
    log.debug("[EBNF Parser] Starting Langium parse");
    const result = langiumParser.parse(input);
    if (result.lexerErrors.length > 0 || result.parserErrors.length > 0) {
      throw new MermaidParseError(result);
    }
    const ast = result.value;
    log.debug("[EBNF Parser] Parsed rules:", ast.rules.length);
    populateDb(ast);
    log.debug("[EBNF Parser] Parse complete");
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
//# sourceMappingURL=ebnfDiagram-CCIWWBDH-NQCGMLPH.js.map
