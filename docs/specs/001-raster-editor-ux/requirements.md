# SPEC-001: Raster selection and editor usability

Goal: preserve the existing image annotation tools while making the editor useful as a minimal raster editor.

## Requirements
1. Select a rectangular region of the *background image pixels*, copy (Ctrl+C), cut (Ctrl+X), paste (Ctrl+V), move pasted pixels, and commit/cancel. Preserve existing annotation selection and clipboard behavior. Ideally floating pasted bitmap is handled as existing image annotation. Support Undo/Redo and correct image coordinates under zoom/scroll. Do not destroy source image on copy.
2. Keep menus/toolbars separate from the editable image viewport, never overlay image content. Reduce excessively large menu footprint without removing tools or keyboard accessibility.
3. Allow crop and rectangular selection to extend beyond the initial viewport: when dragging near the edge auto-scroll the viewport; consider keyboard scrolling while selection is active. Clamp to image bounds and make it work at varying zoom and scrolling positions.
4. Maintain existing ShareX image annotation capabilities including numbered steps, arrows, pixelate, save and clipboard. Prefer minimal changes against upstream.
5. Provide reproducible tests where practical and document any limitations. No changes to upstream/develop branch; work in feature/raster-selection-editor-ux.

Implementation quality: inspect architecture before writing, make targeted changes, avoid unrelated refactoring. Debian host may lack .NET SDK; if so note this rather than claiming build success. Do not push/merge without further inspection. Keep GPL attribution.
