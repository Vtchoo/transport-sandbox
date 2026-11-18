/** DOM owned by one track editor; screen-space positioning and actions stay with the editor. */
export class TrackHoverMenu {
  readonly element = document.createElement("div");
  readonly direction: HTMLButtonElement;
  readonly continueParallel: HTMLButtonElement;
  readonly deleteNode: HTMLButtonElement;
  readonly merge: HTMLButtonElement;
  readonly straighten: HTMLButtonElement;
  readonly confirmSplit: HTMLButtonElement;
  readonly deleteSegment: HTMLButtonElement;
  readonly parallelLeft: HTMLButtonElement;
  readonly parallelRight: HTMLButtonElement;

  constructor() {
    const element = this.element;
    element.id = "track-hover-menu";
    element.setAttribute("role", "group");
    element.setAttribute("aria-label", "Track actions");
    element.innerHTML = `
      <button id="track-direction" type="button" hidden aria-label="Start track from node">+</button>
      <button id="track-continue-parallel" type="button" hidden aria-label="Continue all parallel tracks" title="Continue all parallel tracks"></button>
      <button id="track-delete" type="button" hidden aria-label="Delete node" title="Delete node">×</button>
      <button id="track-merge" type="button" hidden aria-label="Merge segments" title="Merge segments">⇄</button>
      <button id="track-straighten" type="button" hidden>Straighten</button>
      <button id="track-confirm-split" type="button" hidden>Confirm split</button>
      <button id="track-delete-segment" type="button" hidden>Delete track</button>
      <button id="track-parallel-left" class="track-parallel" type="button" hidden aria-label="Add parallel node on the left">Ⅱ+</button>
      <button id="track-parallel-right" class="track-parallel" type="button" hidden aria-label="Add parallel node on the right">Ⅱ+</button>`;
    const button = (id: string) =>
      element.querySelector<HTMLButtonElement>(`#${id}`)!;
    this.direction = button("track-direction");
    this.continueParallel = button("track-continue-parallel");
    this.deleteNode = button("track-delete");
    this.merge = button("track-merge");
    this.straighten = button("track-straighten");
    this.confirmSplit = button("track-confirm-split");
    this.deleteSegment = button("track-delete-segment");
    this.parallelLeft = button("track-parallel-left");
    this.parallelRight = button("track-parallel-right");
    document.getElementById("game")!.append(element);
  }
  dispose() {
    this.element.remove();
  }
}
