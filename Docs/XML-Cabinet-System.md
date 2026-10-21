# XML-driven cabinet definitions (v1)

- `Assets/Resources/CabinetTypes/{normal,empty,corner}.xml` declare parameters, materials and individual parts.
- `Assets/Resources/Plans/sample.xml` contains cabinet instances. Set `typeId="corner"` to use the XML renderer. Cabinets without `typeId` retain the older specialized renderer for drawers, ovens and appliances.
- Element dimensions are **finished dimensions in mm**, including edge banding. `CabinetTypeEngine.CutSize` returns the raw cutting dimensions after subtracting edge bands.
- `visibility="internal|external"` describes manufacturing visibility, not a Unity rendering visibility toggle. All physical elements are rendered.
- Part formulas support `$parameter`, +, -, *, / and parentheses. Example: `width="$width-2*$boardThickness"`.
- Override a specific part with `<elements><element id="LEFT" color="#FF0000" materialId="body"/></elements>` inside the cabinet instance.
- `<position mode="plan" roomId="R01"><plan x="100" y="200"/><room x="100" y="200"/></position>` fixes plan coordinates; use `mode="room"` to fix room-local coordinates instead.
- `<orientation mode="rotation" rotation="90"/>` fixes rotation; `mode="alignment"` plus `<alignment cabinetId="F01" rotationOffset="90"/>` fixes relative orientation.
- `<docking active="true" cabinetId="F01" targetElement="LEFT" targetFace="left" element="RIGHT" face="right" offsetX="0" offsetY="0"/>` makes docking authoritative. `active="false"` leaves position authoritative. Faces: left/right/front/back/center.
- `CabinetPlacementEngine.Resolve` computes dependent positions, detects cycles and updates the `x`, `y`, `rotation` and both coordinate forms. XML editor saves those calculated values to disk.
- In Unity Editor the XML editor saves into `Assets/Resources`. Standalone builds save under `Application.persistentDataPath/XML`. Edited cabinet types and sample plans are loaded from that location in standalone.
- The XML editor validates XML syntax and root names; it does not yet validate every geometric constraint or produce a complete manufacturing BOM.
- The corner model currently uses rectangular pieces to represent the L-shaped base; it is not yet a single CNC-ready polygon, and its joint clearances must be validated before manufacturing.
- The legacy normal and empty cabinet renderers are preserved for existing section/appliance support. Their side panels now stand on the bottom base for normal cabinets.


## Manufacturing and validation (new)

- Normal and empty cabinet **structural parts** are now generated through their `typeRef` XML definitions; section fronts, appliances, and drawers still use the existing specialized detail renderer.
- The empty cabinet inherits normal and disables BASE and PLINTH; its side panels and back reach the floor.
- The `export` UI action now also writes `szabasjegyzek.csv` to `Application.persistentDataPath`. Each row contains cabinet/element/material IDs, finished dimensions, edge thicknesses, raw cut dimensions and visibility metadata.
- The XML editor has a `Geometria ellenőrzés` button and evaluates geometry during save. The Unity Editor menu `BuildYourFurniture/Validate Cabinet System` runs self-tests and logs warnings.
- Geometric checks include positive part and raw cut sizes, side-to-base vertical contact, cabinet total height, corner wing dimensions, and cyclic docking references.
- **Important manufacturing limitations:** this is an initial cut-list and validation pipeline, not certified CNC output. Corner base is currently modeled as two rectangles rather than one true L-shaped polygon. Physical interference, cabinet-to-wall clearances, drawer hardware, and every cross-cabinet collision are not fully checked. Inspect and verify all dimensions before cutting material.
- Tests have been added as a Unity Editor menu command but have not been executed in an actual Unity installation in this environment.


## Constraint-driven assembly v2

The previous implementation is preserved on branch `backup/pre-constraint-xml-engine-20261022`; ongoing work stays on `main`.

Structural parts now support declarative `<placement><attach my="Bottom" to="BASE.Top" x="..." y="..." z="..."/></placement>` constraints. The solver resolves referenced parts first, detects cycles, and calculates the local part center. Face names: `Center`, `Bottom`, `Top`, `Left`, `Right`, `Front`, `Back`. Named anchors are declared on a part using `<anchors><anchor id="InnerCorner" x="..." y="..." z="..."/></anchors>`. Anchor coordinates are local to the referenced part's origin. For polygon panels, `Top` is measured from the underside.

The corner base is now a generic `polygonPanel` with six XML outline vertices and named `InnerCorner`, `LeftWingEnd`, `RightWingEnd` anchors. R1/R2 are attached to `BASE.InnerCorner`; both end panels and all K/S braces are attached to the base, with parametric offsets. The normal cabinet's sides, back and braces are attached to its base. The empty machine cabinet inherits the normal type and explicitly overrides the sides/back with `<placement mode="absolute"/>` so they reach the floor.

The same resolved positions are used for 3D rendering and the manufacturing part list. Unity Editor self-tests now include corner anchor checks.

**Scope note:** This is a single-reference anchor/face constraint engine; multi-constraint mating, general 3D rotations of individual parts, edge-by-edge polygon banding, automated hardware design, and full collision/clearance solving are not yet implemented. The existing specialized section, drawer and appliance renderers are retained. Run `BuildYourFurniture > Validate Cabinet System` in Unity; runtime execution has not been independently verified here.


## Corner cabinet v3: finished-size structural definition (2026-10-22)

`Assets/Resources/CabinetTypes/corner.xml` was replaced with a new nine-part structural model: BASE, LEFT, RIGHT, R1, R2, K1, K2, S1, S2. Instance dimensions are **finished outside sizes including edge bands**. The sample F04 remains 950 × 950 mm in plan, with left/right depths 560/590 mm, height 860 mm including 80 mm plinth, board 18 mm, band 2 mm.

The BASE is a **single 18 mm thick polygon**, with six finished-outline vertices P1..P6 and six individually specified edge bands. `PolygonCutGeometry.Raw` offsets each edge inward by its band thickness and intersects the adjacent offset lines to calculate the **raw six-vertex cut contour**. CSV manufacturing export includes a new `RawPolygonXZ_mm` column; this is a polygon outline, not a rectangular saw size.

The finished body height is 780 mm; sides finish at 762 mm (raw cut height 760 mm when top band is 2 mm), yielding total finished height 80+18+762=860 mm. R1/R2 finish at 662 mm tall and sit on the base; K1/K2 upright braces finish at 100 mm tall and rest above them. The finished lengths of K1, K2, S1, S2 are respectively 930, 914, 914, and 372 mm. These are finished lengths; banding reductions apply only to cut sizes.

Validation now checks finished L bounding dimensions, 18 mm base thickness, side-to-base contact, side top height, element envelope and eight key face contacts. Unity Editor self-tests assert the 950 mm envelope and S2 length. These checks have **not been executed in Unity** here.

**Limitations:** The validation is based on axis-aligned bounding faces, not full mesh Boolean collision testing. The corner drawer remains a legacy special renderer and is not yet a complete XML-described drawer-box/runner assembly. Optional plinth is represented by the instance height offset but not yet a fully parameterized set of manufacturing panels. Old editable `corner.xml` copies in `Application.persistentDataPath/XML/CabinetTypes/` override the packaged Resources version; remove/migrate the saved copy before testing v3.
