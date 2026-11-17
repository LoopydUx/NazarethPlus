# NazarethPlusCity

First-person walk through Nazareth on flat ground. Buildings and streets are separate objects generated from the GeoJSON in `Data/`. The playable scene is saved in the project, so Play and a player build do not read the GeoJSON again and do not use elevation or vehicles.

## Engine and scale

- Unity `6000.1.7f1` with the Universal Render Pipeline, Windows desktop.
- **1 Unity unit = 1 meter.** `+X` is east, `+Y` is up, `+Z` is north.
- GeoJSON is WGS84. Coordinates are `[longitude, latitude]`. Source altitude is ignored.
- The ground plane is **Y = 0**. The saved origin is the centroid of the city boundary and is reused on every rebuild (`Assets/NazarethPlusCity/Settings/CityCoordinateFrame.asset`).
- Local positions come from a WGS84 east-north-up tangent at that origin.

## Open and play

1. Open this folder in Unity 6.1 (`6000.1.7f1`).
2. Let the editor import `Assets/NazarethPlusCity`.
3. Press Play. The startup scene is `Assets/NazarethPlusCity/Scenes/NazarethPlusCity.unity`. It is also the first scene in Build Settings.

The player starts on a validated street inside the city boundary, at standing eye height, with movement already active.

## Controls

| Input | Action |
| --- | --- |
| W A S D | Walk |
| Mouse | Look |
| Shift | Sprint |
| Space | Jump |
| Esc | Pause and resume |
| R | Return to the street spawn |

Bindings live in `Assets/NazarethPlusCity/Input/NazarethPlusCity.inputactions`.

## Generate or rebuild

Menu: **NazarethPlusCity → Generate Or Rebuild Full City**.

The generator reads, and does not modify:

- `Data/city_boundary.geojson`
- `Data/buildings.geojson`
- `Data/streets.geojson`

`Data/building_models` is intentionally unused. Every building is an extrusion of its GeoJSON footprint. There is no terrain import and no vehicles.

Rebuild replaces generator-owned objects under the `Nazareth` hierarchy and the meshes in `Assets/NazarethPlusCity/Generated/Meshes`. Hand-authored objects without a `GeneratorOwned` key are left alone. Per-building height and tint overrides, and per-street width overrides, are kept. Transforms of generated objects are placed from the source coordinates again.

**NazarethPlusCity → Toggle Boundary Outline** shows or hides the inspection outline. Blocking collision stays either way. The outline is off for normal play.

After a run, counts and every feature id are in:

- `NazarethPlusCity_ImportReport.txt`
- `Assets/NazarethPlusCity/Generated/ImportReport.txt`

## What gets built

| Object | Role |
| --- | --- |
| `Nazareth/Buildings/Building_<id>` | One object per building feature. A repaired multi-part footprint keeps extra mesh children on that same object. |
| `Nazareth/Streets/Street_<id>` | One object per street feature. Clipped pieces stay on that object. |
| `Nazareth/Ground/CityGround` | Flat earth mesh of the real boundary, including the interior hole. |
| `Nazareth/Boundary/CityBoundary` | Saved polygon, invisible border collision, optional outline. |
| `Nazareth/Environment` | Sun, daylight volume, street spawn, first-person walker. |

Each building and street keeps its source id, display name, and original properties. Height resolution is `ExplicitHeight`, `FloorCount`, or `Fallback`.

Stable ids come from the GeoJSON feature `id`, then `@id`, then `id`. Labels replace characters other than letters, digits, `_`, and `-`.

## Defaults

Editable on `Assets/NazarethPlusCity/Settings/CityGenerationSettings.asset`:

- Floor height **3 m**. Fallback **3 floors / 9 m** when height and floor count are both missing or unusable.
- Explicit `height` wins. Otherwise `building:levels`, otherwise `number_of_floors`.
- Lengths accept meters, feet, and ranges such as `3-5m` (the midpoint is used). A bare number is meters.
- Ordinary roads **6 m**. Service/track **4 m**. Footways, paths, and steps **2.2 m**. Lane fallback is **3.4 m** per lane. Tertiary **7**, secondary **8**, primary **10**, trunk **12**.
- Road surfaces sit about **3.5 cm** above the ground, with a tiny per-street lift so intersections do not flicker.
- Border walls are **6 m** tall and **0.45 m** thick, extended **0.35 m** at corners, embedded **1 m** below ground. They are invisible.
- Walker: walk **4.2 m/s**, sprint **7.5 m/s**, jump **1.2 m**, eye height **1.64 m**, capsule **1.8 m × 0.3 m radius**.
- Containment backup uses the capsule radius minus an **8 cm** safety margin. Falling below **Y = -1.5** returns the player to the last grounded point inside the map, or to the street spawn.

On a building, enable **Use Height Override** or **Use Tint Override** and the mesh or tint updates in the editor. On a street, enable **Use Width Override**.

## Appearance and performance

Shared URP Lit materials: limestone walls (six restrained tints), concrete roofs, dark asphalt, muted earth. The scene has a procedural blue sky, a directional sun, skybox ambient light, and a neutral tonemap volume. Shadows are realtime.

Generated city objects are not moved by scripts and have no per-frame gameplay code. They stay separate objects. They are not combined into one city mesh or an instanced stand-in. The camera far clip is 4500 m and the sun shadow distance is 150 m, so distant geometry is culled while the objects still exist.

## Packages used for geometry

- Clipper2 (`Assets/NazarethPlusCity/Plugins/Clipper2`, Boost license) offsets and clips street surfaces and can repair a non-simple footprint.
- Mapbox earcut (`Assets/NazarethPlusCity/Scripts/Runtime/Earcut.cs`, ISC license) triangulates roofs, ground, and road surfaces, including holes.

## Verification

Checked with Unity `6000.1.7f1` in batch mode. The full report is `NazarethPlusCity_ImportReport.txt`.

- Boundary features: 1. Buildings: 6261 generated, 0 skipped (459 explicit heights, 1 floor count, 5801 at the 9 m fallback). Streets: 1292 generated, 0 skipped, 0 fully clipped.
- 6247 buildings and 1185 streets are inside the boundary. 14 buildings and 107 streets cross it; each id is listed and the source geometry was kept. None were outside or in the hole.
- Projected distance is 4499.1 m against a 4513.4 m haversine (ratio 0.9968). North increases Z and east increases X.
- Spawn is `(-7.3, 0.0, 33.5)`, yaw 98, on a clear street. Twelve sampled roofs match their resolved height.
- Border probes, including a 7.5 m/s sprint and a 1.2 m jump: a 369 m straight edge, a concave corner, and a 101 m hole edge all stopped the capsule. Standing and jump-height sweeps hit the invisible border. Falling to Y -4.5 and a placement outside the polygon both returned the walker to the street spawn.
- A second full rebuild produced 6261 buildings and 1292 streets again, with no duplicate objects.
- `StreetLevelView.png` and `OverviewView.png` show a blue sky, brown ground, beige stone walls, gray roofs, dark roads, and shadows.
- The saved scene is build index 0. Pressing Play loads that scene and does not read the GeoJSON again.

A standalone Windows player was not packaged, and mouse-look was not driven in a live Play Mode session. The walk, sprint, and jump checks used the saved character controller and border colliders in the editor.
