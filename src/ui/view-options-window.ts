import type { WindowManager } from "./windows";

export interface ViewOptions {
  contours: boolean;
  contourInterval: number;
}
const INTERVALS = [5, 10, 20, 50, 100];
const STORAGE_KEY = "rail-view-v1";

export class ViewOptionsWindow {
  readonly element = document.createElement("section");
  private abort = new AbortController();
  private contours: HTMLInputElement;
  private interval: HTMLSelectElement;
  private legend: HTMLElement;

  constructor(
    private manager: WindowManager,
    opener: HTMLElement,
    private changed: (options: ViewOptions) => void,
    onClose: () => void,
  ) {
    const element = this.element;
    element.id = "view-options";
    element.hidden = true;
    element.setAttribute("aria-labelledby", "view-options-title");
    element.innerHTML = `
      <div class="settings-heading"><h2 id="view-options-title">View options</h2><button id="view-options-close" type="button" aria-label="Close view options">Close</button></div>
      <label class="view-checkbox"><input id="terrain-contours" type="checkbox" /> Show terrain contours</label>
      <label for="contour-interval">ELEVATION INTERVAL</label>
      <select id="contour-interval"></select>
      <p id="contour-legend"></p>`;
    this.contours = element.querySelector("#terrain-contours")!;
    this.interval = element.querySelector("#contour-interval")!;
    this.legend = element.querySelector("#contour-legend")!;
    this.interval.replaceChildren(
      ...INTERVALS.map((value) => new Option(`${value} metres`, String(value))),
    );
    this.interval.value = "10";
    try {
      const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) || "{}");
      this.contours.checked = saved.contours === true;
      if (INTERVALS.includes(saved.contourInterval))
        this.interval.value = String(saved.contourInterval);
    } catch {
      /* Optional storage. */
    }
    document.getElementById("app")!.append(element);
    manager.register({
      id: "view-options",
      element,
      titlebar: element.querySelector(".settings-heading")!,
      title: "View options",
      closeButton: element.querySelector("#view-options-close")!,
      scrollContent: true,
      bounds: { x: innerWidth - 340, y: 140, width: 300, height: 260 },
      minWidth: 270,
      minHeight: 200,
      onClose,
    });
    const signal = this.abort.signal;
    opener.addEventListener("click", () => this.toggle(), { signal });
    this.contours.addEventListener("change", () => this.apply(), { signal });
    this.interval.addEventListener("change", () => this.apply(), { signal });
    this.apply();
  }
  toggle() {
    if (this.manager.isVisible("view-options"))
      this.manager.close("view-options");
    else this.manager.open("view-options");
  }
  apply() {
    const options = {
      contours: this.contours.checked,
      contourInterval: Number(this.interval.value),
    };
    this.legend.textContent = `Bold contours every ${options.contourInterval * 5} m, measured from sea level. Trees are hidden while contours are shown.`;
    this.changed(options);
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(options));
    } catch {
      /* Optional storage. */
    }
  }
  dispose() {
    this.abort.abort();
    this.manager.unregister("view-options");
    this.element.remove();
  }
}
