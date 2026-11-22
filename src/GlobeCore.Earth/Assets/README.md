# Assets

Prepared by EarthWatch3D's `tools/prepare-assets.ps1`, which now writes into the GlobeCore submodule (`external/GlobeCore/src/GlobeCore.Earth/Assets`) instead of the app. The files are public domain or free to use (the plate boundaries need the credit below).

| File | Source | Licence |
|---|---|---|
| `bluemarble-8192.jpg` | NASA Earth Observatory, Blue Marble Next Generation with Topography and Bathymetry (July 2004, R. Stockli), `https://assets.science.nasa.gov/content/dam/science/esd/eo/images/bmng/bmng-topography-bathymetry/july/world.topo.bathy.200407.3x21600x10800.jpg`, resized to 8192x4096 | NASA imagery, generally not copyrighted (NASA Media Usage Guidelines) |
| `ne_50m_coastline.geojson` | Natural Earth, `https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_coastline.geojson` | Public domain |
| `ne_50m_admin_0_boundary_lines_land.geojson` | Natural Earth, `https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_admin_0_boundary_lines_land.geojson` | Public domain |
| `plate_boundaries.geojson` | Peter Bird (2003), An updated digital model of plate boundaries, G³ 4(3):1027 (PB2002); GIS data by Hugo Ahlenius, Nordpil, `https://raw.githubusercontent.com/fraxen/tectonicplates/master/GeoJSON/PB2002_steps.json`. The 5,824 steps are merged into polylines by kind and simplified to 0.01° | ODC Attribution 1.0 (ODC-By): credit Bird (2003) and Hugo Ahlenius, Nordpil |
| `ne_50m_admin_0_countries.geojson` | Natural Earth 5.1.2 admin-0 countries, `https://raw.githubusercontent.com/nvkelso/natural-earth-vector/v5.1.2/geojson/ne_50m_admin_0_countries.geojson` (unmodified, 3,083,490 bytes, SHA-256 `3e458fc036ad0a66411f2c1e6cac49c5d7bfb81cb1123bc513b22511a2b7fdeb`). Read by `CountryIndex` | Public domain |
