# SPEC-001 tasks

- [x] Inspect requirements, core/history, clipboard, input and toolbar architecture.
- [x] Write design and ordered implementation tasks before changing product code.
- [x] Add bounded background-pixel copy/cut helpers using unified history.
- [x] Add rectangular raster tool, overlay, clipboard routing and paste accept/cancel.
- [x] Add timer-based crop/raster edge scrolling and keyboard viewport scrolling.
- [x] Separate toolbar rows from the viewport and compact primary controls.
- [x] Add reproducible core regression harness and manual interaction checklist.
- [x] Attempt build/tests, inspect diff and document results/limitations.
- [x] Verify current feature branch; leave changes unpushed.

Validation attempts are complete; runtime checks remain pending because `dotnet`
is absent. See [validation.md](validation.md) for commands, the desktop checklist,
actual static-check results and remaining behavior limitations.
