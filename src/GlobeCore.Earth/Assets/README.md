# Assets

Prepared by EarthWatch3D's `tools/prepare-assets.ps1`, which now writes into the GlobeCore submodule (`external/GlobeCore/src/GlobeCore.Earth/Assets`) instead of the app. The files are public domain or free to use (the plate boundaries need the credit below).

| File | Source | Licence |
|---|---|---|
| `bluemarble-8192.jpg` | NASA Earth Observatory, Blue Marble Next Generation with Topography and Bathymetry (July 2004, R. Stockli), `https://assets.science.nasa.gov/content/dam/science/esd/eo/images/bmng/bmng-topography-bathymetry/july/world.topo.bathy.200407.3x21600x10800.jpg`, resized to 8192x4096 | NASA imagery, generally not copyrighted (NASA Media Usage Guidelines) |
| `ne_50m_coastline.geojson` | Natural Earth, `https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_coastline.geojson` | Public domain |
| `ne_50m_admin_0_boundary_lines_land.geojson` | Natural Earth, `https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_admin_0_boundary_lines_land.geojson` (760,189 bytes). Checked 2026-11-22: its git blob (`f2da10de881015c7182951b59a75b1e4e27356c6`) is the one in tag v5.1.2's `geojson/` tree, so it is the 5.1.2 edition, like the countries file. Read by `NaturalEarthBoundaries` | Public domain |
| `plate_boundaries.geojson` | Peter Bird (2003), An updated digital model of plate boundaries, G³ 4(3):1027 (PB2002); GIS data by Hugo Ahlenius, Nordpil, `https://raw.githubusercontent.com/fraxen/tectonicplates/master/GeoJSON/PB2002_steps.json`. The 5,824 steps are merged into polylines by kind and simplified to 0.01° | ODC Attribution 1.0 (ODC-By): credit Bird (2003) and Hugo Ahlenius, Nordpil |
| `ne_50m_admin_0_countries.geojson` | Natural Earth 5.1.2 admin-0 countries, `https://raw.githubusercontent.com/nvkelso/natural-earth-vector/v5.1.2/geojson/ne_50m_admin_0_countries.geojson` (unmodified, 3,083,490 bytes, SHA-256 `3e458fc036ad0a66411f2c1e6cac49c5d7bfb81cb1123bc513b22511a2b7fdeb`). Read by `NaturalEarthBoundaries` | Public domain |
| `ne_50m_admin_1_crimea_sevastopol.geojson` | Natural Earth 5.1.2 admin-1 states and provinces (1:50m), `https://raw.githubusercontent.com/nvkelso/natural-earth-vector/v5.1.2/geojson/ne_50m_admin_1_states_provinces.geojson` (2,325,694 bytes, SHA-256 `69a0e06e640b2d505858ae1cb63034e4677f3000b35a98e16312932b98c426b9`). Extract: the two features with `iso_3166_2` UA-43 (Crimea) and UA-40 (Sevastopol), geometry unchanged, properties cut to `name, name_en, iso_3166_2, adm0_a3, admin, type_en, ne_id` (2,536 bytes, SHA-256 `78984719dd109caa4191b0308edac92dd8aed63a5d13309e982e1da4e216a76b`). The bundled set's admin-1 file (a downloaded set has the full file), read by `NaturalEarthBoundaries` | Public domain |

## Boundary sets

A **boundary set** (`GlobeCore.Regions.BoundarySet`) is one Natural Earth edition's countries polygons, land boundary
lines and the admin-1 polygons its rules need, named by its edition, e.g. "Natural Earth 5.1.2 (2022-05-13)" (the
release tag and date). `NaturalEarthBoundaries` reads them: `ReadBundled()` gives the embedded 5.1.2 files above,
`Parse(...)` a stored or downloaded release (`ne_50m_admin_0_countries.geojson`,
`ne_50m_admin_0_boundary_lines_land.geojson`, `ne_50m_admin_1_states_provinces.geojson`), and `Load(rules, store,
validator, log)` starts with the newest validated stored set newer than 5.1.2, or else the bundled one.

Natural Earth properties map to a `Region` as: id `ISO_A3_EH` (or `ADM0_A3` where it is `-99`), name `NAME`, other names
`NAME_LONG`, `FORMAL_EN`, `ADMIN` (in that priority), label point `LABEL_Y`/`LABEL_X`. Admin-1 features map to
subdivisions: code `iso_3166_2`, parent `adm0_a3`, name `name`. Names the file lacks (Türkiye, Czechia, Cabo Verde,
Timor-Leste, Burma, Ivory Coast, Swaziland) are built-in aliases.

