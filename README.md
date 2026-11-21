# GlobeCore

A shared, body-agnostic globe library for WPF apps (.NET 10): it draws a body (the Earth, the Moon, the Sun, Mars) as
a textured globe with image, 3D and screen-space layers on it, plus the plumbing a live-data globe app needs (feeds,
stores, settings, logs). It was taken out of EarthWatch3D (2026-11) so that several apps can share it; apps add it as
a git submodule. Astronomy (orbits, body rotation, sub-solar points) comes from AstroCore.

## Projects

- **`GlobeCore`** (`net10.0`, no UI): bodies (`Body`, `Ellipsoid`, `SurfacePoint`, `SunLit` / `SelfLuminous`), geo
  (GeoJSON lines, date-line splitting, distances, formatting, units), image composition (`GlobeImageComposer` over an
  ordered list of `IImageLayer`s: base, Surface layers, night shade and day/night line, AboveNight layers), feeds
  (`IFeed`, `FeedScheduler`, `FeedHttp` with the app's User-Agent, back-off, a daily download meter), stores, settings,
  storage paths, logging and time.
- **`GlobeCore.Wpf`** (`net10.0-windows`, WPF, HelixToolkit.Wpf): the ellipsoid mesh (`BodyMesh`), camera rules and
  animation, picking (`Projector`, ray/ellipsoid hits), rasterizers, the layer classes and `LayerHost`, shared styles,
  and diagnostics (`CrashHandler`, `SnapshotRenderer`, which saves a window as a PNG through `RenderTargetBitmap`).
- **`GlobeCore.Earth`** (`net10.0-windows`): the Earth pack. `EarthBody` (a sphere of radius 6371 km),
  `EarthAssets` (the embedded NASA Blue Marble, Natural Earth coastlines and borders, and PB2002 plate boundaries,
  resource names `GlobeCore.Earth.Assets.<file>`), `NaturalEarthLines`, `PlateBoundaries`, and the daily cloud layer
  (`GlobeCore.Earth.Clouds`: `GibsCloudsFeed` from NASA GIBS, `CloudLayer`).

Tests are in `tests/GlobeCore.Tests` (`dotnet test`).

## Layers

A `LayerHost` owns the globe in a `HelixViewport3D` and the layers on it:

- `ImageLayer`: draws into the globe texture. Its `Current` image is blended at each compose; `Invalidate()` asks for a
  recompose (throttled to one every 250 ms, with a trailing compose). Composing runs off the UI thread, one at a time;
  if it fails, the plain base image is shown until the next compose.
- `SceneLayer`: owns 3D visuals (`Root`) above the surface, in the body frame (unit equatorial radius); `Update` is
  called on frames when the camera or the layer has changed.
- `OverlayLayer`: draws 2D content on the canvas over the viewport (`Render`).
- `IPickable`: a layer that can be hovered and clicked; the visible one with the highest `ZOrder` that hits wins. The
  host shows its tooltip on hover and raises `Selected` on a click.

The app sets `DisplayTimeUtc` (the globe composes again once it has moved on by a minute) and `Illumination`, or
`IlluminationAt` to work the light out at each compose.

## Adding a body

1. Describe it as a `Body`: an id and name, its `Ellipsoid` (equatorial and polar radii in km;
   `AstroCore.Bodies.BodyConstants.Radii` has them), its rotation (`AstroCore.Bodies.BodyRotations`; add an
   `IauRotationModel` from the IAU elements if it is not there), its heliocentric position (a function of the TT
   Julian date giving J2000 ecliptic AU, such as `PlanetOrbits.Mars.PositionAt`; null means the Earth), its longitude
   convention, and `SunLit(twilightDeg)` or `SelfLuminous` (the Sun: no night side).

   ```csharp
   var (eq, polar) = BodyConstants.Radii("mars");
   var mars = new Body("mars", "Mars", new Ellipsoid(eq, polar), BodyRotations.Mars,
       PlanetOrbits.Mars.PositionAt, LongitudeConvention.EastPositive, new SunLit());
   ```

2. Give it a base texture: an equirectangular `Bgra32Image`, west edge at −180°, north at the top
   (`ImageConversions.DecodeToBgra32` decodes a JPEG or PNG, scaling while it decodes).
3. Create a `LayerHost(viewport, overlay, body, baseImage, log)`, add its layers, set `IlluminationAt`
   (for example `utc => new IlluminationState(body.SubSolarPoint(utc), 0.85, true)`), and `await RecomposeNowAsync()`.

A body's data and assets (like `GlobeCore.Earth`) go in a pack project of their own, so apps take only the bodies
they use.

## Using it from an app: `AstroCoreRoot`

GlobeCore's projects reference AstroCore through the MSBuild property `AstroCoreRoot`. On its own, GlobeCore uses its
own submodule (`external/AstroCore`, set in `Directory.Build.props`). An app that has AstroCore as its own submodule
must build GlobeCore against that copy, so there is one AstroCore in the build. In the app's `Directory.Build.props`:

```xml
<PropertyGroup>
  <AstroCoreRoot>$(MSBuildThisFileDirectory)external\AstroCore\</AstroCoreRoot>
</PropertyGroup>
```

This works because GlobeCore's own `Directory.Build.props` first imports the nearest `Directory.Build.props` above the
GlobeCore folder (the app's, when GlobeCore is the app's submodule), so the app's `AstroCoreRoot` is already set.

The app then needs only `git submodule update --init` (not `--recursive`): GlobeCore's nested AstroCore is not used.

## Credits and licence

The Earth assets and their licences are listed in `src/GlobeCore.Earth/Assets/README.md` and `THIRD-PARTY-NOTICES.txt`
(the plate boundaries need their credit shown). GlobeCore has no licence file yet: the owner will choose one.
