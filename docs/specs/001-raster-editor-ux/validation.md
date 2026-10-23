# SPEC-001 validation

## Results on this host (2026-10-23)

- Branch: `feature/raster-selection-editor-ux`.
- `git diff --check`: passed.
- Python XML parsing: modified AXAML files and regression project are well-formed.
  Structural assertions confirmed separate toolbar and viewport rows.
- Installed .NET SDK 10.0.402 in the user directory (`~/.local/share/dotnet`).
- `dotnet build ShareX.ImageEditor.sln -p:Platform=x64 --configuration Release`:
  **passed, 0 warnings and 0 errors**.
- `dotnet run --project tests/ShareX.ImageEditor.RasterTests/ShareX.ImageEditor.RasterTests.csproj -p:Platform=x64 --configuration Release`:
  **passed, all 7 regression cases**.
- `dotnet publish src/ShareX.ImageEditor.App/ShareX.ImageEditor.App.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o artifacts/ShareX.ImageEditor-win-x64`:
  **passed**; self-contained output is approximately 263 MB.
- Windows graphical interaction, clipboard interoperability, scrolling and layout
  still require manual smoke testing on Windows; a successful cross-build is not a UI test.

## Linux toolbar investigation (2026-10-24)

Comparison with `origin/develop` found two relevant changes in the feature:
`EditorBuiltInToolbars` moved from the viewport's star-sized overlay into an Auto
row, and its constructor removed the bottom `Border` from its XAML parent for
insertion into a second host. The old `MaxWidth` bindings still depended on an
ancestor `UserControl.Bounds.Width`. Before arrange that width is zero; reparenting
also changes which control supplies it. These are layout/ownership hazards, rather
than evidence of a missing `MainViewModel` or `ToolbarAdapter`.

The correction gives the bottom bar its own `EditorBottomToolbar` XAML root,
created together with the top bar. No loaded XAML subtree is removed from a panel.
Both hosts explicitly stretch their content and inherit the editor VM; the top
bar still binds to `ToolbarAdapter`. The two ancestor-bounds width limits are
removed so parent layout constraints supply the available width. The primary
`AnnotationToolbar` retains its own scroll-width cap. Zoom and zoom-to-fit events
forward through the owning toolbar instance; disabling built-in toolbars detaches
those editor handlers and clears both hosts before recreation.

The full reported Linux rendering failure has **not** been reproduced here.
An in-process probe using Avalonia 12.1's layout manager, Skia/Fluent styles and
stubbed platform services found visible toolbars at 1000 × 700 in both
`origin/develop` and the original feature. Thus the source comparison identifies
hazards and the change removes them, but does not establish a confirmed desktop
root cause. The probe is exploratory, not a native-window or pointer interaction
test. Direct X11 initialization on this agent failed with `XOpenDisplay failed`.

Validation after the correction:

- SDK: `/home/andrea/.local/share/dotnet/dotnet` (10.0.402).
- Release solution build with `-p:Platform=x64 --no-restore -m:1 -nodeReuse:false`:
  **passed, 0 warnings and 0 errors**.
- Release raster harness build: **passed, 0 warnings and 0 errors**; execution
  with `--no-build --no-restore`: **all 7 cases passed**.
- `git diff --check` and XML parsing of all three toolbar/editor AXAML files:
  **passed**.
- Exploratory layout probe: passed zero-size startup followed by 1000 × 700,
  resize to 320 × 240 and 1400 × 900, toolbar hiding/showing, disabling/recreating
  built-ins, DataContext replacement and detach/reattach. At 320 × 240 the primary
  scroll viewport measured 310 pixels wide; at 1000 × 700 it measured 892 pixels.
  The toolbar rows collapsed to zero when hidden/disabled. Main VM, toolbar adapter
  and bottom Save command bindings resolved; zoom forwarding fired once and an
  old detached toolbar no longer changed the editor's zoom. This probe used
  temporary platform stubs/private API access outside the repository and is not
  part of the committed raster regression harness.

Remaining visual verification: run the standalone Linux app on the affected
desktop, both without an image and with an image argument. Dismiss the start
screen, check File menus and tool options, resize narrow/short and restore, scroll
the primary toolbar to its last tool, use zoom/zoom-to-fit and bottom commands,
toggle Ctrl+H, and check task mode plus `UseBuiltInToolbars=false`/true and editor
unload/reload. Confirm both bars paint and accept pointer/keyboard input and the
viewport stays between them. These checks remain required even after successful
compilation, raster tests and layout probing.

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

- Compilation and the seven raster cases have passed on this host. Native
  desktop rendering, pointer input and clipboard interoperability remain
  unverified; successful builds and in-process layout probes do not establish them.
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
