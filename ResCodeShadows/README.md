# ResCode Shadows for Rhino

Revit-style shadow diagrams and a ResCode overshadowing check for Rhino 7 and 8.

Defaults follow **Clause 55.04-5 Standard B21** (and Clause 54.04-5 Standard A14), Victoria:

> at least 75%, or 40 m² with a minimum dimension of 3 m, whichever is the lesser area, of the
> secluded private open space should receive a minimum of five hours of sunlight between
> 9 am and 3 pm on 22 September. If existing sunlight is already below this, it should not be further reduced.

## Build and install

```bash
dotnet build -c Release
```

Drag `bin/Release/ResCodeShadows.rhp` onto Rhino (or use `PlugInManager` → Install).

## Commands

| Command | What it does |
|---|---|
| `ResCodeShadowSettings` | Opens the settings dialog:<br>• **City** dropdown with VIC regional cities, the capitals and NZ. Picking a city sets latitude, longitude and UTC offset.<br>• **Find on map…** opens an aerial or street map where you search an address, click, or drag the pin.<br>• **Use model's Earth Anchor** reads location and north from Rhino's `EarthAnchorPoint`.<br>• **True north** takes an angle measured clockwise or counter-clockwise from page-up, with a live compass preview, **Pick in model…** (two clicks) and **Straight up**.<br>• Also sets the date, times, diagram interval and the B21 thresholds.<br>The dialog is also available from the **Settings** option at the first selection prompt of `ResCodeShadows` and `ResCodeSunlight`. `-ResCodeShadowSettings` gives the command-line version for scripts. Settings are remembered between sessions. |
| `ResCodeShadows` | Draws the shadow diagrams on the ground plane: one sub-layer per time under **ResCode Shadow Diagrams**. Each sub-layer holds the *Existing shadow* (grey), the *Additional shadow* from the proposal (red), the outline of the proposed shadow, a title, a sun-direction arrow and a legend. There is also one true-north arrow. Each run replaces the previous run's output. |
| `ResCodeSunlight` | B21 / A14 compliance check. Select the closed curves of the neighbouring SPOS. The command ray-traces a grid (0.25 m by default) every 15 min from 9 to 3. It reports the area that gets at least 5 h of sun, existing vs proposed, against the required area, plus a sunlit-area table at each diagram time. It also adds a coloured result mesh: green = meets the hours, red = sun lost because of the proposal, orange = already below the hours. |

### Object sets (asked for by both commands)

1. **Proposed**: the new buildings, plus new fences and walls.
2. **Demolished**: existing buildings on the site that are being removed.
3. **Context**: retained neighbours, boundary fences and anything else that casts shadow in both scenarios. `AllVisible` takes the whole visible model, leaving out anything already picked and the plugin's own output layers.

Don't include sloping terrain in any set. Anything above the base of the proposal casts shadow.

*Existing conditions* = context + demolished. *Proposed conditions* = context + proposed.
Breps, extrusions, meshes, SubDs and block instances (including nested blocks) are accepted.

## Large models: automatic relevance culling

At the **context** prompt, choose `AllVisible` to take every visible solid in the model. You don't need to tidy layers first. Before anything is meshed, each object is tested using only its bounding box:

1. **Out of reach.** The plugin works out the plan extent of each object's possible shadow at every sun position in the time range. If that never overlaps the area that matters, the object is ignored. For `ResCodeShadows` that area is the proposal's own shadow zone, plus 1 m. For `ResCodeSunlight` it is the SPOS being assessed.
2. **On the ground.** Objects whose top is within 50 mm of the base of the proposal are ignored: paving, slabs, flat site surfaces, linework solids.
3. **Too small.** Objects whose largest dimension is under the size limit are ignored. The default is 0.1 m (fittings, fixings). Posts and rails are long, so they are kept.
4. **Enclosed.** Objects sealed inside a closed solid of the same set are ignored, such as furniture or internal walls inside a closed building mass. The test uses 27 sample points.

The command line reports what was kept and why the rest was ignored. Any object whose extents can't be read is kept.

Other speed-ups:
- Existing render meshes are reused instead of re-meshing.
- Each object keeps its own mesh, and the sunlight ray test checks the object's bounding box before testing the mesh.
- `ResCodeSunlight` only tests the objects that can reach each SPOS.

All of this can be switched off in **Settings → Large models**.

## Notes / limits

- Model north versus true north: "page-up" means world +Y in the Top view. The angle works like Revit's *Angle to True North*.
- Map search uses OpenStreetMap Nominatim. Tiles come from Esri and OpenStreetMap, so the map needs an internet connection.
- The UTC offset guessed after a map pick follows state boundaries only roughly (for example, Broken Hill is +9.5). Check it.
- Times are clock times at the given UTC offset. 22 September falls before daylight saving starts in Victoria, so AEST (+10) is correct.
- Diagram shadows land on one flat ground plane (you're asked for its elevation; the default is the lowest point of the model). They don't fall on sloping terrain or across neighbouring roofs. `ResCodeSunlight` does true 3D ray-casting, so its numbers include overhangs and fences.
- The 3 m minimum dimension of the 40 m² area is not tested automatically. Check that the green zone is at least 3 m wide.
- The sun position uses the NOAA algorithm with refraction applied. In Melbourne at noon on 22 Sept it gives 51.7° altitude.
