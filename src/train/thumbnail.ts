import type { VehicleDefinition } from './catalog';

/** Lightweight side-view illustrations; no WebGL contexts or asset downloads per card. */
export function vehicleThumbnail(definition: VehicleDefinition) {
  const ge = definition.id === 'ge-809', loco = definition.kind === 'locomotive';
  const shell = ge
    ? '<path d="M12 52V36h8V22l8-10h28v7h116v33Z" fill="#b6b9b8"/><path d="M12 38h160v11H12Z" fill="#bc3943"/><path d="M25 19h12v12H17Z M42 18h10v13H42Z" fill="#203c49"/><path d="M150 22h20v15h-20Z" fill="#354649"/>'
    : loco
      ? '<path d="M10 52V38q1-19 20-23h135q7 0 7 9v28Z" fill="#c5cdcd"/><path d="M10 52V38q1-19 20-23h27L41 52Z" fill="#b8323b"/><path d="M17 30q3-9 13-10h9v11Z" fill="#203c49"/><path d="M11 43h160" stroke="#e7bb52" stroke-width="3"/><g fill="#34484c"><circle cx="69" cy="32" r="5"/><circle cx="104" cy="32" r="5"/><circle cx="139" cy="32" r="5"/></g>'
      : `<path d="M10 52V23q0-9 12-9h138q12 0 12 9v29Z" fill="#bdc9ca"/>${definition.kind === 'dome' ? '<path d="M61 15V9q0-5 6-5h50q6 0 6 5v6Z" fill="#5d8d9b" stroke="#d8e2da" stroke-width="2"/>' : ''}<path d="M17 26h148v11H17Z" fill="#254755" stroke="#b6c8ca" stroke-width="3" stroke-dasharray="12 3"/><path d="M13 43h156m-156 4h156" stroke="#869e9f" stroke-width="2"/>`;
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 184 72">${shell}<path d="M9 53h164" stroke="#223637" stroke-width="4"/><g fill="#233335" stroke="#8fa3a2" stroke-width="2"><circle cx="34" cy="58" r="7"/><circle cx="50" cy="58" r="7"/><circle cx="134" cy="58" r="7"/><circle cx="150" cy="58" r="7"/></g></svg>`;
  const image = document.createElement('img'); image.src = `data:image/svg+xml,${encodeURIComponent(svg)}`;
  image.alt = ''; image.className = 'vehicle-thumbnail'; image.draggable = false; return image;
}
