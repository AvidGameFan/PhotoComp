# PhotoComp

A desktop photo comparison tool for Windows and Linux. Open a folder of images and compare them side-by-side with synchronized zoom and pan. Mark favourites with a heart, then copy them — along with any sidecar files — to a destination folder.

---

## Features

- **Side-by-side panels** — two independent image panels displayed simultaneously
- **Synchronized zoom & pan** — mouse-wheel zoom and drag-to-pan affect both panels at once; double-click to reset
- **Independent navigation** — each panel navigates through the image list separately using on-screen buttons or arrow keys
- **EXIF-sorted loading** — images are sorted by date taken (falls back to file modification time when EXIF is absent)
- **Favorite selection** — click the heart icon on any image to mark it; click again to deselect
- **Copy with sidecars** — copies selected images to a chosen folder; automatically copies matching `.json` or `.txt` sidecar files alongside each image
- **Single/dual view toggle** — switch between side-by-side and single-panel view from the toolbar
- **Busy indicator** — a loading overlay and wait cursor appear while a large folder is being scanned

---

## Requirements

- .NET 10 runtime (or use the self-contained build — no install required)

---

## Running

### Self-contained build (no .NET install needed)

Download the zip for your platform from the [Releases](#) page, extract it, and run:

**Windows**
```
PhotoComp.exe
```

**Linux**
```bash
chmod +x PhotoComp
./PhotoComp
```

### From source

```bash
git clone https://github.com/your-org/PhotoComp.git
cd PhotoComp
dotnet run --project PhotoComp/PhotoComp.csproj
```

---

## Usage

### Opening images

Click **📁 Open Folder** in the toolbar and choose a folder containing JPEG or PNG images. Images are loaded in EXIF date order (oldest first). The left panel starts at the first image and the right panel starts at the second.

### Navigating

| Action | Result |
|--------|--------|
| Click **◄** / **►** buttons | Move to previous / next image in that panel |
| Click a panel, then **← →** arrow keys | Keyboard navigation for the focused panel |

### Zooming and panning

| Action | Result |
|--------|--------|
| Mouse wheel | Zoom in / out (both panels, 0.1× – 10×) |
| Left-drag | Pan the image (both panels move together) |
| Double-click | Reset zoom and pan to defaults |
| **⊙ Reset Zoom** button | Same as double-click |

The current zoom level is shown in the toolbar as a percentage.

### Selecting images

Click the **♡** heart icon in the top-right corner of a panel to mark the current image as a favourite. A filled red **♥** indicates it is selected. Click again to deselect. The toolbar shows the running count: **💾 Copy Selected (N)**.

### Copying selected images

1. Heart one or more images.
2. Click **💾 Copy Selected (N)** in the toolbar.
3. Choose a destination folder.
4. A summary dialog reports how many files were copied, how many were skipped (already present), and details of any errors.

For each image copied, PhotoComp also copies a matching `.json` or `.txt` sidecar file from the same source folder if one exists (e.g. `IMG_1234.jpg` → also copies `IMG_1234.json`). Originals are never modified or overwritten.

### Single-panel view

Click **⊟ Single View** in the toolbar to hide the right panel and give the left panel the full window width. Click **⊞ Dual View** to restore the side-by-side layout.

### Info overlay

Each panel shows a small overlay in the bottom-left corner with the image's pixel dimensions and EXIF date/time:

```
3024×4032  |  2024-06-15 14:32:07
```

---

## Building

### Windows (produces `dist\PhotoComp-windows-x64-vX.Y.Z.zip`)

```powershell
powershell -ExecutionPolicy Bypass -File .\build-windows.ps1
powershell -ExecutionPolicy Bypass -File .\build-windows.ps1 -Version "1.2.0"
```

### Linux from Windows (cross-compile, produces `dist\PhotoComp-linux-x64-vX.Y.Z.zip`)

```powershell
powershell -ExecutionPolicy Bypass -File .\build-linux.ps1
```

### Linux natively (produces `dist/PhotoComp-linux-x64-vX.Y.Z.zip`)

```bash
chmod +x build-linux.sh
./build-linux.sh
```

> **Note:** The Linux zip is built without execute-bit preservation. Recipients may need to run `chmod +x PhotoComp` once after extracting.

---

## Running Tests

```bash
dotnet test PhotoComp.Tests/PhotoComp.Tests.csproj
```

---

## Supported Formats

| Format | Extension |
|--------|-----------|
| JPEG | `.jpg`, `.jpeg` |
| PNG | `.png` |

RAW, HEIC, and other formats are not currently supported.

---

## License

MIT

---

## Discussion

Initial version mostly coded by Claude Sonnet 4.5 and 4.6, with design, guidance, and testing by Avidgamefan.
