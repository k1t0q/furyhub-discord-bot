# SCUM HUB offline navigator

This development branch does not replace the Discord mini-map download.

The navigator reads the existing HUB marker list. Choose a marker and press
«Проложить маршрут». A solid purple line follows roads; dashed segments are
the approaches between the player/destination and the road. The footer shows
sector and remaining route distance. Arrival within 30 metres removes the line.
Deviation beyond 60 metres triggers local replanning, at most once in two seconds.
Marker refresh remains manual. Deleting the destination cancels its active route;
editing destination coordinates replans after a marker refresh.

No routing API, game memory access, injected game code, or extra position uploads
are introduced. Existing position tracking and ally sharing are unchanged.
The route is not published to other players.

Road geometry is pinned by Git blob SHA; provenance and license text are embedded
in the executable as NAVIGATION-NOTICES.txt. The graph compiler adds only short
junction joins (at most 6m). It never joins distant disconnected roads. A raster
water mask checks off-road approaches. Buildings, fences, slopes, temporary
obstacles and server-specific road closures are not represented.

## Build on Windows

Install Python with Pillow, NumPy and SciPy. Run `Build-Navigator.ps1` from this
directory. It assembles the existing map, downloads the pinned public road
geometry, compiles the graph, embeds resources, builds the .NET Framework
executable and runs self-tests. End users need only `SCUM_HUB_Navigator.exe`.

## Validation

Self-tests cover road-only detours, partial edge routing, disconnected networks,
connected access roads near isolated spurs, off-road distance, water rejection,
arrival, cancellation races, automatic rerouting and edited destinations.
`--route-check x y goalX goalY` exports a route in normalized map coordinates
to `route-check.json` for visual review. It does not connect to HUB.

The supplied build was tested under Mono on Linux and route geometry was visually
checked against the bundled map. Native Windows/SCUM interaction still requires
an in-game test.
