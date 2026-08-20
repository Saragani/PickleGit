---
status: accepted
---

# No general-purpose control library — AvalonEdit scoped narrowly, everything else hand-rolled

PickleGit doesn't take a dependency on a general-purpose WPF control suite (Telerik/Infragistics/DevExpress-style). The commit graph (`CommitGraphControl.cs`) is a custom `DrawingVisual`-based renderer, and diff/code syntax highlighting for the (read-only) diff views is a custom per-line lexer (`Services/Highlighting/SyntaxHighlighter.cs`). The one deliberate exception is **AvalonEdit**, used only for the merge-conflict editor's editable RESULT pane (`MergeConflictEditorWindow.xaml`) — a real text-editing surface (undo/redo, tab handling, its own highlighting definitions) where hand-rolling would mean re-implementing an editor, not just a renderer.

## Considered Options

- **AvalonEdit for the (read-only) diff view** — rejected: AvalonEdit's editor-control architecture assumes it owns its own text layout and virtualization, which would conflict with the diff view's `FlatDiffItems` design — a single flattened, virtualized `ListView` that's what makes large diffs (thousands of lines) scroll smoothly. Bolting AvalonEdit into a *read-only* diff pane would mean either abandoning that virtualization or fighting AvalonEdit's own layout engine for control of it, for a surface that never needs real editing in the first place.
- **AvalonEdit (or hand-rolled) for the merge editor's RESULT pane** — AvalonEdit was adopted here specifically because this pane *is* genuinely editable: the user hand-resolves conflicts by typing directly into it. Hand-rolling real text editing (caret movement, undo stack, tab/indent handling) for one dialog would have cost far more than the dependency.
- **A third-party grid/tree/graph control** for the commit list or commit graph — rejected: the commit graph needs bespoke Bézier-lane rendering with ref-badge overlays that no off-the-shelf control provides directly, so adopting a general-purpose grid library wouldn't avoid writing a custom renderer anyway — it would just add a licensing and dependency footprint on top of one.

## Consequences

- More code to hand-roll and maintain outside the merge editor: a custom lexer per highlighted language for the diff view, custom `DrawingVisual` rendering for the graph, and the discipline of caching/freezing every `Brush`/`Pen`/`StreamGeometry` used in `OnRender` (see `code-style.md` and `PickleGit/CLAUDE.md`'s performance notes) to keep scrolling smooth.
- AvalonEdit's built-in highlighting definitions ship colors tuned for a light background, so the merge editor's dark-theme integration remaps them to match `WordDiffHighlighter.SyntaxBrushes` — see the comments in `MergeConflictEditorWindow.xaml.cs`.
- The two syntax-highlighting systems (the hand-rolled lexer for diffs, AvalonEdit's own for the merge editor's RESULT pane) are independent and don't share code — a language-highlighting fix in one does not automatically apply to the other.
