# 3D Printing Basics

## How FDM Printing Works

Fused Deposition Modeling (FDM) is the most common consumer 3D printing technology. A thermoplastic filament (typically 1.75mm diameter) is fed through a heated nozzle (hotend) that melts the plastic and deposits it layer by layer onto a build plate. The nozzle moves in the X and Y axes while the build plate (or gantry) moves in Z, building the object from the bottom up. Each layer is typically 0.1-0.3mm thick — thinner layers produce smoother surfaces but take longer to print.

The most popular FDM filaments are PLA (polylactic acid), PETG, and ABS. PLA prints at 190-220°C, is biodegradable, and is the easiest to work with — it warps less and doesn't require an enclosed chamber. PETG is stronger and more heat-resistant (printing at 230-250°C) and is food-safe when properly printed. ABS requires a heated bed at 100-110°C and an enclosure to prevent warping and layer splitting from thermal contraction.

## Bed Leveling and First Layer

The first layer is the foundation of every print. If the nozzle is too far from the bed, the filament won't stick. Too close, and it squishes flat or blocks the nozzle entirely. Manual bed leveling uses a piece of paper as a feeler gauge — slide it between the nozzle and bed at each corner, adjusting the leveling knobs until you feel slight friction.

Automatic bed leveling (ABL) probes like BLTouch or inductive probes measure the bed surface at multiple points and create a mesh compensation map. The firmware adjusts Z height in real-time during printing. ABL doesn't replace a reasonably level bed — it compensates for minor imperfections, typically under 0.5mm of variance.

### Bed Adhesion

Common adhesion methods include:

- **PEI sheet**: Textured or smooth spring steel sheet coated with polyetherimide. Parts stick when hot and release when cool. The gold standard for most filaments.
- **Glue stick**: Apply a thin layer of washable glue stick (PVA-based) to glass or smooth surfaces. Cheap and effective.
- **Painter's tape**: Blue painter's tape works well for PLA on unheated beds.
- **Brim and raft**: Slicer settings that add extra material around the base. A brim adds a single-layer border for grip; a raft prints a full sacrificial platform underneath.

## Slicer Software

A slicer converts a 3D model (STL, 3MF, or OBJ file) into G-code — the machine instructions the printer follows. Popular slicers include PrusaSlicer, Cura, and OrcaSlicer. Key slicer settings:

- **Layer height**: 0.2mm is the standard balance of speed and quality. Use 0.12mm for detailed models, 0.28mm for fast drafts.
- **Infill**: The internal fill pattern and density. 15-20% gyroid or grid infill is standard. Increase to 40-60% for structural parts. 100% infill is rarely needed and wastes material.
- **Print speed**: 40-60 mm/s is safe for most printers. Modern printers with input shaping (Klipper firmware) can print at 150-300 mm/s without quality loss.
- **Support structures**: Material printed under overhangs greater than 45° to prevent drooping. Tree supports are easier to remove than grid supports but take longer to generate.
- **Retraction**: Pulls filament back when the nozzle travels between printed areas. Prevents stringing (thin threads between parts). Typical settings: 1-2mm retraction distance at 30-45mm/s for direct drive, 4-7mm for Bowden setups.

## Resin Printing (MSLA)

Masked Stereolithography (MSLA) uses an LCD screen to selectively cure liquid photopolymer resin with UV light. Each layer is exposed all at once, making print time dependent on height rather than the number of objects on the plate. Resin printing achieves much finer detail than FDM — layer heights of 0.025-0.05mm are common, making it ideal for miniatures, jewelry, and dental models.

### Safety Considerations

Uncured resin is toxic and a skin sensitizer. Always wear nitrile gloves when handling resin or prints before curing. Work in a ventilated area or use a printer with a built-in carbon filter. Wash prints in isopropyl alcohol (IPA) or a water-washable resin cleaner, then post-cure under UV light for 5-10 minutes. Never pour uncured resin down the drain — cure it with sunlight first, then dispose of it as solid waste.

## Troubleshooting Common Issues

- **Stringing**: Thin threads between travel moves. Increase retraction distance and speed. Reduce hotend temperature by 5°C increments.
- **Layer shifting**: Layers misalign in X or Y. Usually caused by loose belts, excessive print speed, or stepper motor overheating. Tighten belts and reduce acceleration.
- **Elephant's foot**: The first few layers bulge outward. Reduce bed temperature by 5°C or add a small negative Z-offset.
- **Warping**: Corners lift off the bed. Ensure the bed is hot enough, use an enclosure for ABS, and add a brim in the slicer.
- **Under-extrusion**: Gaps in walls or sparse infill. Check for a clogged nozzle, increase flow rate, or raise hotend temperature.

## Post-Processing

FDM prints can be sanded smooth starting with 120-grit and progressing to 400-grit. Filler primer spray fills layer lines before painting. For ABS, acetone vapor smoothing dissolves the surface into a glossy finish — suspend the print over a small amount of acetone in a sealed container for 15-30 minutes. PLA cannot be acetone-smoothed but responds well to UV-cured resin coating for a glossy surface.

For functional parts, consider annealing PLA in an oven at 70-80°C for 30-60 minutes. This crystallizes the polymer structure, increasing heat resistance and stiffness by 20-40%, but causes slight dimensional changes — print tolerances should be tested.
