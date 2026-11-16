import type { VehicleDefinition } from './types';

/** Display a model-owned illustration without creating a WebGL context per card. */
export function vehicleThumbnail(definition: VehicleDefinition) {
  const image = document.createElement('img');
  image.src = `data:image/svg+xml,${encodeURIComponent(definition.thumbnailSvg)}`;
  image.alt = '';
  image.className = 'vehicle-thumbnail';
  image.draggable = false;
  return image;
}
