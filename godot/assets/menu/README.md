# Menu consists

The menu is built at runtime from the same native objects as gameplay: `TrackNetwork`,
`TrackRenderer`, catalog `VehicleVisual` instances, shared tree meshes and native station furniture.
There are no baked menu GLBs or per-preset mesh/material manifests.

Add an entry to `consists.json` to add a menu train:

```json
{
  "id": "another-train",
  "gauge": 1435,
  "vehicles": [
    { "id": "ge-809" },
    { "id": "atsf-coach", "reversed": true }
  ]
}
```

Vehicle IDs and supported gauges come from `../vehicles/catalog.json`. Each vehicle keeps
its imported metre-scale geometry. Length, 0.12 m coupler gaps, station length and camera
framing are derived from the actual consist. `reversed` turns an individual vehicle 180 degrees.
The menu chooses a random entry at startup. No scene export or code change is needed.

Menu UI layout and camera drift retain the browser reference. Scene lighting, rails,
platforms, trees and vehicle materials now use the native game objects. The idle smoke
shader retains Three.js-derived color mapping functions. The procedural tree primitives
also adapt Three.js geometry algorithms; their license follows.

MIT License

Copyright © 2010-2025 three.js authors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
