import {
  mdiTrain,
  mdiTrainCar,
  mdiWrench,
  mdiViewDashboard,
  mdiDelete,
  mdiSwapHorizontal,
  mdiPlus,
  mdiFormatListBulleted,
  mdiClose,
  mdiMinus,
  mdiChevronUp,
  mdiChevronDown,
  mdiResizeBottomRight,
  mdiUndo,
  mdiArrowLeft,
  mdiArrowRight,
  mdiMapMarker,
  mdiPlay,
  mdiStop,
  mdiTrayArrowUp,
  mdiCrosshairsGps,
  mdiTrainVariant,
  mdiTrainCarPassengerDoor,
  mdiHighway,
} from "@mdi/js";

export const icons = {
  train: mdiTrain,
  car: mdiTrainCar,
  edit: mdiWrench,
  viewport: mdiViewDashboard,
  remove: mdiDelete,
  flip: mdiSwapHorizontal,
  add: mdiPlus,
  list: mdiFormatListBulleted,
  close: mdiClose,
  minimize: mdiMinus,
  collapse: mdiChevronUp,
  expand: mdiChevronDown,
  resize: mdiResizeBottomRight,
  undo: mdiUndo,
  left: mdiArrowLeft,
  right: mdiArrowRight,
  place: mdiMapMarker,
  play: mdiPlay,
  stop: mdiStop,
  pickup: mdiTrayArrowUp,
  follow: mdiCrosshairsGps,
  track: mdiTrainVariant,
  station: mdiTrainCarPassengerDoor,
  parallel: mdiHighway,
};

export function icon(name: keyof typeof icons) {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("aria-hidden", "true");
  svg.classList.add("app-icon");
  const path = document.createElementNS(svg.namespaceURI, "path");
  path.setAttribute("d", icons[name]);
  svg.append(path);
  return svg;
}
