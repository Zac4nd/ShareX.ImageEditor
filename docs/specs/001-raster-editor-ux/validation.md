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

## Earlier toolbar investigation (2026-10-24)

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

The screenshot was subsequently clarified to be a **Windows build running in Wine**,
not a native Linux build. The full reported rendering failure has **not** been
reproduced in a desktop window here.
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

Remaining visual verification: run the Windows publish in the affected Wine
environment and on native Windows, and test the native Linux app separately,
both without an image and with an image argument. Dismiss the start
screen, check File menus and tool options, resize narrow/short and restore, scroll
the primary toolbar to its last tool, use zoom/zoom-to-fit and bottom commands,
toggle Ctrl+H, and check task mode plus `UseBuiltInToolbars=false`/true and editor
unload/reload. Confirm both bars paint and accept pointer/keyboard input and the
viewport stays between them. These checks remain required even after successful
compilation, raster tests and layout probing.

## Font/resource and image-overlap diagnosis (2026-10-24)

The supplied screenshot is from the **Windows executable under Wine**. It is not
proof of a native Linux font problem, and neither the earlier toolbar ownership
change nor a successful publish proves that the screenshot has been corrected.

Independent findings and targeted corrections:

1. **Missing standalone application resources, reproduced.**
   `ShareX.ImageEditor.csproj` already embeds `Assets/lucide.ttf` as an
   `AvaloniaResource`. Fontconfig reports family/full/PostScript name `lucide`,
   style `Regular`; Skia's stream-loaded typeface also reports `lucide`.
   The existing theme URI `avares://ShareX.ImageEditor/Assets#lucide` matches that
   metadata and the assembly/path. Avalonia `FontManager.TryGetGlyphTypeface`
   resolves that URI to `lucide`, and every codepoint in all 64 `EditorIcons`
   constants has a nonzero glyph. No system installation of Lucide was used
   (`fc-list` reports none).

   However, standalone `App.Initialize()` loaded `App.axaml`, whose styles only
   included Fluent. It never merged `ImageEditorTheme.axaml`. The separate
   library-host `AvaloniaApp.Initialize()` did merge it. An application-level
   resource regression failed before the correction with
   `Application did not load ShareX.FontFamily.Icon`, despite the direct embedded
   font test passing. Missing dynamic resources affect top/bottom icons and the
   ShareX palette used by menus, controls and panels. The standalone app now
   merges the library theme at application scope, also making it available to
   popups and other windows. The font file, family name and URI remain unchanged.

   No extra FontManager registration or system-font fallback was added:
   [Avalonia's font URI documentation](https://docs.avaloniaui.net/docs/styling/custom-fonts)
   specifies the internal family name after `#`, and
   [the pinned 12.1 FontManager implementation](https://github.com/AvaloniaUI/Avalonia/blob/12.1.0/src/Avalonia.Base/Media/FontManager.cs)
   creates an embedded collection on demand for an `avares:` source. This
   startup/resource defect is platform-independent; it is not evidence of a
   Wine-only font-loader failure. Any remaining Wine-specific problem is untested.

2. **Compact icon clipping, independently reproduced with real fonts.**
   The compact toolbar reduced buttons to 32 × 32 but inherited the general
   button padding of `10,6` plus a one-pixel border. The generated icon TextBlock
   measured only **10 × 18** for an 18-pixel glyph. This produced incomplete
   symbols after the font resource was repaired, distinct from missing-glyph
   squares. The compact toolbar now uses four-pixel padding within its existing
   scope. A regression asserts that visible glyph content fits, in addition to
   checking the resolved font and glyph indices. Offscreen Skia frames were
   visually inspected before and after this correction.

3. **Image overlap is a separate layout question.**
   The old `origin/develop` grid had a single star row shared by image content and
   floating toolbars; the image could occupy the toolbar area by construction.
   The current branch already has `Auto,*,Auto` rows, with the viewport in row 1,
   and clips the central image/overlay host. Those changes are retained. The
   viewport row now also clips its entire subtree, including sidebar descendants,
   to keep overflowing content within the area reserved between the bars.
   The new harness measures a real 3000 × 2000 image at 25%, 100% and 250%, with
   nonzero scroll offsets, then resizes to 320 × 240 and 1400 × 900. It checks
   viewport/toolbar bounds and both clipping boundaries, toolbar hiding/showing,
   built-in replacement, adapter/Save bindings, and dark/light theme changes.
   At 1000 × 700 the top/bottom rows are 86/58 pixels and the image ScrollViewer
   is 540 pixels high with its margin; at 320 × 240 they are 118/58 and 48 pixels.
   The viewport does not overlap either toolbar in these checks. This does not
   establish which executable revision produced the Wine screenshot.

Verification of the final source with .NET SDK **10.0.402**:

- Release solution build: **passed, 0 warnings and 0 errors**.
- Raster harness: **all 7 cases passed**.
- New UI harness: **all 3 cases passed in each of two separate processes**,
  exercising the production standalone and library-host `Application.Initialize()`
  paths independently. Includes real asset loading, Skia font metadata, Avalonia
  glyph coverage, palette lookup, generated icon layout and offscreen rendering.
- UI harness build: passed, with the expected **AVA3001** warning for the isolated
  test presentation-root adapter's access to Avalonia's unstable APIs. No new
  NuGet dependency was added. This adapter needs review on Avalonia upgrades;
  it stubs cursor/window services, not fonts or control layout. An attempted
  Avalonia.Headless restore was blocked by the sandbox's NuGet network access.
- Windows `win-x64` self-contained publish: **passed**, approximately 263 MB at
  `artifacts/ShareX.ImageEditor-win-x64/`. The final publish includes both the
  resource initialization and compact padding changes.
- `git diff --check`: passed. Work remains on
  `feature/raster-selection-editor-ux`; no push, merge or `develop` changes.

Desktop limits are explicit: native Linux launch failed before window creation
with `XOpenDisplay failed`. A Windows/Wine launch attempt used an isolated
`/tmp` prefix, but Wine failed to start its server (`sock_check_pollhup` and a
read-only `/run/user/1000/wine/server-…` directory). There was no Wine or native
Windows visual verification. Native Linux **offscreen Skia** success must not be
reported as native Linux desktop success, and does not prove Wine rendering is
fixed. Popup painting/input, menus, native font backends, DPI, pointer handling,
clipboard interaction and the original image/title clipping still require the
manual desktop checklist in the affected Wine setup and on native platforms.
The narrow-size check asserts viewport geometry and primary icon sizes; it does
not establish that every bottom-toolbar command is reachable in a 320px window.

Run the new cases from the repository root (the SDK must be on PATH):

```sh
dotnet run --project tests/ShareX.ImageEditor.UiTests/ShareX.ImageEditor.UiTests.csproj -c Release -p:Platform=x64
dotnet run --project tests/ShareX.ImageEditor.UiTests/ShareX.ImageEditor.UiTests.csproj -c Release -p:Platform=x64 --no-build -- --library-host
```

Set `SHAREX_UI_TEST_OUTPUT_DIR` to a writable directory to save the offscreen PNGs.
These cases are also included in the Windows CI workflow, whose execution has
not been observed in this local session.

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
