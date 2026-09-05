# IntraLayout Studio

A pure C#/.NET 8, read-only DWG/DXF viewer optimized for low memory use and
smooth CAD navigation. It has no drawing or editing features.

## Rendering architecture

- Direct3D 11 through SharpDX.
- One immutable, flattened GPU line-list vertex buffer per drawing.
- Vertices are compact 12-byte structs: X, Y, and packed RGBA.
- Geometry is sorted by layer; layer visibility changes issue or skip draw
  ranges without rebuilding the vertex buffer.
- Rendering is demand-driven at a 16 ms cadence and presented with VSync.

## CAD projection

ACadSharp reads DWG and DXF files. The display projector keeps only:

- Line
- Arc
- Circle
- LWPolyline, including bulge arcs
- INSERT and MINSERT

True color, ACI, ByLayer, and inherited ByBlock colors are resolved before GPU
upload. Dynamic inserts prefer AutoCAD's evaluated `*U` representation and
ambiguous visibility-controlled master blocks are skipped. XData, constraints,
actions, text, dimensions, hatches, and application XRecords are not traversed.

## Solution

- `src/DwgTrueView.Core` — packed GPU contracts and allocation-free 2D camera.
- `src/DwgTrueView.Cad` — shallow display extraction and block flattening.
- `src/DwgTrueView.Rendering.DirectX` — Direct3D 11 viewport and GPU buffers.
- `src/DwgTrueView.App` — minimalist Windows Forms shell.
- `tests-dotnet/DwgTrueView.Tests` — camera, buffer, color, insert, and fixture
  regressions.

## Build and run

```powershell
dotnet build DwgTrueViewLite.sln -c Release
dotnet test DwgTrueViewLite.sln -c Release
dotnet run --project src/DwgTrueView.App -c Release
```

## Controls

- Mouse wheel: zoom around the cursor.
- Middle mouse drag: pan.
- Middle mouse double-click or `Home`: zoom extents.
- `Ctrl+N` / `Ctrl+O`: new tab / open DWG or DXF. `Ctrl+S` save copy. `Ctrl+P` print.
- `Ctrl+Z` / `Ctrl+Y` undo/redo. `Ctrl+C` / `Ctrl+V` / `Ctrl+X` copy, paste, cut.
- `F3` object snap, `F7` grid, `F8` ortho. Drawing aliases: `L`, `M`, `F`, `CO`/`CP`, `RO`, `TR`.
- Grid toolbar button: toggle the CAD grid.
- Layer checkboxes: show or hide GPU draw ranges instantly.

## Publish

```powershell
dotnet publish src/DwgTrueView.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:PublishReadyToRun=true `
  -o artifacts/publish/dwg-trueview-lite
```