## Boundary rules (data, supplied by the app)

An app's decisions about boundaries are a rules file (`boundary-rules.json`) applied at every load, to the bundled set
and to every downloaded one, so they survive updates. `BoundaryRules.Parse(stream)` reads it and `Apply(raw)` builds the
set. Every rule must give a `reason`; a rule with none is rejected. If a rule does not match a set exactly, the whole set
is rejected with `BoundaryRuleException` ("rule moveAdmin1 UA-43 no longer matches: ..."), never half-applied.

```jsonc
{
  "rules": [
    {
      "kind": "moveAdmin1",                // move admin-1 units from one country to another
      "name": "moveAdmin1 UA-43",          // optional; default: the kind and the first code
      "codes": ["UA-43", "UA-40"],         // Crimea, Sevastopol (iso_3166_2)
      "from": "RUS",
      "to": "UKR",
      "expectParts": 1,                    // optional: exactly this many RUS parts must be removed
      "expectLines": 1,                    // optional: exactly this many land boundary lines must be removed
      "description": "Crimea shown as part of Ukraine (UN General Assembly resolution 68/262)",
      "reason": "UN practice: UN General Assembly resolution 68/262 (27 March 2014), Territorial integrity of Ukraine"
    },
    {
      "kind": "mergeUnits",                // a unit's polygons join another; its names resolve to it
      "units": ["SOL"],
      "into": "SOM",
      "description": "Somaliland counted with Somalia",
      "reason": "Somaliland is not a separate unit in the UN humanitarian sources (C16)"
    },
    {
      "kind": "alias",                     // extra names (any spelling) for countries
      "names": { "DR Congo (Zaire)": "COD" },
      "reason": "UCDP's spelling"
    }
  ]
}
```

- **`moveAdmin1`**: every code must be exactly one admin-1 feature whose `adm0_a3` is `from`; `from` and `to` must each
  be exactly one country. The parts of `from` whose outer-ring vertices are all vertices of those admin-1 polygons, with
  the same area (within 1%), are removed (at least one, or exactly `expectParts`); the admin-1 polygons are added to
  `to`, after its own parts; and the land boundary lines all of whose points are those vertices (the units' own
  boundary, now internal, like the Perekop line) are removed (exactly `expectLines` when given).
- **`mergeUnits`**: every unit and `into` must be in the set. A chain (A into B, B into C) ends at C.
- **`alias`**: every target must be in the set. Aliases replace the file's own names and the built-in aliases.
- `description` is the text shown on screen with the edition (`BoundarySet.Describe()`,
  `BoundaryRules.Descriptions`). It is required for `moveAdmin1` and `mergeUnits`, which change the map (a rule
  without one is rejected), and optional for `alias`, which changes only name matching (one without is not listed).
- Order: all `moveAdmin1` rules first, in file order, on Natural Earth's geometry; then the polygons are split at ±180°;
  then `mergeUnits` and `alias` rules in file order. Comments and trailing commas are allowed; unknown kinds or keys
  are rejected.

`BoundaryValidator` checks a downloaded set before it is used (the rules apply, the country count is within ±10% of the
bundled set's, every probe point lands in its country, every name the app's sources use still resolves).
`BoundaryValidator.ReadProbes` reads `{"probes": [{"name": "Khartoum", "lat": 15.5, "lon": 32.53, "expect": "SDN"}]}`.

### No built-in rules

GlobeCore applies no rules of its own. The transitional built-in copy of the Crimea rule, and the parameterless
`CountryIndex.LoadNaturalEarth()` and `NaturalEarthLines.LandBoundaries()` that used it, were removed (Plan 2b, ruling
R6) once both apps passed their own `boundary-rules.json`. Callers pass the app's rules: `CountryIndex.LoadNaturalEarth(stream)`,
`CountryIndex.From(set)`, `NaturalEarthLines.LandBoundaries(stream or set)`.

## Crimea and Sevastopol (UN practice)

