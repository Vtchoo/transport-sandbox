import { icon } from './icons';
export interface WindowBounds { x: number; y: number; width: number; height: number }
interface WindowOptions {
  id: string; element: HTMLElement; titlebar: HTMLElement; title: string; closeButton: HTMLButtonElement;
  bounds: WindowBounds; minWidth?: number; minHeight?: number; modal?: boolean; scrollContent?: boolean; onClose: () => void;
}
interface ManagedWindow extends WindowOptions {
  bounds: WindowBounds; open: boolean; minimized: boolean; collapsed: boolean;
  minimize: HTMLButtonElement; rollup: HTMLButtonElement;
  controller: AbortController;
}
const STORAGE_KEY = 'rail-window-layout-v1';
const MARGIN = 8, BOTTOM = 105;

/** Shared by the DOM windows and keyboard/pointer resize paths. */
export function constrainWindow(bounds: WindowBounds, viewport: { width: number; height: number }, minimum = { width: 260, height: 200 }): WindowBounds {
  const availableWidth = Math.max(1, viewport.width - MARGIN * 2), availableHeight = Math.max(36, viewport.height - MARGIN - BOTTOM);
  const width = Math.max(Math.min(minimum.width, availableWidth), Math.min(bounds.width, availableWidth));
  const height = Math.max(Math.min(minimum.height, availableHeight), Math.min(bounds.height, availableHeight));
  return { x: Math.max(MARGIN, Math.min(bounds.x, viewport.width - width - MARGIN)), y: Math.max(MARGIN, Math.min(bounds.y, viewport.height - height - BOTTOM)), width, height };
}

