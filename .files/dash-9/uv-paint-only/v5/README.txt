dash9-v5: material-selected paint-only UV atlas, 4096 x 4096

Exactly the valid faces assigned to material "paint" are mapped.
Other faces keep their original solid-color materials, without UVs or texture binding.
LEFT SIDE: front at right. RIGHT SIDE: front at left. Paint lettering normally on both.
The large sides, roof, front and rear use recognizable orthographic views.
Cabinet doors stay in their actual side-view locations. Side faces at different depths intentionally share projected paint coordinates, so stripes align across the body and cabinets.
Occluded/secondary marked surfaces are unwrapped separately in the lower-right area; no marked faces are omitted.
Sloped surfaces in the main views use projection mapping, so their texel density varies with slope.
Paint the basecolor PNG, using the transparent layout PNG as a guide layer.
Magenta in the basecolor is the original paint placeholder; replace it with your livery.
The textured DAE binds that PNG only to the marked faces. Keep DAE/PNG together.
The checker DAE and two-view preview show texture coverage and orientation.
This authoring kit does not change the Godot game model. The older Santa Fe texture will need repainting for the new layout.
