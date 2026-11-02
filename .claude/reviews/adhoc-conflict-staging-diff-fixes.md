# Review — adhoc-conflict-staging-diff-fixes

Scope: working tree vs merge-base f7185de (uncommitted; includes new files DecodedStagingService.cs,
LineDiff.cs, TextDecoding.cs, ThreeWayMerge.cs). Rules applied: security.md, code-style.md,
architecture.md, PickleGit/CLAUDE.md. Not built or run as part of this review.

Things checked that passed (no finding): all new LibGit2Sharp access (GetConflictStageBytes,
WriteIndexContent, DecodedTextFallback, check-attr) runs on the executor. The Task.Run in
OpenMergeEditorCoreAsync only does decoding and file reads, with no repo access. CLI args follow the
established single-string + CliGitService.Quote convention. ResolveInsideWorkdir guards workdir reads
and writes, using a trailing-separator root compare. Stale-content checks guard both in-app index and
workdir writes. The new XAML uses DynamicResource for every brush, and every converter it uses
(BoolToVis, NullToVis) lives in App.xaml. The ContextMenu bindings follow the PlacementTarget.Tag
pattern. The new commands go through ResolveTargets, which handles FileTreeRow. No MessageBox or
empty catch was added. AppLog calls log only paths, never secrets. The OpenMergeEditor reentrancy
guard calls InvalidateRequerySuggested. The LineDiff Myers bisect index math matches
diff-match-patch, and its cost-limit split always makes progress. The ThreeWayMerge chunk loop
terminates and handles add/add (null base).

---

[MEDIUM] Performance / UI freeze (tree multi-select)
File: PickleGit/ViewModels/RepositoryViewModel.Staging.cs:302
Issue: ApplyTreeRowSelection runs on every CollectionChanged of SelectedWorkingTreeRows/SelectedStagedTreeRows. ListViewMultiSelectBehavior adds a Shift-range one item at a time, so N added rows means N calls. Each call does files.Clear() plus one files.Add per effective file (line 328). Every one of those Adds raises _selectedWorkingFiles.CollectionChanged. That in turn re-syncs the hidden flat ListView's behavior, a full SelectedItems Clear and re-add each time (O(k)), and raises two PropertyChanged events. Selecting one folder holding a few thousand files (a typical untracked folder) is therefore O(n^2) WPF selection operations. A Shift-range over many rows is roughly O(n^3). The same per-item pattern runs in SyncTreeRowsFromFiles (line 365) against the tree's own behavior.
Fix: Coalesce. Defer ApplyTreeRowSelection with a single Dispatcher.BeginInvoke and a dirty flag, so one burst of row changes is applied once. Write the file collection as a single bulk change, for example a RangeObservableCollection Reset or a guard that suppresses the flat-list behavior during the rewrite. Measure with a folder of 2,000+ untracked files.

[MEDIUM] Selection semantics / data-loss adjacent
File: PickleGit/ViewModels/RepositoryViewModel.Staging.cs:312
Issue: While a folder row is selected, Ctrl-clicking a file row under it to deselect that file has no effect on the effective set. The folder still contributes row.Files, so the deselected file stays in _selectedWorkingFiles. SyncTreeRowsFromFiles is skipped because _applying*TreeSelection is set, so the ListView shows the file as deselected while Stage, Discard and Take Ours/Theirs still act on it. Discard has a confirmation, but that dialog shows a count, not the file list.
Fix: When a file row under a selected folder is deselected, drop the folder from the selection (and its path from folderPaths), keep its other files as individual file-row selections, then re-sync the rows. Alternatively, run SyncTreeRowsFromFiles after the apply so the UI always shows the effective set.

[MEDIUM] Data loss (bulk conflict resolution without confirmation)
File: PickleGit/ViewModels/RepositoryViewModel.Rebase.cs:130
Issue: Take Ours and Take Theirs now apply to the whole multi-selection, or to every conflicted file under a right-clicked folder (ConflictTargets via ResolveTargets). `git checkout --ours/--theirs` overwrites each working file, including any partial hand-resolution in progress, and then stages it, with no prompt. Before this change the blast radius was one explicitly clicked file. MarkResolvedAsync (line 159) likewise now `git add`s every conflicted file in a folder, including files still full of conflict markers, without a prompt.
Fix: When targets.Count > 1, show DialogService.Confirm listing the files (or the count plus the first few names) before running checkout/add. For Mark Resolved, consider warning when a target still contains `<<<<<<<` markers.

[MEDIUM] Memory / executor blocking (binary conflicts)
File: PickleGit/Services/GitService.cs:1325
Issue: GetConflictStageBytes fully loads all three stage blobs, up to 50 MB each, whenever any stage is binary to libgit2. That includes genuinely binary assets (images, archives, DLLs) that RebuiltConflict.Prepare then rejects in TryDecode. OpenMergeEditorCoreAsync (Rebase.cs:325) collects every file's stages into one dictionary before any are decoded, so a merge with many conflicting binary assets holds up to ~150 MB per file at once and blocks the executor while reading.
Fix: Before loading, sniff the first ~8 KB of each stage blob (blob.GetContentStream) with TextDecoding's encoding detection and skip non-decodable content, returning a "binary" marker so the editor still shows its warning. Alternatively, process files one at a time (read, Prepare, discard bytes) instead of building the whole map first.

