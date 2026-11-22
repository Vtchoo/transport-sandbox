/** Short game warnings anchored near the last pointer position, with keyboard fallback. */
export class WarningMessages {
  private point?: { x: number; y: number };
  private timer?: ReturnType<typeof setTimeout>;
  private abort = new AbortController();
  constructor(private element: HTMLElement) {
    document.addEventListener('pointermove', event => { this.point = { x: event.clientX, y: event.clientY }; }, { signal: this.abort.signal, capture: true });
    document.addEventListener('pointerdown', event => { this.point = { x: event.clientX, y: event.clientY }; }, { signal: this.abort.signal, capture: true });
    window.addEventListener('resize', () => this.hide(), { signal: this.abort.signal });
  }
  show(message: string) {
    clearTimeout(this.timer);
    const focused = document.activeElement?.getBoundingClientRect();
    const point = this.point ?? { x: focused?.width ? focused.x + focused.width / 2 : innerWidth / 2, y: focused?.height ? focused.bottom : innerHeight / 2 };
    this.element.textContent = message; this.element.hidden = false;
    const rect = this.element.getBoundingClientRect();
    this.element.style.left = `${Math.max(8, Math.min(point.x + 16, innerWidth - rect.width - 8))}px`;
    this.element.style.top = `${Math.max(8, point.y + 20 + rect.height < innerHeight - 8 ? point.y + 20 : point.y - rect.height - 12)}px`;
    this.timer = setTimeout(() => this.hide(), 4500);
  }
  hide() { clearTimeout(this.timer); this.element.hidden = true; }
  dispose() { this.hide(); this.abort.abort(); }
}