export class WindowManager {
  private windows = new Map<string, ManagedWindow>();
  private order: string[] = [];
  private active?: string;
  private abort = new AbortController();
  private observer = new ResizeObserver(() => this.layout());
  private interaction?: { id: string; pointer: number; target: HTMLElement; startX: number; startY: number; bounds: WindowBounds; resize: boolean };
  private saved: Record<string, WindowBounds> = {};
  constructor(private taskbar: HTMLElement, private changed: (active?: string) => void = () => {}) {
    try { this.saved = JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}'); } catch { /* Optional storage. */ }
    window.addEventListener('resize', () => this.layout(), { signal: this.abort.signal });
    window.addEventListener('blur', () => this.endInteraction(), { signal: this.abort.signal });
  }
  register(options: WindowOptions) {
    const { element, titlebar, closeButton } = options;
    element.classList.add('game-window'); element.dataset.windowId = options.id;
    if (!(element instanceof HTMLDialogElement)) element.setAttribute('role', 'dialog');
    element.setAttribute('aria-label', options.title);
    titlebar.classList.add('game-window-titlebar'); titlebar.tabIndex = 0;
    titlebar.setAttribute('aria-label', `${options.title}. Drag to move, arrow keys to move, Alt plus arrows to resize. Double-click to roll up.`);
    const controls = document.createElement('div'); controls.className = 'game-window-controls';
    const make = (text: string, label: string) => { const button = document.createElement('button'); button.type = 'button'; button.textContent = text; button.title = label; button.setAttribute('aria-label', label); controls.append(button); return button; };
    const minimize = make('−', `Minimize ${options.title}`), rollup = make('▴', `Roll up ${options.title}`);
    minimize.replaceChildren(icon('minimize'));
    closeButton.replaceChildren(icon('close')); closeButton.title = `Close ${options.title}`; closeButton.setAttribute('aria-label', closeButton.title);
    controls.append(closeButton); titlebar.append(controls);
    if (options.scrollContent) {
      const content = document.createElement('div'); content.className = 'game-window-content';
      for (const child of [...element.children]) if (child !== titlebar) content.append(child);
      element.append(content);
    }
    const grip = document.createElement('button'); grip.type = 'button'; grip.className = 'game-window-resize'; grip.title = `Resize ${options.title}`; grip.setAttribute('aria-label', `${grip.title}. Drag or use arrow keys.`); element.append(grip);
    const saved = this.saved?.[options.id];
    const bounds = saved && [saved.x, saved.y, saved.width, saved.height].every(Number.isFinite) ? { ...saved } : { ...options.bounds };
    const controller = new AbortController();
    const managed: ManagedWindow = { ...options, bounds, open: false, minimized: false, collapsed: false, minimize, rollup, controller };
    this.windows.set(options.id, managed); this.order.push(options.id);
    if (options.modal) { minimize.hidden = true; rollup.hidden = true; }
    const signal = controller.signal;
    const activate = (event: Event) => { if (!(event.target as HTMLElement).closest('.game-window-controls')) this.focus(options.id); };
    element.addEventListener('pointerdown', activate, { capture: true, signal });
    element.addEventListener('focusin', activate, { signal });
    closeButton.addEventListener('click', () => this.close(options.id), { signal });
    minimize.addEventListener('click', () => this.minimize(options.id), { signal });
    rollup.addEventListener('click', () => this.rollup(options.id), { signal });
    titlebar.addEventListener('dblclick', event => { if (!(event.target as HTMLElement).closest('button')) this.rollup(options.id); }, { signal });
    for (const [target, resize] of [[titlebar, false], [grip, true]] as [HTMLElement, boolean][]) {
      target.addEventListener('pointerdown', event => {
        if (event.button !== 0 || (!resize && (event.target as HTMLElement).closest('button,input,select,a'))) return;
        event.preventDefault(); this.endInteraction();
        this.interaction = { id: options.id, pointer: event.pointerId, target, startX: event.clientX, startY: event.clientY, bounds: { ...managed.bounds }, resize };
        target.setPointerCapture(event.pointerId); target.focus({ preventScroll: true }); element.classList.add('window-moving');
      }, { signal });
      target.addEventListener('pointermove', event => {
        const drag = this.interaction; if (!drag || drag.pointer !== event.pointerId || drag.id !== options.id) return;
        if (!(event.buttons & 1)) { this.endInteraction(); return; }
        const dx = event.clientX - drag.startX, dy = event.clientY - drag.startY;
        managed.bounds = drag.resize ? { ...drag.bounds, width: drag.bounds.width + dx, height: drag.bounds.height + dy } : { ...drag.bounds, x: drag.bounds.x + dx, y: drag.bounds.y + dy };
        this.apply(managed);
      }, { signal });
      target.addEventListener('pointerup', () => this.endInteraction(), { signal });
      target.addEventListener('pointercancel', () => this.endInteraction(), { signal });
      target.addEventListener('lostpointercapture', () => this.endInteraction(), { signal });
      target.addEventListener('keydown', event => {
        if (event.target !== target || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
        event.preventDefault(); event.stopPropagation();
        const amount = event.shiftKey ? 1 : 10, dx = event.key === 'ArrowLeft' ? -amount : event.key === 'ArrowRight' ? amount : 0, dy = event.key === 'ArrowUp' ? -amount : event.key === 'ArrowDown' ? amount : 0;
        if (resize || event.altKey) { managed.bounds.width += dx; managed.bounds.height += dy; }
        else { managed.bounds.x += dx; managed.bounds.y += dy; }
        this.apply(managed); this.save();
      }, { signal });
    }
    this.observer.observe(element); this.apply(managed); element.hidden = true;
  }
  isOpen(id: string) { return !!this.windows.get(id)?.open; }
  get activeId() { return this.active; }
  topmost(matches: (id: string) => boolean) { return [...this.order].reverse().find(id => this.isExpanded(id) && matches(id)); }
  unregister(id: string) {
    const w = this.windows.get(id); if (!w) return;
    this.saved[id] = { ...w.bounds };
    if (this.interaction?.id === id) this.endInteraction();
    w.controller.abort(); this.observer.unobserve(w.element); this.windows.delete(id);
    this.order = this.order.filter(other => other !== id); this.chooseActive(id); this.render();
  }
  isVisible(id: string) { const w = this.windows.get(id); return !!w?.open && !w.minimized; }
  isExpanded(id: string) { const w = this.windows.get(id); return this.isVisible(id) && !w!.collapsed; }
  open(id: string) {
    const w = this.windows.get(id)!; w.open = true; w.minimized = false; w.collapsed = false;
    this.focus(id); this.render();
  }
  focus(id: string) {
    const w = this.windows.get(id); if (!w?.open || w.minimized) return;
    this.order = this.order.filter(item => item !== id); this.order.push(id);
    if (this.active !== id) { this.endInteraction(); this.active = id; }
    this.changed(this.isExpanded(id) ? id : undefined);
    this.render();
  }
  close(id: string) {
    const w = this.windows.get(id); if (!w?.open) return;
    this.endInteraction(); w.open = false; w.onClose(); this.chooseActive(id); this.render(); this.save();
  }
  minimize(id: string) {
    const w = this.windows.get(id); if (!w?.open || w.modal) return;
    this.endInteraction(); w.minimized = true; this.chooseActive(id); this.render();
  }
  rollup(id: string) {
    const w = this.windows.get(id); if (!w?.open || w.modal) return;
    this.endInteraction(); w.collapsed = !w.collapsed; w.rollup.textContent = w.collapsed ? '▾' : '▴';
    w.rollup.setAttribute('aria-label', `${w.collapsed ? 'Expand' : 'Roll up'} ${w.title}`);
    if (this.active === id) this.changed(w.collapsed ? undefined : id);
    this.render();
  }
  closeAll() { for (const w of this.windows.values()) if (w.open) this.close(w.id); }
  private chooseActive(id: string) {
    if (this.active !== id) return;
    this.active = [...this.order].reverse().find(other => this.isExpanded(other)); this.changed(this.active);
  }
  private apply(w: ManagedWindow) {
    w.bounds = constrainWindow(w.bounds, { width: innerWidth, height: innerHeight }, { width: w.minWidth ?? 260, height: w.minHeight ?? 200 });
    const { x, y, width, height } = w.bounds;
    Object.assign(w.element.style, { left: `${x}px`, top: `${y}px`, bottom: 'auto', right: 'auto', width: `${width}px`, height: `${height}px` });
  }
  private layout() { for (const w of this.windows.values()) this.apply(w); }
  private render() {
    for (const [id, w] of this.windows) {
      w.element.hidden = !w.open || w.minimized; w.element.classList.toggle('window-active', id === this.active);
      w.rollup.replaceChildren(icon(w.collapsed ? 'expand' : 'collapse')); w.rollup.title = `${w.collapsed ? 'Expand' : 'Roll up'} ${w.title}`; w.rollup.setAttribute('aria-label', w.rollup.title);
      w.element.classList.toggle('window-collapsed', w.collapsed); w.element.style.zIndex = String(20 + this.order.indexOf(id));
      this.apply(w);
    }
    this.taskbar.replaceChildren();
    for (const w of this.windows.values()) if (w.open && w.minimized) {
      const button = document.createElement('button'); button.type = 'button'; button.textContent = `▣ ${w.title}`; button.title = `Restore ${w.title}`; button.dataset.restoreWindow = w.id;
      button.addEventListener('click', () => this.open(w.id)); this.taskbar.append(button);
    }
    this.taskbar.hidden = !this.taskbar.childElementCount;
  }
  private endInteraction() {
    const drag = this.interaction; this.interaction = undefined;
    if (!drag) return;
    if (drag.target.hasPointerCapture(drag.pointer)) drag.target.releasePointerCapture(drag.pointer);
    this.windows.get(drag.id)?.element.classList.remove('window-moving'); this.save();
  }
  private save() {
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(Object.fromEntries([...this.windows].map(([id, w]) => [id, w.bounds])))); } catch { /* Optional storage. */ }
  }
  dispose() { this.endInteraction(); this.abort.abort(); for (const w of this.windows.values()) w.controller.abort(); this.observer.disconnect(); }
}
