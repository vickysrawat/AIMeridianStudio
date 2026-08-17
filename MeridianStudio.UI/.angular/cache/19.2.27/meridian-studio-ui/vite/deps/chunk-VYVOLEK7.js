import {
  __name
} from "./chunk-R5274TMJ.js";

// node_modules/mermaid/dist/chunks/mermaid.core/chunk-2Q5K7J3B.mjs
var ImperativeState = class {
  /**
   * @param init - Function that creates the default state.
   */
  constructor(init) {
    this.init = init;
    this.records = this.init();
  }
  static {
    __name(this, "ImperativeState");
  }
  reset() {
    this.records = this.init();
  }
};

export {
  ImperativeState
};
//# sourceMappingURL=chunk-VYVOLEK7.js.map
