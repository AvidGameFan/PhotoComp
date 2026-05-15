# Plan: PhotoComp — Photo Comparison Tool

A desktop photo comparison app built with **C# + Avalonia UI** (MIT licensed, cross-platform Windows + Linux). Two independent image panels are displayed side-by-side; zoom and pan are synchronized. Images are loaded from a user-selected folder, sorted by EXIF date. Each panel navigates independently via arrow keys (when focused) or on-screen buttons. A heart icon marks preferred images; a copy command exports selections to a chosen destination folder.

---

## Technology Stack

| Component | Choice | License |
|-----------|--------|---------|
| UI framework | Avalonia UI 11.x | MIT |
| MVVM helpers | CommunityToolkit.Mvvm 8.x | MIT |
| EXIF / metadata | MetadataExtractor 2.9.x | **Apache 2.0** |
| Runtime | .NET 8 | MIT |

> **Apache 2.0 note:** MetadataExtractor is Apache 2.0, not MIT. It is permissive and compatible with MIT projects. If strictly MIT-only is required, fall back to reading dimensions from `Bitmap` and `File.GetLastWriteTime()` for dates (loses EXIF accuracy). See *Further Considerations* #1.

---

## Project Structure

```
PhotoComp/
├── PhotoComp.sln
└── PhotoComp/
    ├── PhotoComp.csproj
    ├── App.axaml / App.axaml.cs
    ├── Models/
    │   ├── ImageItem.cs       (FilePath, FileName, DateTaken, Width, Height)
    │   └── ZoomState.cs       (Scale, OffsetX, OffsetY — shared observable)
    ├── Services/
    │   ├── ImageLoaderService.cs
    │   └── CopyService.cs
    ├── ViewModels/
    │   ├── MainWindowViewModel.cs
    │   └── ImagePanelViewModel.cs
    ├── Views/
    │   ├── MainWindow.axaml / .axaml.cs
    │   └── ImagePanelView.axaml / .axaml.cs
    └── Assets/
        └── heart.svg
```

---

## Implementation Phases

### Phase 1 — Project Setup
1. `dotnet new avalonia.mvvm -n PhotoComp` to scaffold standard Avalonia MVVM project
2. Add NuGet packages: `Avalonia`, `Avalonia.Desktop`, `CommunityToolkit.Mvvm`, `MetadataExtractor`
3. Configure `AppBuilder.UsePlatformDetect()` in `Program.cs` for Windows + Linux auto-detection

### Phase 2 — Core Models & Services *(parallel with Phase 1)*
4. `ImageItem` — immutable record: `FilePath`, `FileName`, `DateTaken` (DateTime), `Width`, `Height`
5. `ZoomState` — `ObservableObject` with `Scale` (default 1.0), `OffsetX`, `OffsetY`; single shared instance passed to both panel view-models
6. `ImageLoaderService.LoadImages(folderPath)` — enumerate `*.jpg`, `*.jpeg`, `*.png` (case-insensitive); read `ExifSubIfdDirectory.TagDateTimeOriginal` via MetadataExtractor (fallback to `File.GetLastWriteTime()`); read pixel dimensions from EXIF or `Bitmap`; sort by `DateTaken` ascending
7. `CopyService.CopySelected(filePaths, destinationFolder)` — `File.Copy` with `overwrite: false`; `Path.Combine` throughout for cross-platform paths

### Phase 3 — ViewModels *(depends on Phase 2)*
8. `ImagePanelViewModel` — receives `IReadOnlyList<ImageItem>`, `ZoomState`, and shared `HashSet<string> selectedPaths`; exposes `CurrentIndex`, `CurrentImage`, `IsCurrentHearted`, `NavigateNextCommand`, `NavigatePreviousCommand`, `ToggleHeartCommand`
9. `MainWindowViewModel` — owns shared `ZoomState` and `HashSet<string> SelectedPaths`; creates both panel VMs (left starts at index 0, right at index 1); exposes `LoadFolderCommand` (opens folder picker → calls loader → constructs panel VMs), `CopySelectedCommand` (disabled when count = 0; opens destination folder picker → calls copy service), `SelectedCount`

