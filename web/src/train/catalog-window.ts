import { VEHICLES, vehicleDefinition } from "./catalog";
import { catalogAllows } from "./catalog-routing";
import type { TrainConsist } from "./consist";
import { vehicleThumbnail } from "./thumbnail";
import type { WindowManager } from "../ui/windows";

export const VEHICLE_DRAG_TYPE = "application/x-railway-vehicle";
interface CatalogActions {
  select(id: string): void;
  dragStart(id: string): void;
  dragEnd(): void;
}

/** Owns the catalog's window, model list, filtering, preview selection and event lifecycle. */
export class VehicleCatalogWindow {
  readonly element = document.createElement("section");
  readonly viewport: HTMLCanvasElement;
  private abort = new AbortController();
  private hovered?: string;
  private focused?: string;
  private buttons: HTMLButtonElement[];
  private context: HTMLElement;
  private empty: HTMLElement;
  private previewName: HTMLElement;

  constructor(
    private manager: WindowManager,
    actions: CatalogActions,
  ) {
    const element = this.element;
    element.id = "vehicle-catalog-panel";
    element.hidden = true;
    element.setAttribute("aria-labelledby", "vehicle-catalog-title");
    element.innerHTML = `
      <div class="track-panel-heading"><h2 id="vehicle-catalog-title">Vehicle catalog</h2><button id="vehicle-catalog-close" type="button">Close</button></div>
      <p id="catalog-context" class="catalog-context"></p>
      <div class="catalog-scroll"><div class="vehicle-catalog" aria-label="Vehicle models"></div></div>
      <p id="catalog-empty" hidden>No compatible vehicles are available.</p>
      <div class="catalog-preview">
        <canvas id="catalog-viewport" width="384" height="216" aria-label="Rotating vehicle preview" hidden></canvas>
        <p id="catalog-preview-name">Hover a vehicle to preview it</p>
      </div>
      <p class="track-help">Edit train on the frontmost train window: append to its rear. Otherwise, click a vehicle to create a new train.</p>`;
    this.viewport =
      element.querySelector<HTMLCanvasElement>("#catalog-viewport")!;
    this.context = element.querySelector("#catalog-context")!;
    this.empty = element.querySelector("#catalog-empty")!;
    this.previewName = element.querySelector("#catalog-preview-name")!;
    const list = element.querySelector(".vehicle-catalog")!,
      signal = this.abort.signal;
    this.buttons = VEHICLES.map((definition) => {
      const button = document.createElement("button");
      button.type = "button";
      button.dataset.vehicle = definition.id;
      button.draggable = true;
      const name = document.createElement("strong"),
        detail = document.createElement("span");
      name.textContent = definition.name;
      detail.textContent = `${definition.length.toFixed(2)} m · ${Object.keys(
        definition.bogiesByGauge,
      )
        .map((gauge) => Number(gauge).toLocaleString())
        .join(" / ")} mm`;
      button.append(vehicleThumbnail(definition), name, detail);
      list.append(button);
      button.addEventListener("click", () => actions.select(definition.id), {
        signal,
      });
      button.addEventListener(
        "pointerenter",
        () => {
          this.hovered = definition.id;
          this.refreshPreview();
        },
        { signal },
      );
      button.addEventListener(
        "pointerleave",
        () => {
          this.hovered = undefined;
          this.refreshPreview();
        },
        { signal },
      );
      button.addEventListener(
        "focus",
        () => {
          this.focused = definition.id;
          this.refreshPreview();
        },
        { signal },
      );
      button.addEventListener(
        "blur",
        () => {
          this.focused = undefined;
          this.refreshPreview();
        },
        { signal },
      );
      button.addEventListener(
        "dragstart",
        (event) => {
          actions.dragStart(definition.id);
          event.dataTransfer?.setData(VEHICLE_DRAG_TYPE, definition.id);
          if (event.dataTransfer) event.dataTransfer.effectAllowed = "copy";
        },
        { signal },
      );
      button.addEventListener("dragend", () => actions.dragEnd(), { signal });
      return button;
    });
    document.getElementById("game")!.append(element);
    manager.register({
      id: "catalog",
      element,
      titlebar: element.querySelector(".track-panel-heading")!,
      title: "Vehicle catalog",
      closeButton: element.querySelector("#vehicle-catalog-close")!,
      bounds: { x: 24, y: 85, width: 350, height: 590 },
      minWidth: 280,
      minHeight: 440,
      onClose: () => {},
    });
  }
  get previewVehicle() {
    return this.hovered ?? this.focused;
  }
  private refreshPreview() {
    const id = this.previewVehicle;
    this.viewport.hidden = !id;
    this.previewName.textContent = id
      ? vehicleDefinition(id).name
      : "Hover a vehicle to preview it";
  }
  refresh(target?: TrainConsist, gauge?: number) {
    let count = 0;
    for (const button of this.buttons) {
      button.hidden = !catalogAllows(
        vehicleDefinition(button.dataset.vehicle!),
        target,
        gauge,
      );
      if (!button.hidden) count++;
      else {
        if (button.dataset.vehicle === this.hovered) this.hovered = undefined;
        if (button.dataset.vehicle === this.focused) this.focused = undefined;
      }
    }
    this.context.textContent = target
      ? "Compatible vehicles · click to append to the rear of the frontmost train editor."
      : "Click a vehicle to create a new train.";
    this.empty.hidden = count !== 0;
    this.refreshPreview();
  }
  dispose() {
    this.abort.abort();
    this.manager.unregister("catalog");
    this.element.remove();
    this.hovered = undefined;
    this.focused = undefined;
  }
}