[MEDIUM] Performance (decoded-text fallback reads whole binaries)
File: PickleGit/Services/GitService.cs:1216
Issue: DecodedTextFallback runs for every binary diff except images. It calls oldBytes() and newBytes(), which read the entire index blob and working file (File.ReadAllBytes) before DetectEncoding even looks at the first 8000 bytes. Selecting a large binary (zip, mp4, dll) in Staged, Unstaged or history therefore reads up to two full copies of it on the executor thread, stalling every other git operation. A file over 2 GB throws and is caught, but only after the I/O.
Fix: Apply a size cap like MaxRebuildBlobBytes (blob.Size / FileInfo.Length) and/or sniff a prefix with the same NUL-parity check before doing the full read.

[LOW] Data integrity (lossy ANSI fallback on save)
File: PickleGit/ViewModels/MergeConflictEditorViewModel.cs:1130
Issue: TextDecoding falls back to Encoding.Default (system ANSI, replacement fallback) for NUL-free, non-UTF-8 content (TextDecoding.cs:108). If "ours" decodes as ANSI while "theirs" is UTF-16 (one branch re-encoded the file), the rebuilt conflict saves with ours' ANSI encoding. Characters from theirs that ANSI can't represent are silently replaced with '?'. Encoding.Default never throws, so the EncoderFallbackException path in TrySave doesn't catch it.
Fix: In RebuiltConflict, when the stage encodings differ, or when ours is the ANSI fallback, use a throwing encoder (Encoding.GetEncoding(cp, EncoderFallback.ExceptionFallback, ...)) or block Save with a warning.

[LOW] Performance (process spawn per diff load)
File: PickleGit/Services/GitService.cs:1238
Issue: HasContentConversionAttributes spawns `git check-attr` synchronously on the executor every time a decoded-text Staged/Unstaged diff loads, including every reload after each hunk stage. The pattern is established, but it costs a process launch per selection.
Fix: Cache the result per path for the current refresh cycle, or invalidate the cache on watcher events touching .gitattributes.

[LOW] Correctness (pre-existing path now reachable with rebuilt conflicts)
File: PickleGit/ViewModels/MergeConflictEditorViewModel.cs:581
Issue: The `!File.Exists(absolutePath)` early return runs before the `rebuilt` branch, so rebuilt.CanSave and Warning are never applied when the working file is missing. RebuiltConflict.Prepare had already classified that case as "changed, CanSave=false", but the VM keeps CanSave=true with an empty document, so Save & Stage would write an empty file and resolve the conflict to it.
Fix: Apply ConflictWarning/CanSave from `rebuilt` before the File.Exists guard, or set CanSave=false in that guard.

[LOW] Code reuse
File: PickleGit/ViewModels/RepositoryViewModel.Rebase.cs:108
Issue: QuotedPathChunks duplicates GitService.ChunkPathsByLength (GitService.cs:931/2053) with a different limit. Its progress label also repeats the same "N files" text for each chunk.
Fix: Reuse ChunkPathsByLength (expose it internally) so the command-line limit lives in one place.

[LOW] Dead code
File: PickleGit/ViewModels/RepositoryViewModel.Diff.cs:280
Issue: IsDecodedTextDiff (and the PropertyChanged raised for it) is not bound or read anywhere in XAML or code.
Fix: Remove it, or bind it to a visible "decoded from UTF-16" hint if one is intended.

[LOW] Test coverage
File: PickleGit/Services/LineDiff.cs:599
Issue: LineDiff, ThreeWayMerge, TextDecoding and DecodedStagingService are pure, deterministic algorithms with non-trivial edge cases: no-EOL handling, direction-dependent line staging, BOM-less UTF-16 sniffing, and cost-limit splitting. They currently have no automated tests (the repo has no test project).
Fix: When a test project is introduced, start with these four, since they are the cheapest high-value targets (round-trip: Apply(stage all) == new bytes; Apply(unstage all) == old bytes).

[INFO] Feature parity
File: PickleGit/Services/GitService.cs:1105
Issue: The two-commit GetFileDiff(shaA, shaB, ...) used by the branch Compare view didn't get DecodedTextFallback, so UTF-16 files there still show only the "Binary file" notice. Every other diff entry point now decodes.
Fix: Wrap its result in DecodedTextFallback(..., stageable: false) for consistency.

[INFO] Commit format
File: (branch) adhoc-conflict-staging-diff-fixes
Issue: There are no commits on the branch yet (all changes are uncommitted), so the commit-format checklist is N/A. The change spans several areas: merge editor, tree selection/context menus, decoded diff/staging, and 3-way rebuild. A single commit would need the complex multi-area tier, or the work should be split into separate commits.
Fix: Split into atomic commits per commit-format.md when shipping.

---

## Summary

CRITICAL: 0  HIGH: 0  MEDIUM: 5  LOW: 6  INFO: 2

Verdict: READY TO MERGE (no CRITICAL/HIGH). Still, the bulk Take Ours/Theirs confirmation and the tree-selection performance and deselect issues are worth fixing before shipping.
