# Review — adhoc-scroll-sync-change-maps

Scope: working-tree diff vs `8883221` (MergeConflictEditorViewModel.cs, DiffView.xaml.cs,
MergeConflictEditorWindow.xaml(.cs)) + new `PickleGit/Controls/ScrollSyncGroup.cs`.
Plan files ignored. User reports manual verification GREEN in the running app.

## Checklist

| Area | Result |
|---|---|
| Commit format | N/A (no commits yet) |
| Code style | Pass — `_camelCase` fields, `nameof` in `ArgumentNullException`, `PaneRowKinds` uses `BaseViewModel.Set`, no new commands, no new brushes/converters, no MessageBox, no empty catch |
| Security (security.md) | Pass — no paths, processes, credentials, logging, JSON or patch handling touched |
| WPF/MVVM | Pass — scroll plumbing in code-behind is view logic; new XAML uses `{Binding PaneRowKinds}` + existing `DiffChangeMapControl`; `BorderBrush` stays `DynamicResource`; new grid column keeps ListView / selection overlay / find bar in column 0 together |
| Threading / git backend | Pass — no GitService calls added; `PaneRowKinds` computed in `RebuildPaneAndResultItems` alongside the existing `PaneItems`/`ResultItems` reassignment (same thread as before) |
| Rendering / virtualization | Pass — no new `OnRender` code; pane ListViews keep Recycling + `ScrollUnit=Pixel` with fixed row heights, so pixel offsets/extents are stable for the sync group |

## Findings

```
[MEDIUM] Correctness / UX
File: PickleGit/Controls/ScrollSyncGroup.cs:130
Issue: Block alignment produced by RunIndependent/ScrollPanesToBlock is transient. Broadcast copies the
source's ABSOLUTE VerticalOffset to every other member, so after the panes are deliberately left at
different pixel offsets (Ours on its toolbar row, Result on its block row), the very first wheel tick
in any pane snaps the other panes to that pane's absolute offset — potentially hundreds of pixels —
and they are no longer on the block. Same for Page Up/Down and scrollbar drags. (The old code had the
same absolute-offset semantics, so this is not a regression, but it undercuts the new
"align on block" feature: alignment survives only until the user touches a scroll input.)
Fix: Propagate vertical movement as a delta (target.ScrollToVerticalOffset(target.VerticalOffset +
e.VerticalChange), with the expected value recorded as that clamped sum) so relative alignment set by
RunIndependent is preserved; or, if absolute sync is the intended design, document that block
alignment is a one-shot view and accept the snap.
```

```
[LOW] Robustness / Threading
File: PickleGit/Controls/ScrollSyncGroup.cs:60
Issue: RunIndependent schedules EndIndependent on Dispatcher.CurrentDispatcher. All current callers
are on the UI thread so this works, but if ever called off the UI thread, CurrentDispatcher creates a
new dispatcher that never pumps — _independentDepth is never decremented and sync is disabled for the
group's lifetime with no error.
Fix: Use the members' dispatcher (e.g. capture viewer.Dispatcher in Add, or call VerifyAccess() on a
member's viewer) instead of Dispatcher.CurrentDispatcher.
```

```
[LOW] Robustness
File: PickleGit/Controls/ScrollSyncGroup.cs:87
Issue: ScrollChanged is a bubbling routed event; OnScrollChanged does not check e.OriginalSource, so a
ScrollViewer nested inside an item template (e.g. a TextBox) would be treated as a genuine scroll of the
outer pane and re-broadcast its (unchanged) offset — undoing a RunIndependent alignment. Verified that
the merge-editor row templates currently contain no TextBox/ScrollViewer (find boxes are siblings of
the ListViews, not inside them); DiffView row templates not exhaustively checked. Latent only.
Fix: `if (!ReferenceEquals(e.OriginalSource, member.Viewer)) return;` at the top of OnScrollChanged.
```

```
[LOW] Consistency
File: PickleGit/Views/MergeConflictEditorWindow.xaml.cs:883
Issue: The new Ours/Theirs change maps jump only the clicked pane via ScrollIntoView and let pixel sync
carry Result, whereas the Result change map (line ~917) block-aligns all panes when the clicked row is a
block. Clicking the same conflict band on the Ours map therefore lands Result off that block.
Fix: Mirror the Result-map logic: if lv.Items[idx] is a ConflictPaneItem with a BlockVm, call
ScrollPanesToBlock(item.BlockVm.Block, matching ResultItem); else ScrollIntoView.
```

```
[LOW] Duplication
File: PickleGit/Views/MergeConflictEditorWindow.xaml.cs:889
Issue: JumpPaneToFraction duplicates the fraction→index clamp already inlined in
ConflictResultChangeMap_JumpRequested (line 917) and DiffView.JumpListViewToFraction.
Fix: Extract a single `int? FractionToIndex(ListView, double)` helper (or reuse it in the Result handler)
so the three copies don't drift.
```

```
[LOW] Correctness (scope gap)
File: PickleGit/Views/MergeConflictEditorWindow.xaml.cs:305 / :367
Issue: OnPaneScrollWired aligns panes on the current unresolved block only once — when the three
ScrollViewers are first wired (the Loaded handlers early-return once wired). RewireFileVm (file switch)
does not realign, so the doc comment's "the editor never opens with the panes at different positions"
holds for the first file only; subsequent files open at whatever clamped offsets the panes inherit.
Fix: At the end of RewireFileVm, defer (DispatcherPriority.Loaded, after ItemsSource has rebound) a call
to the same alignment logic as OnPaneScrollWired; or reword the comment.
```

```
[INFO] Correctness edge case (uncertain impact)
File: PickleGit/Controls/ScrollSyncGroup.cs:136 / :113
Issue: An expectation is recorded whenever the target is not already at the landing offset, but no
ScrollChanged follows if the target never lays out (Result ListView Collapsed in manual-edit mode) or its
extent shrinks before the queued command runs so the offset doesn't change. The stale expectation then
swallows the next genuine scroll of that pane IF it lands exactly on the same clamped offset (one event;
the following scroll propagates). Rare and self-healing.
Fix (optional): skip Collapsed/!IsVisible targets in Broadcast, and/or expire expectations after a
dispatcher pass.
```

```
[INFO] Design note
File: PickleGit/Controls/ScrollSyncGroup.cs:50
Issue: RunIndependent's release after two DispatcherPriority.Loaded hops is a timing heuristic (relies on
layout running at Render priority ahead of Loaded, and on ScrollIntoView's deferred Loaded callback being
queued before the first hop). It holds for the current call sites; genuine user scrolls during that short
window are dropped (panes resync on the next scroll). Acceptable; documented in the XML comment.
Fix: None required.
```

```
[INFO] Lifetime
File: PickleGit/Controls/ScrollSyncGroup.cs:39
Issue: ScrollChanged subscriptions are never removed and Add de-dupes by ScrollViewer instance. Group,
viewers and owning view share a lifetime, and the *_Loaded handlers early-return once wired, so no leak or
duplicate subscription. If a template is ever re-applied, the old ScrollViewer stays a (harmless) member.
Fix: None required; add Remove() only if a future caller needs re-wiring.
```

## Summary

CRITICAL: 0 · HIGH: 0 · MEDIUM: 1 · LOW: 5 · INFO: 3

**Verdict: READY TO MERGE**
