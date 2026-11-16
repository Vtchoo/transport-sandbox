import type { VehicleDefinition } from "./catalog";

/** Lightweight side-view illustrations; no WebGL contexts or asset downloads per card. */
export function vehicleThumbnail(definition: VehicleDefinition) {
  if (definition.id.startsWith("drgw-")) return rioGrandeThumbnail(definition);
  const ge = definition.id === "ge-809",
    loco = definition.kind === "locomotive";
  const shell = ge
    ? '<path d="M12 52V36h8V22l8-10h28v7h116v33Z" fill="#b6b9b8"/><path d="M12 38h160v11H12Z" fill="#bc3943"/><path d="M25 19h12v12H17Z M42 18h10v13H42Z" fill="#203c49"/><path d="M150 22h20v15h-20Z" fill="#354649"/>'
    : loco
      ? '<path d="M10 52V38q1-19 20-23h135q7 0 7 9v28Z" fill="#c5cdcd"/><path d="M10 52V38q1-19 20-23h27L41 52Z" fill="#b8323b"/><path d="M17 30q3-9 13-10h9v11Z" fill="#203c49"/><path d="M11 43h160" stroke="#e7bb52" stroke-width="3"/><g fill="#34484c"><circle cx="69" cy="32" r="5"/><circle cx="104" cy="32" r="5"/><circle cx="139" cy="32" r="5"/></g>'
      : `<path d="M10 52V23q0-9 12-9h138q12 0 12 9v29Z" fill="#bdc9ca"/>${definition.kind === "dome" ? '<path d="M61 15V9q0-5 6-5h50q6 0 6 5v6Z" fill="#5d8d9b" stroke="#d8e2da" stroke-width="2"/>' : ""}<path d="M17 26h148v11H17Z" fill="#254755" stroke="#b6c8ca" stroke-width="3" stroke-dasharray="12 3"/><path d="M13 43h156m-156 4h156" stroke="#869e9f" stroke-width="2"/>`;
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 184 72">${shell}<path d="M9 53h164" stroke="#223637" stroke-width="4"/><g fill="#233335" stroke="#8fa3a2" stroke-width="2"><circle cx="34" cy="58" r="7"/><circle cx="50" cy="58" r="7"/><circle cx="134" cy="58" r="7"/><circle cx="150" cy="58" r="7"/></g></svg>`;
  const image = document.createElement("img");
  image.src = `data:image/svg+xml,${encodeURIComponent(svg)}`;
  image.alt = "";
  image.className = "vehicle-thumbnail";
  image.draggable = false;
  return image;
}

function rioGrandeThumbnail(definition: VehicleDefinition) {
  const steam = definition.kind === "locomotive",
    baggage = definition.id === "drgw-wood-baggage";
  const shell = steam
    ? '<path d="M17 47V27h81v20Z" fill="#343a35"/><path d="M22 26h75v5H22Z" fill="#858e86"/><path d="M28 28V12h7v16m16 0V18h10v10m16 0V18h10v10" stroke="#343a35" stroke-width="5"/><path d="M98 47V19h25v28Z M133 48V31h42v17Z" fill="#e6b62f"/><path d="M96 19h30v-4H96Z M135 30h40v-5h-40Z" fill="#2c332f"/><path d="M102 24h15v12h-15Z" fill="#233e43"/><path d="m16 45-9 11h24Z" fill="#343a35"/>'
    : `<path d="M12 52V22h160v30Z" fill="#e6b62f"/><path d="M9 23v-6h166v6Z M21 14V9h142v5Z" fill="#30372f"/><path d="M23 15h138" stroke="#85927c" stroke-width="2"/>${baggage ? '<g fill="#ce9f26" stroke="#4c4630"><path d="M54 24h27v26H54Z M102 24h27v26h-27Z"/></g><path d="M22 28h20v10H22Z M141 28h20v10h-20Z M59 27h17v9H59Z M107 27h17v9h-17Z" fill="#203d44"/>' : Array.from({ length: 12 }, (_, i) => `<path d="M${18 + i * 12} 28h8v12h-8Z" fill="#203d44"/>`).join("")}<path d="M14 45h156" stroke="#b28829" stroke-width="2"/>`;
  const wheels = steam
    ? '<circle cx="20" cy="57" r="4"/>' +
      [41, 58, 75, 92]
        .map(
          (x) =>
            `<circle cx="${x}" cy="54" r="9"/><path d="M${x - 6} 54h12m-6-6v12" stroke="#8a958b"/>`,
        )
        .join("") +
      '<circle cx="143" cy="57" r="5"/><circle cx="154" cy="57" r="5"/><circle cx="166" cy="57" r="5"/>'
    : [31, 46, 138, 153]
        .map((x) => `<circle cx="${x}" cy="58" r="6"/>`)
        .join("");
  const image = document.createElement("img");
  image.src =
    "data:image/svg+xml," +
    encodeURIComponent(
      `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 184 72">${shell}<path d="M13 51h161" stroke="#30372f" stroke-width="4"/><g fill="#28332d" stroke="#718071" stroke-width="1.5">${wheels}</g>${steam ? '<path d="M37 57h60" stroke="#c2c9be" stroke-width="3"/>' : ""}</svg>`,
    );
  image.alt = "";
  image.className = "vehicle-thumbnail";
  image.draggable = false;
  return image;
}
