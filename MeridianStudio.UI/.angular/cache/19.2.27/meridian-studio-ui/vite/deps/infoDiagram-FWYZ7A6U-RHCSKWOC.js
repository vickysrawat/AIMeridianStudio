import {
  parse
} from "./chunk-F3TYDIAJ.js";
import "./chunk-EDQBFHJY.js";
import "./chunk-5U4VC5HW.js";
import "./chunk-S6MKQXIC.js";
import "./chunk-CY5MTJ6W.js";
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
import {
  selectSvgElement
} from "./chunk-E7QZZSDJ.js";
import {
  configureSvgSize
} from "./chunk-GMG6TM6O.js";
import "./chunk-BXI6AT5O.js";
import {
  log
} from "./chunk-J73RWVFM.js";
import {
  __name
} from "./chunk-R5274TMJ.js";
import {
  __async
} from "./chunk-7RSYZEEK.js";

// node_modules/mermaid/dist/chunks/mermaid.core/infoDiagram-FWYZ7A6U.mjs
var parser = {
  parse: __name((input) => __async(null, null, function* () {
    const ast = yield parse("info", input);
    log.debug(ast);
  }), "parse")
};
var DEFAULT_INFO_DB = {
  version: "11.16.0" + (true ? "" : "-tiny")
};
var getVersion = __name(() => DEFAULT_INFO_DB.version, "getVersion");
var db = {
  getVersion
};
var draw = __name((text, id, version) => {
  log.debug("rendering info diagram\n" + text);
  const svg = selectSvgElement(id);
  configureSvgSize(svg, 100, 400, true);
  const group = svg.append("g");
  group.append("text").attr("x", 100).attr("y", 40).attr("class", "version").attr("font-size", 32).style("text-anchor", "middle").text(`v${version}`);
}, "draw");
var renderer = {
  draw
};
var diagram = {
  parser,
  db,
  renderer
};
export {
  diagram
};
//# sourceMappingURL=infoDiagram-FWYZ7A6U-RHCSKWOC.js.map
