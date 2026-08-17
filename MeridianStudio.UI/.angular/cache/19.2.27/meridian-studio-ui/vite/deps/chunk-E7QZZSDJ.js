import {
  getConfig2
} from "./chunk-GMG6TM6O.js";
import {
  select_default
} from "./chunk-J73RWVFM.js";
import {
  __name
} from "./chunk-R5274TMJ.js";

// node_modules/mermaid/dist/chunks/mermaid.core/chunk-VAUOI2AC.mjs
var selectSvgElement = __name((id) => {
  const {
    securityLevel
  } = getConfig2();
  let root = select_default("body");
  if (securityLevel === "sandbox") {
    const sandboxElement = select_default(`#i${id}`);
    const doc = sandboxElement.node()?.contentDocument ?? document;
    root = select_default(doc.body);
  }
  const svg = root.select(`#${id}`);
  return svg;
}, "selectSvgElement");

export {
  selectSvgElement
};
//# sourceMappingURL=chunk-E7QZZSDJ.js.map
