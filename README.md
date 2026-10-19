# BuildYourFurniture

Unity-based XML-driven 2D/3D kitchen and furniture planning prototype.

## Setup
1. Open this repository as a Unity project after adding/retaining a compatible Unity `Packages/manifest.json` and `ProjectSettings` from your locally created Unity project.
2. Create an empty scene and an empty GameObject; attach `FurnitureApp`.
3. Play. The UI is generated from `Assets/Resources/UI/*.xml`.
4. A sample 2660 x 2200 mm room is loaded from `Assets/Resources/Plans/sample.xml`.
5. Import custom plans from the main menu (Unity Editor or Windows standalone). Navigate 3D with WASD, Q/E, right mouse and wheel.
6. The materials screen exports a semicolon-delimited UTF-8 CSV to `Application.persistentDataPath` (not XLSX).

## Architecture
- All screens start empty; controls are instantiated from XML by `FurnitureApp`.
- Plan XML defines room polygons, walls, dimensions, and texture metadata.
- Geometry currently creates simple white wall cubes and floor bounding rectangles. Texture image tiling, 2D plan editor, furniture modeling, true spreadsheet UI, native Save dialog and VR are future work.
- For screen button pictures, add Sprite assets under `Assets/Resources/UI/upload.png`, `view3d.png`, `materials.png`. Buttons currently use colored fallback backgrounds.
- `CityTrack`'s `ServiceHost` is reused separately under `Assets/Scripts/Core/Services`; avoid importing CityTrack-specific AR/image matcher subsystems.

## Limitations
This is a first prototype; Unity compilation and runtime have not been verified in this environment. Do not replace existing Unity project settings or packages with CityTrack's settings.
