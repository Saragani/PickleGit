---
status: accepted
---

# Plain Thumb instead of GridSplitter for pane resizing

The sidebar and detail-panel widths were originally resized with a standard WPF `GridSplitter`, but its default drag behavior fought with the `Auto`-sized grid columns backing those widths, producing visible jitter on roughly every third or fourth drag. Both splitters were replaced with a plain `Thumb` that drives `Width` directly on the adjacent view, with the drag delta clamped to existing min/max constants.

## Consequences

- Pane-width persistence (`AppSettings`) reads and writes through the ViewModel-owned `Width` properties instead of relying on `GridSplitter`'s own resize mechanics.
- Clamping to the valid min/max range must happen in the property setter itself, not just at the drag site — a value loaded straight from `settings.json` (hand-edited, or written by a stale/buggy older build) skips the drag-site clamp entirely, and WPF's `FrameworkElement.Width` setter throws outright on a negative value. This was found as a follow-up bug after the initial Thumb migration (see `code-style.md`'s JSON-deserialization security note, and commit `ee0c672`).
