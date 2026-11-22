import { vehicleDefinition } from './catalog';
import type { TrainConsist } from './consist';
import { compatibleGauges, consistLayout } from './consist';
import { catalogAllows, catalogDestination } from './catalog-routing';
import type { TrainTab } from './catalog-routing';
import { vehicleThumbnail } from './thumbnail';
import type { TrainEditor } from './editor';
import type { WindowManager } from '../ui/windows';
import { icon } from '../ui/icons';
import { trainPerformance } from './physics';
import { VehicleCatalogWindow, VEHICLE_DRAG_TYPE } from './catalog-window';

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id)! as T;
const ITEM = 'application/x-railway-train-item', VEHICLE = VEHICLE_DRAG_TYPE;
interface TrainView { element: HTMLElement; tab: TrainTab; selected?: number; status: string }
interface TrainFollow { readonly current: number | undefined; toggle(id: number): void }
export class TrainWindows {
  readonly views = new Map<number, TrainView>();
  readonly catalog: VehicleCatalogWindow;
  private abort = new AbortController();
  private dragged?: { train: number; item: number };
  private vehicleDrag?: string;
  private refreshing = false;
  private disposed = false;
  get previewVehicle() { return this.catalog.previewVehicle; }
  constructor(readonly editor: TrainEditor, readonly manager: WindowManager, private follow?: TrainFollow) {
    this.catalog = new VehicleCatalogWindow(manager, {
      select: id => this.catalogClick(id),
      dragStart: id => { this.vehicleDrag = id; },
      dragEnd: () => this.clearDrag(),
    });
    manager.register({ id: 'fleet', element: $('train-list-panel'), titlebar: $('train-list-panel').querySelector('.track-panel-heading')!, title: 'Trains', closeButton: $('train-list-close'), bounds: { x: 50, y: 110, width: 330, height: 390 }, minWidth: 270, minHeight: 200, onClose: () => {} });
    editor.onChange = action => {
      this.refresh();
      const train = editor.builder.selected;
      if (action && train) {
        const tab = action === 'Place train' || action === 'Attach trains' ? 'viewport' : !train.placed ? 'edit' : this.views.get(train.id)?.tab ?? 'viewport';
        this.openTrain(train.id, tab);
      }
    };
    editor.onMessage = (id, text) => { const view = id === undefined ? undefined : this.views.get(id); if (view) { view.status = text; this.ui(view, 'train-status').textContent = text; } };
    editor.onMotion = () => this.refreshMotion();
    editor.onClose = () => { const active = this.manager.activeId; if (active?.startsWith('train:')) manager.close(active); };
    this.refresh();
  }
  private ui<T extends HTMLElement>(view: TrainView, name: string) { return view.element.querySelector<T>(`[data-ui="${name}"]`)!; }
  private train(id: number) { return this.editor.builder.trains.find(train => train.id === id); }
  private destination() {
    const id = this.manager.topmost(id => id.startsWith('train:'));
    if (!id) return;
    const number = Number(id.slice(6)), train = this.train(number), view = this.views.get(number);
    return train && view ? catalogDestination({ train, tab: view.tab }) : undefined;
  }
  catalogClick(id: string) {
    const target = this.destination();
    if (!catalogAllows(vehicleDefinition(id), target, target && this.editor.trackGauge(target.id))) return;
    this.editor.addVehicle(id, target?.id);
  }
  toggleCatalog() { if (this.manager.isOpen('catalog') && this.manager.isVisible('catalog')) this.manager.close('catalog'); else this.manager.open('catalog'); }
  openFleet() { this.manager.open('fleet'); }
  activate(active?: string) {
    if (this.disposed || this.refreshing) return;
    const id = active?.startsWith('train:') ? Number(active.slice(6)) : undefined;
    const view = id === undefined ? undefined : this.views.get(id), train = id === undefined ? undefined : this.train(id);
    if (train) this.editor.selectTrain(train.id);
    this.editor.setEnabled(!!train && view?.tab === 'edit');
    for (const [number, other] of this.views) {
      const model = this.train(number); if (!model) continue;
      this.ui<HTMLButtonElement>(other, 'train-place').disabled = model.placed || !model.items.length || (this.editor.builder.selected?.id === number && this.editor.isPlacing);
      this.ui<HTMLButtonElement>(other, 'train-reverse').disabled = this.editor.builder.selected?.id !== number || !this.editor.isPlacing;
    }
    this.refreshCatalog();
  }
  get hasActiveWindow() { const id = this.manager.activeId; return id === 'catalog' || id === 'fleet' || !!id?.startsWith('train:'); }
  escape() {
    if (!this.hasActiveWindow) return false;
    if (this.editor.enabled) return this.editor.escape();
    this.manager.close(this.manager.activeId!); return true;
  }
  openTrain(id: number, tab?: TrainTab) {
    const train = this.train(id); if (!train) return;
    const view = this.ensureView(train); const next = tab ?? (train.placed ? 'viewport' : view.tab);
    view.tab = next; this.renderView(train, view); this.manager.open(`train:${id}`); this.activate(this.manager.activeId);
  }
  private ensureView(train: TrainConsist) {
    const existing = this.views.get(train.id); if (existing) return existing;
    const element = document.createElement('section'); element.id = `train-window-${train.id}`; element.dataset.trainId = String(train.id); element.className = 'train-window';
    element.innerHTML = `<div class="track-panel-heading"><h2></h2><button type="button" data-close>Close</button></div>
      <div class="train-tabs" role="tablist" aria-label="Train tabs"><button type="button" role="tab" data-tab="viewport"></button><button type="button" role="tab" data-tab="edit"></button></div>
      <div data-ui="train-overview" role="tabpanel"><canvas class="train-viewport" aria-label="Train viewport"></canvas><button type="button" class="train-run" data-ui="train-run">Start train</button><div class="train-tools"><button type="button" data-ui="train-follow">Follow train</button><button type="button" data-ui="train-pickup">Remove from track</button></div><p data-ui="train-location"></p><p data-ui="train-overview-specs"></p><p data-ui="train-support" hidden>Supporting track changed. The train remains at its last valid location.</p></div>
      <div data-ui="train-editor" role="tabpanel" class="train-editor-tab">
        <p data-ui="train-specs"></p><div class="consist-direction"><span>← FRONT</span><span>REAR →</span></div>
        <ol data-ui="train-consist" aria-label="Train consist from front to rear"></ol><p data-ui="train-empty">Add vehicles from the catalog.</p>
        <div data-ui="train-selection-tools" hidden><span data-ui="train-selected-name"></span><div class="train-tools"><button type="button" data-ui="train-move-front">← Move</button><button type="button" data-ui="train-move-rear">Move →</button><button type="button" data-ui="train-flip">Flip vehicle</button><button type="button" data-ui="train-remove">Remove</button></div></div>
        <div data-ui="train-trash">Drop a vehicle here to remove it</div>
        <button class="train-place" type="button" data-ui="train-place">Place train on track</button>
        <div class="train-tools"><button type="button" data-ui="train-catalog">Add vehicles…</button><button type="button" data-ui="train-reverse">Reverse preview · R</button></div>
      </div>
      <p data-ui="train-status" role="status"></p><div class="train-tools"><button type="button" data-ui="train-undo">Undo · Ctrl+Z</button></div>`;
    $('game').append(element);
    const view: TrainView = { element, tab: train.placed ? 'viewport' : 'edit', status: '' }; this.views.set(train.id, view);
    for (const [selector, name] of [['[data-tab="viewport"]', 'viewport'], ['[data-tab="edit"]', 'edit'], ['[data-ui="train-flip"]', 'flip'], ['[data-ui="train-remove"]', 'remove'], ['[data-ui="train-undo"]', 'undo'], ['[data-ui="train-place"]', 'place'], ['[data-ui="train-catalog"]', 'add']] as const) element.querySelector(selector)!.prepend(icon(name));
    element.querySelector('h2')!.textContent = train.name;
    element.querySelectorAll<HTMLButtonElement>('[data-tab]').forEach(button => {
      const tab = button.dataset.tab as TrainTab, panel = this.ui(view, tab === 'viewport' ? 'train-overview' : 'train-editor');
      button.id = `${element.id}-tab-${tab}`; panel.id = `${element.id}-panel-${tab}`; button.setAttribute('aria-controls', panel.id); panel.setAttribute('aria-labelledby', button.id);
      button.addEventListener('click', () => this.openTrain(train.id, tab));
      button.addEventListener('keydown', event => { if (!['ArrowLeft', 'ArrowRight'].includes(event.key)) return; event.preventDefault(); event.stopPropagation(); this.openTrain(train.id, tab === 'viewport' ? 'edit' : 'viewport'); element.querySelector<HTMLButtonElement>(`[data-tab="${view.tab}"]`)!.focus(); });
    });
    const action = (name: string, handler: () => void) => this.ui(view, name).addEventListener('click', () => { this.editor.selectTrain(train.id); handler(); });
    action('train-flip', () => this.editor.editConsist('Flip vehicle', () => this.editor.builder.flip(view.selected!)));
    action('train-remove', () => this.editor.editConsist('Remove vehicle', () => this.editor.builder.remove(train.id, view.selected!)));
    for (const [name, direction] of [['train-move-front', -1], ['train-move-rear', 1]] as const) action(name, () => { const current = this.train(train.id)!, from = current.items.findIndex(item => item.id === view.selected); this.editor.editConsist('Reorder vehicle', () => this.editor.builder.moveItem(train.id, view.selected!, train.id, from + (direction < 0 ? -1 : 2))); });
    action('train-place', () => { this.manager.focus(`train:${train.id}`); this.editor.startPlacement(); });
    action('train-run', () => this.editor.toggleRunning(train.id));
    action('train-pickup', () => this.editor.removeFromTrack(train.id));
    this.ui(view, 'train-pickup').prepend(icon('pickup'));
    action('train-follow', () => { this.follow?.toggle(train.id); this.refreshMotion(); });
    action('train-reverse', () => this.editor.reversePreview());
    action('train-catalog', () => this.manager.open('catalog'));
    action('train-undo', () => this.editor.undo());
    const trash = this.ui(view, 'train-trash');
    trash.addEventListener('dragover', event => { if (this.dragged && event.dataTransfer?.types.includes(ITEM)) { event.preventDefault(); trash.classList.add('drop-ready'); } });
    trash.addEventListener('dragleave', () => trash.classList.remove('drop-ready'));
    trash.addEventListener('drop', event => { const drag = this.validItemDrag(event); if (drag?.train === train.id) { event.preventDefault(); this.editor.selectTrain(train.id); this.editor.editConsist('Remove vehicle', () => this.editor.builder.remove(train.id, drag.item)); } this.clearDrag(); });
    this.manager.register({ id: `train:${train.id}`, title: train.name, element, titlebar: element.querySelector('.track-panel-heading')!, closeButton: element.querySelector('[data-close]')!, bounds: { x: 390 + (train.id - 1) % 5 * 28, y: 85 + (train.id - 1) % 5 * 26, width: 550, height: 510 }, minWidth: 340, minHeight: 330, onClose: () => { if (this.editor.builder.selected?.id === train.id) this.editor.setEnabled(false); this.refreshCatalog(); } });
    return view;
  }
  private validItemDrag(event: DragEvent) {
    if (!this.dragged || !event.dataTransfer?.types.includes(ITEM)) return;
    try { const payload = JSON.parse(event.dataTransfer.getData(ITEM)); if (payload.train === this.dragged.train && payload.item === this.dragged.item) return this.dragged; } catch { /* External drag. */ }
  }
  private clearDrag() { this.dragged = undefined; this.vehicleDrag = undefined; document.querySelectorAll('.drop-ready,.dragging').forEach(element => element.classList.remove('drop-ready', 'dragging')); }
  private bindDrop(element: HTMLElement, trainId: number, index: number) {
    element.addEventListener('dragover', event => { if (!event.dataTransfer || !((this.dragged && event.dataTransfer.types.includes(ITEM)) || (this.vehicleDrag && event.dataTransfer.types.includes(VEHICLE)))) return; event.preventDefault(); element.classList.add('drop-ready'); event.dataTransfer.dropEffect = this.vehicleDrag ? 'copy' : 'move'; });
    element.addEventListener('dragleave', () => element.classList.remove('drop-ready'));
    element.addEventListener('drop', event => {
      event.preventDefault(); const item = this.validItemDrag(event);
      if (item) this.editor.editConsist('Move vehicle', () => this.editor.builder.moveItem(item.train, item.item, trainId, index));
      else if (this.vehicleDrag && event.dataTransfer?.getData(VEHICLE) === this.vehicleDrag && catalogAllows(vehicleDefinition(this.vehicleDrag), this.train(trainId))) this.editor.addVehicle(this.vehicleDrag, trainId, index);
      this.clearDrag();
    });
  }
  private renderView(train: TrainConsist, view: TrainView) {
    const ui = <T extends HTMLElement>(name: string) => this.ui<T>(view, name);
    view.element.querySelectorAll<HTMLButtonElement>('[data-tab]').forEach(button => { const selected = button.dataset.tab === view.tab; button.setAttribute('aria-selected', String(selected)); button.tabIndex = selected ? 0 : -1; });
    ui('train-overview').hidden = view.tab !== 'viewport'; ui('train-editor').hidden = view.tab !== 'edit';
    const specs = `${train.items.length} vehicles · ${consistLayout(train.items.map(item => item.definition)).length.toFixed(2)} m\nGauge: ${compatibleGauges(train.items.map(item => item.definition)).map(gauge => `${gauge.toLocaleString()} mm`).join(', ') || '—'}`;
    ui('train-specs').textContent = specs; ui('train-overview-specs').textContent = specs; ui('train-location').textContent = train.placed ? 'Parked on track' : 'In construction · not placed';
    ui('train-support').hidden = this.editor.isSupported(train.id);
    const list = ui('train-consist'), scroll = list.scrollLeft; list.replaceChildren();
    const gap = (index: number) => { const slot = document.createElement('li'); slot.className = 'consist-slot'; const button = document.createElement('button'); button.type = 'button'; button.textContent = '+'; button.dataset.insertIndex = String(index); button.setAttribute('aria-label', `Insert at position ${index + 1}`); button.title = 'Drag a vehicle here to insert it'; this.bindDrop(button, train.id, index); slot.append(button); list.append(slot); };
    gap(0);
    train.items.forEach((item, index) => {
      const row = document.createElement('li'); row.className = 'consist-item'; row.dataset.itemId = String(item.id); row.draggable = true;
      const button = document.createElement('button'); button.type = 'button'; button.className = 'consist-select'; button.setAttribute('aria-pressed', String(view.selected === item.id));
      const thumb = vehicleThumbnail(item.definition); thumb.classList.toggle('reversed', item.facing === -1);
      const title = document.createElement('strong'), detail = document.createElement('small'); title.textContent = `${index + 1}. ${item.definition.name}`; detail.textContent = `${item.definition.length.toFixed(2)} m${item.facing === -1 ? ' · reversed' : ''}`;
      button.append(thumb, title, detail); row.append(button); button.addEventListener('click', () => { view.selected = item.id; this.renderView(this.train(train.id)!, view); });
      row.addEventListener('dragstart', event => { this.dragged = { train: train.id, item: item.id }; event.dataTransfer?.setData(ITEM, JSON.stringify(this.dragged)); if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move'; row.classList.add('dragging'); });
      row.addEventListener('dragend', () => this.clearDrag()); list.append(row); gap(index + 1);
    }); list.scrollLeft = scroll;
    const index = train.items.findIndex(item => item.id === view.selected);
    ui('train-selection-tools').hidden = index < 0; ui('train-selected-name').textContent = index < 0 ? '' : train.items[index].definition.name;
    ui<HTMLButtonElement>('train-move-front').disabled = index <= 0; ui<HTMLButtonElement>('train-move-rear').disabled = index < 0 || index >= train.items.length - 1;
    ui('train-empty').hidden = !!train.items.length; ui('train-trash').hidden = !train.items.length;
    ui<HTMLButtonElement>('train-place').disabled = train.placed || !train.items.length || (this.editor.builder.selected?.id === train.id && this.editor.isPlacing);
    ui<HTMLButtonElement>('train-reverse').disabled = this.editor.builder.selected?.id !== train.id || !this.editor.isPlacing;
    ui<HTMLButtonElement>('train-undo').disabled = !this.editor.canUndo; ui('train-status').textContent = view.status;
    this.renderMotion(train, view);
  }
  private renderMotion(train: TrainConsist, view: TrainView) {
    const pickup = this.ui<HTMLButtonElement>(view, 'train-pickup'); pickup.disabled = !train.placed; pickup.title = 'Remove the stopped train from its rails for repositioning';
    const follow = this.ui<HTMLButtonElement>(view, 'train-follow'), following = this.follow?.current === train.id;
    follow.disabled = !train.placed || !this.follow; follow.textContent = following ? 'Stop following' : 'Follow train'; follow.prepend(icon('follow')); follow.setAttribute('aria-pressed', String(following));
    const button = this.ui<HTMLButtonElement>(view, 'train-run');
    button.textContent = train.running ? 'Stop train' : 'Start train'; button.prepend(icon(train.running ? 'stop' : 'play'));
    button.disabled = !train.placed; button.setAttribute('aria-pressed', String(train.running));
    this.ui(view, 'train-location').textContent = !train.placed ? 'In construction · not placed' : `${train.running ? 'Started' : Math.abs(train.speed) > 0.001 ? 'Braking to stop' : 'Stopped'} · ${(Math.abs(train.speed) * 3.6).toFixed(1)} km/h${train.travelDirection === -1 ? ' · reversing' : ''}`;
    const p = trainPerformance(train.items.map(i => i.definition), this.editor.physicsMode);
    this.ui(view, 'train-overview-specs').textContent = `${train.items.length} vehicles · ${consistLayout(train.items.map(i => i.definition)).length.toFixed(2)} m · ${(p.mass / 1000).toFixed(1)} t\n${this.editor.physicsMode === 'ho' ? 'HO' : 'Realistic'} physics · limit ${(p.maxSpeed * 3.6).toFixed(0)} km/h`;
  }
  private refreshMotion() {
    for (const [id, view] of this.views) { const train = this.train(id); if (train) this.renderMotion(train, view); }
    for (const button of $('train-fleet').querySelectorAll<HTMLButtonElement>('[data-open-train]')) {
      const train = this.train(Number(button.dataset.openTrain)); if (train) button.textContent = `${train.name} · ${train.items.length} vehicles · ${train.placed ? train.running ? 'started' : Math.abs(train.speed) > 0.001 ? 'braking' : 'stopped' : 'construction'}`;
    }
  }
  private refreshCatalog() {
    if (this.disposed) return;
    const target = this.destination();
    this.catalog.refresh(target, target && this.editor.trackGauge(target.id));
    $('place-train').setAttribute('aria-pressed', String(this.manager.isVisible('catalog')));
  }
  refresh() {
    if (this.disposed || this.refreshing) return; this.refreshing = true;
    try {
      for (const [id, view] of this.views) if (!this.train(id)) { this.views.delete(id); this.manager.unregister(`train:${id}`); view.element.remove(); }
      for (const train of this.editor.builder.trains) this.renderView(train, this.ensureView(train));
      const fleet = $('train-fleet'); fleet.replaceChildren();
      if (!this.editor.builder.trains.length) fleet.textContent = 'No trains yet. Choose a vehicle from the catalog.';
      for (const train of this.editor.builder.trains) {
        const button = document.createElement('button'); button.type = 'button'; button.dataset.openTrain = String(train.id); button.textContent = `${train.name} · ${train.items.length} vehicles · ${train.placed ? 'parked' : 'construction'}`; button.addEventListener('click', () => this.openTrain(train.id, 'viewport')); fleet.append(button);
      }
      this.refreshCatalog();
      this.refreshMotion();
    } finally { this.refreshing = false; }
  }
  dispose() {
    this.disposed = true; this.abort.abort(); this.editor.onChange = undefined; this.editor.onMessage = undefined; this.editor.onClose = undefined; this.editor.onMotion = undefined;
    for (const [id, view] of this.views) { this.manager.unregister(`train:${id}`); view.element.remove(); } this.views.clear();
    this.catalog.dispose(); this.manager.unregister('fleet');
  }
}
