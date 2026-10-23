# SPEC-001 validation

## Results on this host (2026-10-23)

- Branch: `feature/raster-selection-editor-ux`. Changes remain in the working tree;
  no push, merge or upstream/develop modification.
- `git diff --check`: passed.
- Python XML parsing: modified AXAML files and regression project are well-formed.
  Structural assertions confirmed separate top toolbar, viewport/interaction and
  bottom toolbar rows. These checks do not compile Avalonia bindings or C#.
- Build attempted with `dotnet build ShareX.ImageEditor.sln -p:Platform=x64`:
  **not run**, exit 127, `dotnet: command not found`.
- Regression harness attempted with the command below: **not run**, same missing
  SDK. No runtime, native clipboard, graphical layout or interaction success is
  claimed on this Debian host.

## Reproduce with .NET 10 SDK

From the repository root:

```sh
dotnet build ShareX.ImageEditor.sln -p:Platform=x64
dotnet run --project tests/ShareX.ImageEditor.RasterTests/ShareX.ImageEditor.RasterTests.csproj -p:Platform=x64
dotnet run --project src/ShareX.ImageEditor.App/ShareX.ImageEditor.App.csproj -p:Platform=x64
```

The console harness has seven cases, exits nonzero on failure, and references the
real editor library without adding a test framework. It checks invalid/empty
regions; reversed, fractional and out-of-bounds rectangles; source immutability;
background-only extraction; independent clipboard/image clones; alpha preservation;
opaque-input transparent cuts; annotation preservation; ordered cut/paste/move/cancel
undo/redo; and redo branch replacement. Changing `EditorCore.Zoom` checks that core
extraction remains in image space; it does **not** test Avalonia coordinate transforms.
Native SkiaSharp assets and the normal solution package dependencies must restore
successfully on the test platform.

## Desktop smoke checklist (pending)

Use a large image (at least 3000 × 2000) with recognizable corner/edge markings,
plus a small image that fits completely. Repeat region tests at 25%, 100% and 250%,
from both zero and nonzero horizontal/vertical scroll offsets.

1. Press `Q` or choose **Select pixels**. Drag in both directions, including across
   existing steps/arrows. `Ctrl+C`, `Ctrl+V`: source stays intact; only background
   pixels become a selected image annotation at the original image coordinates.
   Move with the mouse and arrows; Enter accepts. Confirm another paste is independent.
2. `Ctrl+X`: selected background becomes transparent; annotations remain. Paste,
   move, then Escape: pasted annotation disappears, cut remains. Walk backward and
   forward through move/paste/cut using Ctrl+Z/Ctrl+Y; verify pixels and annotations.
3. Copy region pixels into another application. Then copy a different external
   bitmap or image file: editor paste must use the original external-image flow.
   Check ordinary annotation cut/copy/paste and Ctrl+Shift+C; outside a raster
   selection Ctrl+C still copies the complete edited image.
4. Hold a raster drag near each viewport edge and each corner without moving the
   pointer. Scrolling must continue and the far corner of the marquee must follow
   the stationary pointer in image space. Reverse direction, reach image bounds,
   release, right-click or press Escape: scrolling must stop.
5. Repeat edge/corner scrolling with crop resize/move handles and with **Quick
   crop** enabled for drawing. Check Shift square crops at all four image edges.
   Arrow keys scroll the viewport while a region is active; Shift+arrows scroll
   farther. While a pasted image/annotation is selected, arrows move it instead.
6. During a drag, switch tools, lose pointer capture or close/unload the editor.
   Confirm scrolling stops. Open another image and verify no old marquee remains.
7. Resize the editor to narrow/short sizes; scroll the primary toolbar to its last
   tool, tab through buttons and use shortcuts. Toggle Ctrl+H; check both toolbars
   collapse and image content stays between the toolbar rows when shown. Include
   task mode and a host using `UseBuiltInToolbars=false`.
8. Draw numbered steps, arrows and pixelate regions; edit text; save and copy the
   whole image. Confirm overlays/marquees are absent from output. Check preview
   padding/centering and a high-DPI desktop. Exercise Auto Copy on/off so it cannot
   overwrite the clipboard during an active raster selection or pending paste.

## Behavior boundaries and remaining limitations

- Runtime validation is pending because this host lacks the SDK and a tested
  Avalonia desktop session. Compilation and native clipboard behavior remain
  unverified; XML/static inspection does not establish either.
- Paste acceptance keeps an editable image annotation. Enter deselects it; Escape
  removes the pending paste as an undoable deletion. Canceling paste does not undo
  the separate cut. Tool changes/normal deselection accept the pending annotation.
- Raster selection is a transient rectangular marquee, recreated by dragging;
  it has no resize handles or freeform selection. Only background pixels are copied.
- Ordinary external image paste retains the existing insertion dialog. Identifying
  an editor-origin raster bitmap uses exact normalized pixel comparison. A native
  clipboard provider that changes colors/alpha will fall back to that existing
  external-image flow. Large clipboard comparisons run on the UI thread.
- Cuts allocate a new alpha-capable background and use existing full-canvas history
  limits (five canvas snapshots / fifty total snapshots). Very large images can
  consume substantial memory. No new layer engine or history storage is introduced.
- New raster labels/notifications are English, consistent with other existing
  inline editor labels; translation coverage is not added in this change.