### Phase 4 — ImagePanelView UserControl *(depends on Phase 3)*
10. `ImagePanelView.axaml` — `Focusable="True"` Grid: `LayoutTransformControl` wrapping `Image` control; left/right navigation buttons at panel edges; heart `ToggleButton` (top-right corner, bound to `IsCurrentHearted`); info `TextBlock` overlay (bottom-left, semi-transparent background) showing `{Width}×{Height} | {DateTaken:yyyy-MM-dd HH:mm:ss}`
11. Code-behind zoom: `PointerWheelChanged` → update `SharedZoom.Scale` (clamped 0.1–10×); pan: `PointerPressed` + `PointerMoved` → update `SharedZoom.OffsetX/Y`; both panels observe `ZoomState.PropertyChanged` → apply `ScaleTransform` + `TranslateTransform` to `LayoutTransformControl`
12. Double-click on panel → reset `ZoomState` to Scale=1, OffsetX=0, OffsetY=0
13. Keyboard: `KeyDown` → `Key.Left` = `NavigatePrevious`, `Key.Right` = `NavigateNext`; clicking on a panel calls `Focus()` so arrow keys route to that panel
14. Image loading: bind `Image.Source` via a `StringToAvaloniaBitmapConverter`; implement a small LRU bitmap cache (≤ 10 items) with `Bitmap.Dispose()` on eviction to manage memory with large photos

### Phase 5 — Main Window Layout *(depends on Phase 3 & 4, parallel with Phase 4)*
15. `MainWindow.axaml` — `DockPanel`: top `ToolBar` with "Open Folder" button, "Copy Selected (N)" button, zoom-level readout; two-column `Grid` containing left and right `ImagePanelView`; bottom `StatusBar` showing `Left: N/Total | Right: N/Total | Selected: N`

### Phase 6 — Packaging
16. Windows: `dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true`
17. Linux: same command with `-r linux-x64`

---

## Verification

1. Open a folder with mixed JPEG + PNG — verify list sorted by EXIF date, not filename
2. Set left panel to image 3, right panel to image 7, click each and press arrow keys — confirm independent navigation
3. Mouse-wheel zoom on left panel → right panel zooms identically; drag in right panel → left panel pans identically
4. Overlay accuracy: confirm displayed dimensions and date/time match an EXIF viewer
5. Heart 3 images → click "Copy Selected (3)" → choose output folder → verify exactly 3 files copied, originals untouched
6. Add a PNG with no EXIF — verify graceful fallback to file modification date, no crash
7. Open an empty folder — verify panels are blank with no crash
8. Run on Linux with `dotnet run` — verify UI, image loading, and folder picker work

---

## Decisions

- **Navigation**: fully independent per panel — each panel tracks its own index in the shared sorted list
- **Arrow keys**: captured by the last-clicked (focused) panel only
- **Formats**: JPEG/JPG and PNG only; RAW and HEIC are out of scope
- **Copy**: non-destructive — originals never modified; destination files skipped (not overwritten) if already present.  If a sidecar file (e.g. `IMG_1234.jpg` → `IMG_1234.json`) is present, copy it alongside the image -- the sidecar extension could be .json or .txt.
- **Heart state**: persists for the session only (no database or sidecar files)
- **Zoom reset**: double-click on either panel resets both panels to Scale=1, no offset

---

## Further Considerations

1. **Apache 2.0 vs MIT**: MetadataExtractor is Apache 2.0. Either accept it as compatible or replace with manual fallback (`BinaryReader`-based EXIF parsing) for a fully MIT dependency tree.
2. **Memory management**: Large JPEGs (20–50 MB decoded) will spike RAM with both panels open. The LRU bitmap cache (step 14) is essential — tune the limit based on expected photo resolution.
3. **Heart persistence**: Currently session-only. If selections should survive an accidental close, a simple `selections.json` sidecar file in the source folder could persist the set — out of current scope.