The apps show Crimea and Sevastopol as part of Ukraine (the `moveAdmin1 UA-43` rule above, in each app's rules file),
as the United Nations does (UN General Assembly resolution 68/262 of 27 March 2014, "Territorial integrity of Ukraine";
EarthRelief3D ruling R15). Natural Earth's default admin-0 file draws them inside Russia.

Natural Earth 5.1.2's point-of-view (POV) variants were checked first and not used:

- They exist only at 1:10m (`10m_cultural/ne_10m_admin_0_countries_<pov>.shp`); there is no 1:50m POV file at tag
  v5.1.2 (the `50m_cultural` and `geojson` folders hold only the default countries files).
- The Ukrainian POV (`ne_10m_admin_0_countries_ukr`, .shp SHA-256 `ec923b81a27e36efce7bb293c45ce87134f967330ef137d04e1be9c4940b6d82`)
  differs from the default 10m file (.shp SHA-256 `7ce119ef6342e43cff7c0c3004e0911ab7ec1988a14734372031d2012180e7bc`)
  far beyond Crimea. Geometry differs for 17 ADM0_A3 units: BRA, CHN, COL, CYP, GRL, IND, ISR, KAZ, KOR, LBN, MAR,
  RUS, SAH, SDN, SOM, SRB, UKR; and 9 units are absent (merged into others): BJN, CNM, CYN, KAB, KAS, KOS, SER, SOL, SPI.
  The ISO POV (`_iso`) differs in 35 units and drops 19. Several of these are one government's view, not UN practice.

So the default 1:50m file stays unmodified and the rule is applied as it is read:

1. The union of Natural Earth's own 1:50m admin-1 polygons for Crimea and Sevastopol (the extract above) is exactly
   one part of Russia's admin-0 multipolygon in `ne_50m_admin_0_countries.geojson` (the last of its 101 parts): all 72 of that
   part's vertices are vertices of the two admin-1 polygons, and the planar areas agree (3.10178 square degrees against
   3.02459 + 0.07718). That part is dropped from RUS, and the two admin-1 polygons are added to UKR.
2. On the land-boundary lines, the one line all of whose 15 points are vertices of the Crimea polygon (the Perekop
   isthmus, `FEATURECLA` "Disputed (please verify)", `NE_ID` 1746708787; Natural Earth's own Ukrainian view, `FCLASS_UA`,
   classes it "Admin-1 boundary") is left out. The Kerch Strait line (`FCLASS_UA` "International boundary (verify)")
   stays.

With `expectParts` and `expectLines` of 1, the rule rejects a set (`BoundaryRuleException`) where the files no longer
match (not exactly one part, admin-1 feature or line), so a changed or updated file cannot silently put Crimea back in
Russia. Tests: `BoundaryRulesTests.CrimeaRuleMovesAdmin1AndDropsPerekop`, `.MismatchedRuleRejectsTheSet`,
`CountryIndexTests.CrimeaAndSevastopolAreUkraine`, `.CrimeaRuleMovesExactlyNaturalEarthsAdmin1Polygons`,
`EarthAssetsTests.LandBoundariesHaveNoLineBetweenCrimeaAndUkraine`.

## Updates (Natural Earth releases)

`BoundaryUpdateFeed` (feed "Borders", every 24 h) with `NaturalEarthBoundarySource` asks the GitHub API for the latest
release of `nvkelso/natural-earth-vector` (ETag-aware). Only a newer tag downloads the three `geojson/` files
(`ne_50m_admin_0_countries`, `ne_50m_admin_0_boundary_lines_land`, and the **full** `ne_50m_admin_1_states_provinces`),
applies the app's rules, validates, stores the set under `<data>/cache/boundaries/<tag>/` and swaps it into
`CurrentBoundaries`. A rejected release leaves the current set in place and shows the "Borders" dot amber with the reason.

- Adopting a release downloads about 6.2 MB (countries 3.08 MB, lines 0.76 MB, admin-1 2.33 MB at v5.1.2), once per
  release; the daily check is a few KB (and an unchanged answer, HTTP 304, has no body).
- Anything that goes wrong while a downloaded set is read, built or checked is a rejection, never a retry, so a bad
  release is not downloaded again and again.
- A rejection is remembered in `<data>/cache/boundaries/rejected.json` with a fingerprint
  (`BoundaryUpdateFeed.Fingerprint`: SHA-256 of the app's rules and probes file bytes and GlobeCore's informational
  version). A remembered rejection with another fingerprint, or none, is forgotten, so an app update that fixes its
  rules, or a GlobeCore update, looks at the release again. There is no expiry.
- After a failed download of the files (network error, timeout, 5xx), the files are not downloaded again for 6 hours
  (`BoundaryUpdateFeed.DownloadBackoff`, ruling R5). The release check keeps running; until then it reports the same
  failure with "next try after" a local time.
