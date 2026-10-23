# SPEC-001 design

## Existing architecture

`EditorCore` owns source pixels, annotations and a unified memento history.
`ApplyImageOperation` records a full canvas snapshot; annotation operations record
annotation snapshots. `EditorInputController` owns crop interactions;
`EditorSelectionController` already moves/resizes image annotations in image
coordinates. `EditorView.ClipboardHandler` distinguishes external images from an
internal deep-cloned annotation clipboard. The built-in toolbars currently occupy
the same grid cell as the viewport.

## Targeted changes

1. Append a `RasterSelect` tool (preserve persisted enum values), expose it in the
   existing customizable toolbar with the unused `Q` shortcut. Keep `Select` and
   its annotation hit testing unchanged. Draw a transient rectangular marquee in
   the overlay, never in the saved annotation collection. Convert pointer positions
   through `AnnotationCanvas`, whose logical coordinates are source image pixels.
   Normalize reverse drags, floor/ceil pixel edges and intersect with source bounds.
2. Add core helpers to copy background pixels into an independent bitmap and clear
   a cut region to transparency through `ApplyImageOperation`. Annotations above
   those pixels remain intact. Copy does not change the image or history.
3. Raster copy/cut uses the internal image-annotation clipboard and also publishes
   a bitmap to the system clipboard. A failed clipboard write must not cut pixels.
   Internal raster paste reuses `ImageAnnotation` and existing selection. Inspection
   found that UI geometry changes did not capture history, so a small core hook
   records one snapshot per pointer gesture and one per keyboard nudge.
   Enter accepts the pasted annotation; Escape removes it through history. A
   canceled paste is therefore undoable, and canceling paste after cut does not
   reverse the earlier cut (Ctrl+Z restores it). Accepting keeps an editable image
   annotation rather than flattening unrelated annotations. Ordinary annotation
   and external clipboard paths keep their behavior. Outside a raster selection,
   Ctrl+C retains the existing whole-image behavior; Ctrl+Shift+C copies annotations.
   Match native bitmap pixels with the internal raster clipboard before using its
   stored origin; different external images/files retain clipboard priority.
4. Put the top toolbar and bottom actions in separate auto-sized grid rows with
   the viewport in the remaining row. Preserve lazy toolbar creation and event
   wiring. Give the bottom bar its own XAML root rather than removing and
   reparenting a child from the top bar's loaded tree. Let the hosts constrain
   toolbar width instead of binding it to pre-arrange ancestor bounds.
   Compact primary toolbar buttons locally; keep horizontal scrolling,
   all tools, customizable shortcuts and focus navigation.
5. Use a UI timer for edge scrolling during crop draw/handle drag and raster drag.
   Retain the pointer in viewport coordinates, scroll a bounded amount each tick,
   then translate it back into image coordinates after layout. Share crop update
   logic between pointer motion and scrolling. Clamp offsets and regions. Stop on
   release, capture loss, tool change, unload and disposal. Arrow keys scroll while
   a region is active (Shift increases the step), without stealing annotation moves.

## Validation and scope

Add a small reproducible .NET regression harness for pixel extraction, source
immutability, transparent cut, annotations, and unified undo/redo; avoid a new test
framework dependency. Include manual cases for viewport geometry, stationary edge
scrolling, zoom/scroll, clipboard interoperability, cancellation, annotation tools
and toolbar layout. Attempt build/run on this host and record actual results.

No broad input/controller rewrite, layer system, freeform pixel selection, upstream
branch changes, push or merge. Retain GPL headers. Native clipboard behavior and
Avalonia visual interaction require a desktop smoke test; static checks cannot
substitute for compilation or that smoke test.
