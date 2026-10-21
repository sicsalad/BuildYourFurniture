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
