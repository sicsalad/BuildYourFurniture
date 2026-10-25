# Szekrény készítő – XML cabinet designer (initial implementation)

Open from **BuildYourFurniture > Szekrény készítő** in the Unity Editor.

The left inspector edits the cabinet name, named dimensions, colors, material records, and individual elements. The right preview shows the current polygon panels in 3D. Click a panel to select it; drag with the active **Move**, **Resize** or **Rotate** mode, or use **Orbit** / right mouse to orbit the camera; wheel zooms. Numerical X/Y/Z positions and rotations are editable in the inspector in millimetres/degrees. Add, duplicate and remove panel elements.

Cabinets are persisted to `Assets/CabinetDesigns/<id>.xml`. **Új**, **Megnyitás**, **Mentés**, **Duplikálás**, **Törlés** are provided in the toolbar. This new XML schema is independent of the existing runtime `CabinetTypes/*.xml` assembly format; the runtime cabinet renderer does not yet consume `cabinetDesign` files.

Each panel stores a planar finished polygon (`outline`), thickness (default 18 mm), per-edge band thickness, material reference, position and XYZ Euler rotation. Individual outline point coordinates may reference named cabinet dimensions through expressions such as `$width`, `$depth-18`. A new default rectangular panel binds its right edge to `$width` and its far edge to `$depth`. The inspector calculates the raw core cut outline by inward edge offsets; the finished exterior remains unchanged when banding is toggled.

**Known limitations:** 3D preview currently draws the finished closed solid in one material, not separate textured wood-core and band-strip meshes. Editing raw point coordinates does not override an active expression on that coordinate; clear the expression to edit manually. Mouse manipulation is a first-pass viewport interaction (screen-delta translation, Y rotation, uniform planar resize), not yet full axis-constrained transform handles. The editor has not been compiled or tested in a Unity Editor environment from this session. Existing runtime cabinet types are not migrated automatically.
