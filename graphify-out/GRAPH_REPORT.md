# Graph Report - PickleGit  (2026-11-03)

## Corpus Check
- 262 files · ~292,401 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 8 file(s) not represented in the graph (top: (none) 3, .lock 1, .iss 1)

## Summary
- 4582 nodes · 8405 edges · 234 communities (163 shown, 71 thin omitted)
- Extraction: 94% EXTRACTED · 6% INFERRED · 0% AMBIGUOUS · INFERRED: 540 edges (avg confidence: 0.83)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `4e511605`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- RepositoryViewModel
- system
- UserControl
- UserControl
- UserControl
- GitService
- Window
- UserControl
- AppViewModel
- GraphNode
- .Build
- SyntaxHighlighter
- .RefreshAsync
- .RefreshOnceAsync
- Architecture Rules
- SettingsData
- Window
- NoLigaturesTypographyProperties
- DiffView
- DiffTextSelectionController
- DataTemplate
- DecodedText
- RepositoryWatcher
- AppSettings
- CommitInfo
- MergeConflictFileViewModel
- Window
- Task
- CreatePullRequestDialog
- IFileOpenDialog
- .ShowError
- InteractiveRebaseViewModel
- github.sh
- MergeConflictEditorWindow
- PickleGit Claude Code Guide
- ConflictBlockViewModel
- CommitListView
- FileChange
- ResourceDictionary
- .SideBySideListView_PreviewMouseLeftButtonDown
- MergeConflictBlock
- SidebarRowKind
- SettingsWindow
- ConflictResultListView
- BranchNodeViewModel
- TextPromptDialog
- MergeConflictSessionViewModel
- SidebarView
- CloneDialog
- FileDiffResult
- Help: Context Menus
- CommandPaletteWindow
- Brush
- Window
- RemoteDialog
- PaneFindState
- CredentialStore
- CultureInfo
- SidebarRowTemplateSelector
- DiffLine
- HostingViewModel
- Plan: Bitbucket OAuth credential retry
- ListViewMultiSelectBehavior
- Differ
- ConflictPaneItem
- HelpWindow
- Help: Settings & Preferences
- BisectState
- GitCli
- Plan: Conflict/staging/diff fixes
- ConfirmDialog
- Roadmap Phase 1 - Quick wins
- .RunAsync
- MainWindow
- RowText
- DiffChangeMapControl
- Audit & Close
- PickleGit v1.0.24 Release Notes
- Window
- .HandleFindBoxKeyDown
- PickleGit Claude Workflow Team Guide
- ShortcutManager
- PullRequestInfo
- RowText
- /pkl:plan
- .OnStartup
- Plan: Diff/Conflict/Blame UI fixes
- ConflictState
- .Apply
- SideBySideItem
- DiffHunk
- Workflow Rules
- CommitGraphCell
- RefLabel
- StagingPatchOp
- BitbucketCloudProvider
- GitLabProvider
- CredentialsDialog
- Review: adhoc-conflict-staging-diff-fixes
- BlameLine
- Unstaged Files List
- ConflictResultItem
- AvatarService
- ValueConverters.cs
- DetachedHeadConverter
- Diff View Screenshot
- GitHubProvider
- NewBranchDialog
- StagedListView
- .HandleFindBoxKeyDown
- Commit Graph Screenshot
- Main Window Overview Screenshot
- IHostingProvider
- Retro Log (pkl:retro process improvements)
- /pkl:create-branch - Branch Setup
- GitKraken-Class Master Plan
- checkpoint.sh
- Codebase Design Skill
- Decouple Background Refreshes Plan
- Building PickleGit Changelog
- UserControl
- /pkl:commit - Commit with Correct Format (SHIP flow)
- Help: The Diff View
- Plan: LFS clean filter staging
- .ShowSection_Click
- frontend-architect agent (WPF/MVVM)
- Roadmap Phase 4 - Infrastructure & architecture
- plan-frontmatter.sh
- ADR 0001 Hybrid Git Backend
- ADR 0003 No Third-Party Controls
- PickleGit.csproj
- Provider-Native PRs
- ReflogEntry
- RepositoryAccount.cs
- UI Performance Improvements Plan
- TextBox
- AggregatedFileTreeRow
- Plan File (.plans/gh-N.md)
- LigatureSuppressingColorizer
- build-installer.ps1
- PickleGit Master Plan
- Default Identity
- BaseViewModel
- RefreshScope
- Window
- MainTabControl
- 4.4 RepositoryViewModel decomposition (partial classes, StagingService, WorktreeService, HostingViewModel)
- FileTreeRow
- ADR 0002 No DI Container
- Plan: Icon font fallback
- ADR 0004 Thumb-Based Pane Resizing
- ButtonChrome
- BoolToVisibilityConverter
- Staging & Picker Polish
- Create Branch Dialog
- .DwmSetWindowAttribute
- SearchBox
- DateFormatConverter
- Closing the List
- RowRoot
- .ThemeBox_SelectionChanged
- SidebarSearchClearButton
- hitl-loop.template.sh
- NullToVisibilityConverter
- FileSortMode
- Border
- Op
- ExistenceChoice
- SidebarList
- .SidebarSearchBox_TextChanged
- CommitList
- Close the Gaps Skill
- ConflictResolution
- .RunBackgroundFetchPassAsync
- AggregatedFileChange
- TabScrollLeftButton
- UndoEntry
- BlameSearchHighlighter
- /pkl:remove-irrelevant-comments
- diff-profile.sh
- plan-drift-check.sh
- Copilot Instructions (Azure rules)
- HeaderPanel
- PART_SelectedContentHost
- PART_TabScroller
- TabBusyStoryboard
- TabDragCanvas
- TabDragGhostLabel
- HelpMarkdownStyles.xaml
- PaletteDark.xaml
- PaletteLight.xaml
- Bd
- GhostRefsHost
- HeaderScroller
- learning-guide agent
- performance-engineer agent
- requirements-analyst agent
- root-cause-analyst agent
- system-architect agent
- technical-writer agent
- WordDiffHighlighter
- BulkObservableCollection
- Plan: adhoc-background-auto-fetch — Background auto-fetch of all open tabs
- ConflictResultEditBox
- .Warn
- .Load
- Help: The Commit Graph
- App.xaml
- TokenKind
- Language
- graphify-guard.sh
- adhoc-background-auto-fetch.report.md

## God Nodes (most connected - your core abstractions)
1. `RepositoryViewModel` - 561 edges
2. `GitService` - 157 edges
3. `UserControl` - 132 edges
4. `SettingsData` - 113 edges
5. `AppViewModel` - 112 edges
6. `UserControl` - 112 edges
7. `UserControl` - 101 edges
8. `Window` - 98 edges
9. `UserControl` - 90 edges
10. `Window` - 73 edges

## Surprising Connections (you probably didn't know these)
- `Step 1: Auto-fetch interval setting ✓` --references--> `AppSettings`  [INFERRED]
  .plans/adhoc-background-auto-fetch.md → PickleGit/Services/AppSettings.cs
- `Step 1: Auto-fetch interval setting ✓` --references--> `SettingsData`  [INFERRED]
  .plans/adhoc-background-auto-fetch.md → PickleGit/Services/AppSettings.cs
- `Requirements` --references--> `AppViewModel`  [INFERRED]
  .plans/adhoc-background-auto-fetch.md → PickleGit/ViewModels/AppViewModel.cs
- `Step 3: Scheduler in AppViewModel + startup hook ✓` --references--> `AppViewModel`  [INFERRED]
  .plans/adhoc-background-auto-fetch.md → PickleGit/ViewModels/AppViewModel.cs
- `Requirements` --references--> `RepositoryViewModel`  [INFERRED]
  .plans/adhoc-background-auto-fetch.md → PickleGit/ViewModels/RepositoryViewModel.Bisect.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Busy state and background refresh pipeline** — picklegit_plans_quiet_background_refresh_isbusy_exclusive_lock, picklegit_plans_quiet_background_refresh_tryenterbusyscope, picklegit_plans_quiet_background_refresh_repositorywatcher, picklegit_plans_quiet_background_refresh_gitexecutor, picklegit_plans_quiet_background_refresh_silent_refresh_tier [EXTRACTED 1.00]
- **CLI routing for git fidelity** — plans_adhoc_lfs_clean_filter_staging_stagecli, plans_adhoc_lfs_clean_filter_staging_cli_routed_smudge_side_operations, plans_adhoc_lfs_clean_filter_staging_gitservice_reopen, picklegit_claude_cligitservice, plans_adhoc_lfs_clean_filter_staging_git_lfs_clean_filter [EXTRACTED 1.00]
- **Deep Module Vocabulary** — claude_skills_codebase_design_skill_deep_module, claude_skills_codebase_design_skill_seam, claude_skills_codebase_design_skill_adapter, claude_skills_codebase_design_skill_leverage, claude_skills_codebase_design_skill_locality [EXTRACTED 1.00]
- **Dialogs fixed with star-row/ScrollViewer pattern** — plans_adhoc_dialog_button_clipping_confirmdialog, plans_adhoc_dialog_button_clipping_errordialog, plans_adhoc_dialog_button_clipping_textpromptdialog, plans_adhoc_dialog_button_clipping_star_row_scrollviewer_pattern [EXTRACTED 1.00]
- **Domain-model doc maintenance (grilling + domain-modeling + ADR/CONTEXT)** — claude_skills_grill_with_docs_skill, claude_skills_grilling_skill, claude_skills_domain_modeling_skill, claude_skills_domain_modeling_adr_format, claude_skills_domain_modeling_context_format [EXTRACTED 1.00]
- **GitHub issue commands built on github.sh** — claude_commands_pkl_create_issue_github_sh, claude_commands_pkl_create_issue_pkl_create_issue, claude_commands_pkl_fetch_issue_pkl_fetch_issue, claude_commands_pkl_issue_close_pkl_issue_close, claude_commands_pkl_issue_attach_pkl_issue_attach [EXTRACTED 1.00]
- **Hybrid backend call chain** — docs_adr_0001_hybrid_git_backend_gitservice, docs_adr_0001_hybrid_git_backend_cligitservice, docs_adr_0001_hybrid_git_backend_gitcli, docs_adr_0001_hybrid_git_backend_gitexecutor, docs_adr_0001_hybrid_git_backend_libgit2sharp [EXTRACTED 1.00]
- **Hybrid Git Execution Pipeline** — claude_rules_architecture_repositoryviewmodel, claude_rules_architecture_gitexecutor, claude_rules_architecture_gitservice, claude_rules_architecture_cligitservice, claude_rules_architecture_gitcli [EXTRACTED 1.00]
- **Main Window Shell Layout** — picklegit_resources_help_screenshots_main_window_overview_toolbar, picklegit_resources_help_screenshots_main_window_overview_repo_tabs, picklegit_resources_help_screenshots_main_window_overview_sidebar, picklegit_resources_help_screenshots_main_window_overview_commit_list, picklegit_resources_help_screenshots_main_window_overview_status_bar [EXTRACTED 1.00]
- **Commit identity resolution** — picklegit_resources_help_screenshots_settings_window_identity_precedence, picklegit_resources_help_screenshots_settings_window_default_identity, picklegit_resources_help_screenshots_settings_window_repository_override [EXTRACTED 1.00]
- **Commit composition controls (message guide, identity, amend, commit/commit & push)** — picklegit_resources_help_screenshots_staging_commit_panel_subject_length_guide, picklegit_resources_help_screenshots_staging_commit_panel_committing_as_identity, picklegit_resources_help_screenshots_staging_commit_panel_amend_last_commit, picklegit_resources_help_screenshots_staging_commit_panel_commit_button, picklegit_resources_help_screenshots_staging_commit_panel_commit_and_push_button [EXTRACTED 1.00]
- **SHIP phase flow (review-pr, commit, QA list, issue comment, close, retro)** — claude_commands_pkl_commit_pkl_commit, claude_commands_pkl_commit_pkl_review_pr, claude_commands_pkl_commit_pkl_qa_list, claude_commands_pkl_commit_pkl_issue_comment, claude_commands_pkl_issue_close_pkl_issue_close, claude_commands_pkl_commit_pkl_retro [EXTRACTED 1.00]
- **Idea to Dev-Ready Plan Pipeline** — claude_docs_prd_workflow_guide_grill_me, claude_docs_prd_workflow_guide_to_prd, claude_docs_prd_workflow_guide_to_issues, claude_docs_prd_workflow_guide_close_the_gaps, claude_commands_pkl_plan [EXTRACTED 1.00]
- **Virtualized Diff + Async Loading Performance Architecture** — claude_plans_lovely_kindling_conway_flat_diff_items, claude_plans_lovely_kindling_conway_async_diff_loading, claude_plans_steady_splashing_scott_d2_git_executor, claude_plans_steady_splashing_scott_d7_custom_syntax_highlighter [INFERRED 0.75]
- **Live theme switching pitfalls** — picklegit_claude_app_applytheme, picklegit_claude_dynamicresource_for_theme_brushes, picklegit_claude_style_derived_foreground_override_fails_to_reach_button_content, picklegit_claude_templatebinding_cannot_target_attached_property_in_setter_value, picklegit_themes_darktheme [INFERRED 0.75]
- **Bitbucket OAuth authentication fix chain** — plans_adhoc_bitbucket_oauth_credential_retry_ensurecredentialsasync, plans_adhoc_bitbucket_oauth_credential_retry_tryautoresolvecredential, plans_adhoc_bitbucket_oauth_credential_retry_git_credential_fill, plans_adhoc_bitbucket_oauth_bearer_header_git_credential_manager, plans_adhoc_bitbucket_oauth_bearer_header_gitcli_hasconfiguredcredentialhelper, plans_adhoc_bitbucket_oauth_bearer_header_buildhttpauthenv [INFERRED 0.85]
- **Credential Safety Flow** — claude_rules_security_no_credentials_in_logs, claude_reviews_fix_bitbucket_oauth_bearer_header_gitcli_stripuserinfo, claude_rules_architecture_credentialstore, claude_reviews_fix_bitbucket_oauth_bearer_header_stale_credential_purge_bug [INFERRED 0.85]
- **Diff view virtualization pitfalls** — picklegit_claude_flatten_nested_itemscontrols_into_single_virtualized_list, picklegit_claude_virtualizingstackpanel_mis_positions_variable_height_rows, picklegit_claude_diffitemtemplateselector_type_check_no_ops_for_sidebysideitem, picklegit_claude_diffitemtemplateselector, picklegit_claude_previewmouseleftbuttondown_handled_swallows_row_button_clicks [INFERRED 0.85]
- **Commit history row composition** — picklegit_resources_help_screenshots_commit_graph_ref_badges, picklegit_resources_help_screenshots_commit_graph_graph_column, picklegit_resources_help_screenshots_commit_graph_uncommitted_changes_row [INFERRED 0.85]
- **Hunk staging workflow** — picklegit_resources_help_screenshots_diff_view_unstaged_files_panel, picklegit_resources_help_screenshots_diff_view_diff_pane, picklegit_resources_help_screenshots_diff_view_stage_hunk_button, picklegit_resources_help_screenshots_diff_view_staged_files_panel, picklegit_resources_help_screenshots_diff_view_commit_message_box [INFERRED 0.85]
- **History Visualization** — picklegit_resources_help_screenshots_main_window_overview_commit_graph, picklegit_resources_help_screenshots_main_window_overview_ref_badges, picklegit_resources_help_screenshots_main_window_overview_uncommitted_changes_row [INFERRED 0.85]
- **Stage-then-commit workflow (right panel, top-to-bottom: staged, unstaged, compose)** — picklegit_resources_help_screenshots_staging_commit_panel_unstaged_files_list, picklegit_resources_help_screenshots_staging_commit_panel_staged_files_list, picklegit_resources_help_screenshots_staging_commit_panel_commit_message_box, picklegit_resources_help_screenshots_staging_commit_panel_commit_button [INFERRED 0.85]
- **PLAN-BUILD-SHIP Plan-File Lifecycle** — claude_commands_pkl_plan, claude_commands_pkl_plan_plan_file, claude_scripts_checkpoint, claude_commands_pkl_plan_deviation_register, claude_commands_pkl_plan_verification_stubs, claude_commands_pkl_retro, claude_commands_pkl_review_pr [INFERRED 0.85]
- **Hosting PR providers** — picklegit_docs_provider_native_prs_ihostingprovider, picklegit_docs_provider_native_prs_githubprovider, picklegit_docs_provider_native_prs_gitlabprovider, picklegit_docs_provider_native_prs_bitbucketcloudprovider [INFERRED 0.85]
- **Verification discipline for BUILD steps** — claude_hard_gate_verification_stubs, claude_hard_gate_build_step_checkpoint, claude_retro_log_live_evidence_for_manual_stubs, claude_retro_log_checkpoint_auto_ticks_stubs, claude_agents_self_review_four_self_check_questions [INFERRED 0.85]
- **Three-pane repository layout** — picklegit_resources_help_screenshots_commit_graph_sidebar, picklegit_resources_help_screenshots_commit_graph_commit_list, picklegit_resources_help_screenshots_commit_graph_commit_detail_panel [INFERRED 0.95]

## Communities (234 total, 71 thin omitted)

### Community 0 - "RepositoryViewModel"
Cohesion: 0.01
Nodes (240): RepositoryViewModel, BisectBadCommand, BisectGoodCommand, BisectInfo, BisectResetCommand, BisectSkipCommand, CanStartBisect, HasBisect (+232 more)

### Community 1 - "system"
Cohesion: 0.05
Nodes (14): PickleGit, PickleGit.Services.Hosting, PickleGit.Controls, PickleGit.Services.Highlighting, PickleGit.Services.Git, PickleGit.Views, PickleGit.Converters, PickleGit.Behaviors (+6 more)

### Community 2 - "UserControl"
Cohesion: 0.02
Nodes (123): AbortOperationCommand, AggregatedFiles, AggregatedFiles.Count, AggregatedFilesTreeRows, AuthorEmail, BisectBadCommand, BisectGoodCommand, BisectInfo.CurrentSha (+115 more)

### Community 3 - "UserControl"
Cohesion: 0.02
Nodes (93): AuthorDate, BlameFind.IsOpen, BlameFind.NextCommand, BlameFind.PrevCommand, BlameFind.Status, BlameLines, CloseDiffPaneCommand, BoolToGridLength (+85 more)

### Community 4 - "UserControl"
Cohesion: 0.02
Nodes (85): AheadBy, Author, BehindBy, BranchFilterMode, BranchInfo, IndentToMargin, StarMenuLabel, DataContext.CheckoutBranchCommand (+77 more)

### Community 5 - "GitService"
Cohesion: 0.05
Nodes (10): GitService, Cli, Executor, GitDirectory, IsHeadDetached, IsOpen, RepoPath, WorkingDirectory (+2 more)

### Community 6 - "Window"
Cohesion: 0.03
Nodes (79): ., AllMineCheckedWholeFile, AllTheirsCheckedWholeFile, BlockStatusLabel, BlockVm.AllOursChecked, BlockVm.AllTheirsChecked, BlockVm.HasBase, BlockVm.ResetCommand (+71 more)

### Community 7 - "UserControl"
Cohesion: 0.03
Nodes (70): Commit.AuthorEmail, Commit.AuthorName, Commit.BisectMark, Commit.CommitterDate, Commit.IsUncommitted, Commit.Message, Commit.MessageShort, Commit.OverflowRefCount (+62 more)

### Community 8 - "AppViewModel"
Cohesion: 0.03
Nodes (68): AppViewModel, ActiveTab, AddEmptyTabCommand, AutoFetchIntervalMinutes, CanToggleAuthor, CanToggleBranchTag, CanToggleCommitDesc, CanToggleDateTime (+60 more)

### Community 9 - "GraphNode"
Cohesion: 0.13
Nodes (15): GraphEdge, Color, FromLane, IsMerge, ToLane, GraphLayout, GraphNode, Commit (+7 more)

### Community 10 - ".Build"
Cohesion: 0.32
Nodes (3): AggregatedNode, FileTreeBuilder, Node

### Community 12 - ".RefreshAsync"
Cohesion: 0.10
Nodes (5): GitCliOptions, Env, Progress, StdIn, Requirements

### Community 13 - ".RefreshOnceAsync"
Cohesion: 0.05
Nodes (38): BranchInfo, AheadBy, BehindBy, DisplayName, FullName, IsHead, IsRemote, IsStarred (+30 more)

### Community 14 - "Architecture Rules"
Cohesion: 0.06
Nodes (44): AvalonEdit SearchPanel, ReselectSameCommit, Review adhoc-diff-conflict-blame-ui-fixes, ErrorDialog Star Row Squeeze, Review adhoc-fix-dialog-button-clipping, CliGitService.Quote, Review adhoc-lfs-clean-filter-staging, StageCli git add Staging (+36 more)

### Community 15 - "SettingsData"
Cohesion: 0.04
Nodes (53): SettingsData, ActiveRepoPath, AutoFetchIntervalMinutes, BranchFilterModeByRepo, ColAuthor, ColBranchTag, ColCommitDesc, ColDateTime (+45 more)

### Community 16 - "Window"
Cohesion: 0.04
Nodes (51): ActiveTab.CanCancel, ActiveTab.CancelOperationCommand, ActiveTab.CanUndo, ActiveTab.CreateBranchCommand, ActiveTab.CurrentBranch, ActiveTab.DetachedHeadTooltip, ActiveTab.FetchAllCommand, ActiveTab.FetchCommand (+43 more)

### Community 17 - "NoLigaturesTypographyProperties"
Cohesion: 0.04
Nodes (44): NoLigaturesTypographyProperties, AnnotationAlternates, Capitals, CapitalSpacing, CaseSensitiveForms, ContextualAlternates, ContextualLigatures, ContextualSwashes (+36 more)

### Community 20 - "DiffTextSelectionController"
Cohesion: 0.13
Nodes (3): DiffTextSelectionController, HasSelection, IsSelecting

### Community 21 - "DataTemplate"
Cohesion: 0.10
Nodes (20): AggregatedFileTreeRowTemplateSelector, FileTemplate, FolderTemplate, ConflictPaneItemTemplateSelector, BlockLineTemplate, BlockToolbarTemplate, ContextTemplate, ConflictResultItemTemplateSelector (+12 more)

### Community 22 - "DecodedText"
Cohesion: 0.06
Nodes (25): DecodedText, Encoding, Lines, MissingFinalNewline, Preamble, SourceBytes, Terminators, TextDecoding (+17 more)

### Community 23 - "RepositoryWatcher"
Cohesion: 0.06
Nodes (7): GitExecutor, MonotonicClock, RepoChangeKind, Refs, WorkingDir, RepositoryWatcher, SuppressScope

### Community 25 - "CommitInfo"
Cohesion: 0.08
Nodes (22): CommitInfo, AuthorDate, AuthorDateRelative, AuthorEmail, AuthorName, BisectMark, CommitterDate, CommitterEmail (+14 more)

### Community 26 - "MergeConflictFileViewModel"
Cohesion: 0.05
Nodes (34): MergeConflictFileViewModel, AllMineCheckedWholeFile, AllTheirsCheckedWholeFile, BlockStatusLabel, CanSave, ConflictNavLabel, ConflictWarning, CurrentUnresolvedIndex (+26 more)

### Community 27 - "Window"
Cohesion: 0.06
Nodes (40): ActiveTab.AuthorEmail, ActiveTab.AuthorName, ActiveTab.InstallLfsCommand, ClearRepoIdentityCommand, ConfirmBeforeDiscard, DateFormat, DefaultAuthorEmail, DefaultAuthorName (+32 more)

### Community 28 - "Task"
Cohesion: 0.07
Nodes (14): SubmoduleInfo, IsDirty, IsInitialized, Name, Path, StatusLabel, Url, WorktreeInfo (+6 more)

### Community 29 - "CreatePullRequestDialog"
Cohesion: 0.06
Nodes (31): Description, HasStatusText, IsDraft, PrTitle, SelectedTargetBranch, SourceBranch, StatusText, SupportsDraft (+23 more)

### Community 30 - "IFileOpenDialog"
Cohesion: 0.07
Nodes (4): FileOpenDialogRCW, IFileOpenDialog, IShellItem, ShellFolderPicker

### Community 33 - "InteractiveRebaseViewModel"
Cohesion: 0.06
Nodes (26): RebaseTodoAction, Break, Drop, Edit, Fixup, Pick, Reword, Squash (+18 more)

### Community 34 - "github.sh"
Cohesion: 0.06
Nodes (42): /pkl:issue-comment, _api_call(), _api_call_gh(), _api_call_rest(), cmd_close(), cmd_comment(), cmd_create(), cmd_view() (+34 more)

### Community 36 - "PickleGit Claude Code Guide"
Cohesion: 0.09
Nodes (6): AppViewModel, CommitListView, LoadDiffAsync, PickleGit Claude Code Guide, RelayCommand, SyntaxHighlighter.TokenizeGeneric

### Community 37 - "ConflictBlockViewModel"
Cohesion: 0.07
Nodes (23): ConflictBlockViewModel, AllOursChecked, AllTheirsChecked, BaseOptions, Block, CurrentText, HasBase, IsResolvedEffective (+15 more)

### Community 38 - "CommitListView"
Cohesion: 0.09
Nodes (3): CommitListView, AppVm, RepoVm

### Community 39 - "FileChange"
Cohesion: 0.09
Nodes (10): FileChange, IsStaged, Kind, LinesAdded, LinesDeleted, OldPath, OursMissing, Path (+2 more)

### Community 40 - "ResourceDictionary"
Cohesion: 0.08
Nodes (30): (ctrl:ButtonChrome.HoverBackground), Foreground, IsDropDownOpen, Payload.IsHead, Bd, CheckMark, Chrome, ContentSite (+22 more)

### Community 41 - ".SideBySideListView_PreviewMouseLeftButtonDown"
Cohesion: 0.21
Nodes (6): FlatDiffItems, SideBySideItems, SideBySideLeftListView, SideBySideRightListView, UnifiedListView, ListView

### Community 42 - "MergeConflictBlock"
Cohesion: 0.08
Nodes (26): ConflictDocItem, Block, ContextText, Kind, ConflictDocItemKind, Block, Context, MergeConflictBlock (+18 more)

### Community 43 - "SidebarRowKind"
Cohesion: 0.08
Nodes (24): SidebarRow, IndentLevel, IsExpanded, Kind, Payload, SidebarRowKind, LocalBranchesHeader, LocalBranchGroup (+16 more)

### Community 44 - "SettingsWindow"
Cohesion: 0.13
Nodes (5): SelfHostedEntry, Domain, Kind, KindLabel, SettingsWindow

### Community 45 - "ConflictResultListView"
Cohesion: 0.17
Nodes (7): PaneItems, ResultItems, ConflictLeftListView, ConflictResultListView, ConflictRightListView, InvertBoolToVis, ListView

### Community 46 - "BranchNodeViewModel"
Cohesion: 0.09
Nodes (18): BranchNodeViewModel, AheadBy, BehindBy, BranchInfo, CanFetchBranch, Children, DisplayName, ExpansionChanged (+10 more)

### Community 47 - "TextPromptDialog"
Cohesion: 0.09
Nodes (21): CheckboxText, CheckboxVisibility, InputText, IsCheckboxChecked, PromptText, InputBox, Window, DialogTitle (+13 more)

### Community 48 - "MergeConflictSessionViewModel"
Cohesion: 0.10
Nodes (14): MergeConflictSessionViewModel, CloseCommand, CurrentFile, Files, LeftFind, OursCommitLabel, OursCommitTooltip, ResolvedCountLabel (+6 more)

### Community 50 - "CloneDialog"
Cohesion: 0.11
Nodes (17): LocalPath, RecurseSubmodules, BranchBox, HintText, PathBox, UrlBox, ValidationText, Window (+9 more)

### Community 51 - "FileDiffResult"
Cohesion: 0.12
Nodes (11): FileDiffResult, CanStagePartially, Hunks, IsBinary, IsDecodedText, NewText, OldText, ContentSource (+3 more)

### Community 52 - "Help: Context Menus"
Cohesion: 0.15
Nodes (14): CredentialStore, Phase 7 provider-native PR creation, Help: Pull Requests, Hosting personal access token, Pull request creation, Bisect workflow, Branch/commit comparison, Help: History Tools (+6 more)

### Community 53 - "CommandPaletteWindow"
Cohesion: 0.07
Nodes (20): Category, Gesture, AppCommandRegistry, PaletteCommand, Category, Command, CommandParameter, DisplayText (+12 more)

### Community 54 - "Brush"
Cohesion: 0.12
Nodes (6): AuthorAvatarColorConverter, BisectMarkToColorConverter, ColorToSolidBrushConverter, ConflictRowBackgroundConverter, FileChangeKindToColorConverter, ThemeBrushes

### Community 55 - "Window"
Cohesion: 0.10
Nodes (18): DetailsText, DetailsVisibility, IsChecked, DetailsToggle, Label, Window, BoolToVis, DialogTitle (+10 more)

### Community 56 - "RemoteDialog"
Cohesion: 0.10
Nodes (17): IsNameEditable, RemoteName, NameBox, UrlBox, Window, DialogTitle, HeaderText, OkText (+9 more)

### Community 57 - "PaneFindState"
Cohesion: 0.11
Nodes (9): FindNavigationHelper, PaneFindState, CurrentMatch, CurrentMatchRange, IsOpen, NextCommand, PrevCommand, SearchText (+1 more)

### Community 58 - "CredentialStore"
Cohesion: 0.20
Nodes (3): CREDENTIAL, CredentialStore, NativeMethods

### Community 59 - "CultureInfo"
Cohesion: 0.07
Nodes (22): AuthorInitialsConverter, BisectMarkToLabelConverter, BisectProgressTextConverter, BoolToGridLengthConverter, BulkActionLabelConverter, DiffLineForegroundConverter, DiscardBadgeTextConverter, DiscardTooltipConverter (+14 more)

### Community 60 - "SidebarRowTemplateSelector"
Cohesion: 0.11
Nodes (19): SidebarRowTemplateSelector, LocalBranchesHeaderTemplate, LocalBranchGroupTemplate, LocalBranchLeafTemplate, PullRequestsHeaderTemplate, PullRequestTemplate, RemoteBranchesHeaderTemplate, RemoteBranchGroupTemplate (+11 more)

### Community 61 - "DiffLine"
Cohesion: 0.29
Nodes (7): DiffLine, Content, HighlightSpans, Kind, NewLineNumber, OldLineNumber, SyntaxSpans

### Community 62 - "HostingViewModel"
Cohesion: 0.10
Nodes (12): HostingViewModel, CreatePullRequestCommand, HasHostingProvider, HostingProvider, HostingProviderConfigured, HostingProviderName, IsLoadingPullRequests, OpenPullRequestCommand (+4 more)

### Community 63 - "Plan: Bitbucket OAuth credential retry"
Cohesion: 0.16
Nodes (21): Plan: Bitbucket OAuth Bearer header, CliGitService.BuildHttpAuthEnv, CliGitService, EnsureCredentialsAsync, Git Credential Manager (GCM), GitCli.HasConfiguredCredentialHelper, Plan: Bitbucket OAuth Bearer header - run report, Plan: Bitbucket OAuth credential retry (+13 more)

### Community 64 - "ListViewMultiSelectBehavior"
Cohesion: 0.12
Nodes (3): ListViewMultiSelectBehavior, SelectedItems, NoopScope

### Community 67 - "ConflictPaneItem"
Cohesion: 0.11
Nodes (15): AlignedPair, LcsAligner, ConflictPaneItem, BlockVm, ContextNewLineNumber, ContextOldLineNumber, ContextText, Display (+7 more)

### Community 68 - "HelpWindow"
Cohesion: 0.11
Nodes (11): Bd, ContentViewer, TopicList, Window, HelpWindow, Topic, FileName, Title (+3 more)

### Community 69 - "Help: Settings & Preferences"
Cohesion: 0.15
Nodes (13): AppSettings, Command Palette, Help: Getting Started, Main window layout, Repository tabs, Fetch, Pull, Push, Force push with lease, Help: Syncing (+5 more)

### Community 70 - "BisectState"
Cohesion: 0.10
Nodes (18): BisectMark, Bad, Current, Good, None, Skip, BisectState, BadSha (+10 more)

### Community 71 - "GitCli"
Cohesion: 0.12
Nodes (9): GitCli, GitPathOverride, IsGitAvailable, GitCliResult, ErrorText, ExitCode, StdErr, StdOut (+1 more)

### Community 74 - "Plan: Conflict/staging/diff fixes"
Cohesion: 0.17
Nodes (20): Plan: Conflict manual-edit find bar, AvalonEdit SearchPanel, DiffSearchMatchBrush, FindMatchesInResult, MergeConflictEditorWindow, PaneFindState, Plan: Conflict manual-edit find bar - run report, ResultFind bar (+12 more)

### Community 75 - "ConfirmDialog"
Cohesion: 0.12
Nodes (15): CancelText, OkButton, Window, DialogTitle, HeaderText, MessageText, OkText, ConfirmDialog (+7 more)

### Community 76 - "Roadmap Phase 1 - Quick wins"
Cohesion: 0.12
Nodes (16): ADR 0003 No Third-Party Controls (AvalonEdit exception), 1.2 AvatarService thread-safety, 1.3 CredentialStore robustness, 1.9 Dialog consistency pass (theme resources, DialogService), 3.5 Hosting upgrades (GitHub Enterprise, self-managed GitLab), PickleGit Improvement Roadmap, 3.4 Merge editor upgrades (BASE pane, BOM-aware encoding), 1.1 Merge-editor wrong-block resolution (MergeConflictEditorViewModel.ResolveCurrent) (+8 more)

### Community 77 - ".RunAsync"
Cohesion: 0.14
Nodes (7): CliGitService, IsAvailable, WorkingDirectory, GitMergeMode, Default, FastForwardOnly, NoFastForward

### Community 79 - "RowText"
Cohesion: 0.11
Nodes (18): Content, Header, Left, Left.Kind, Left.OldLineNumber, Line, Line.Kind, Line.NewLineNumber (+10 more)

### Community 80 - "DiffChangeMapControl"
Cohesion: 0.13
Nodes (7): DiffChangeMapControl, RowKinds, DiffLineKind, Added, Context, Deleted, Header

### Community 81 - "Audit & Close"
Cohesion: 0.20
Nodes (18): CliGitService, DialogService, GitCli, GitService, GitService.Executor (GitExecutor), RepositoryWatcher, Audit & Close, Phase 0 Foundations (+10 more)

### Community 82 - "PickleGit v1.0.24 Release Notes"
Cohesion: 0.14
Nodes (17): Phase: Stability & Auth Hardening, Cancellation-token race, --cleanup=strip conflicts block loss, Committer-date sorting, Ctrl+G go to line, Git Credential Manager, Merge Conflict Editor, PickleGit v1.0.24 Release Notes (+9 more)

### Community 83 - "Window"
Cohesion: 0.12
Nodes (16): Action, CancelCommand, DataContext.CycleActionCommand, DataContext.MoveDownCommand, DataContext.MoveUpCommand, IsLoading, IsReword, Items (+8 more)

### Community 84 - ".HandleFindBoxKeyDown"
Cohesion: 0.16
Nodes (9): BlameFind.SearchText, SideBySideLeftFind.SearchText, SideBySideRightFind.SearchText, UnifiedFind.SearchText, BlameFindBox, SideBySideLeftFindBox, SideBySideRightFindBox, UnifiedFindBox (+1 more)

### Community 86 - "PickleGit Claude Workflow Team Guide"
Cohesion: 0.17
Nodes (12): /sc:cleanup, /sc:design, /sc:document, Doxygen-Specific Rules, /sc:estimate, /sc:explain, /sc:implement, /sc:improve (+4 more)

### Community 87 - "ShortcutManager"
Cohesion: 0.15
Nodes (6): ShortcutDescriptor, ShortcutManager, ShortcutEntry, DisplayName, GestureText, Id

### Community 88 - "PullRequestInfo"
Cohesion: 0.14
Nodes (10): PullRequestInfo, Author, DisplayRoute, DisplayTitle, Id, SourceBranch, State, TargetBranch (+2 more)

### Community 89 - "RowText"
Cohesion: 0.14
Nodes (16): Display, Display.Kind, LeftLine.Display, LeftLine.Display.Kind, RightLine.Display, RightLine.Display.Kind, SourceLine.Display, SourceLine.Display.Kind (+8 more)

### Community 90 - "/pkl:plan"
Cohesion: 0.17
Nodes (13): /pkl:plan, After Implementation Section, Spec / Acceptance Criteria Section, /sc:brainstorm, PRD Creation Workflow Guide, /close-the-gaps, /grill-me, /grill-with-docs (+5 more)

### Community 91 - ".OnStartup"
Cohesion: 0.24
Nodes (4): App, IsLightTheme, SplashStatusViewModel, StatusMessage

### Community 92 - "Plan: Diff/Conflict/Blame UI fixes"
Cohesion: 0.18
Nodes (13): DiffView, RepositoryViewModel, AvalonEdit SearchPanel, ConflictResultEditBox, DiffView.ScrollDiffItemToTop, Flat-mode Files changed tooltip, MergeConflictEditorWindow, NavigateHunk (+5 more)

### Community 93 - "ConflictState"
Cohesion: 0.12
Nodes (15): ConflictOperation, CherryPick, Merge, None, Rebase, Revert, ConflictState, ConflictedFiles (+7 more)

### Community 95 - "SideBySideItem"
Cohesion: 0.09
Nodes (11): DiffItem, Header, Hunk, Kind, Line, SideBySideItem, Header, Hunk (+3 more)

### Community 96 - "DiffHunk"
Cohesion: 0.13
Nodes (15): DiffHunk, HasLineSelection, Header, Lines, NewStart, OldStart, FileChangeKind, Added (+7 more)

### Community 97 - "Workflow Rules"
Cohesion: 0.14
Nodes (12): Bug Plan Guide, Feature Plan Guide, Investigation Plan Guide, Refactor Plan Guide, Script Plan Guide, Approval Grammar, Deviation Register, pkl:plan (+4 more)

### Community 100 - "StagingPatchOp"
Cohesion: 0.16
Nodes (5): StagingPatchOp, Discard, Stage, Unstage, StagingService

### Community 101 - "BitbucketCloudProvider"
Cohesion: 0.18
Nodes (6): BitbucketCloudProvider, Domain, IsConfigured, Owner, ProviderName, Slug

### Community 102 - "GitLabProvider"
Cohesion: 0.16
Nodes (7): GitLabProvider, Domain, IsConfigured, Owner, ProjectPath, ProviderName, Slug

### Community 103 - "CredentialsDialog"
Cohesion: 0.18
Nodes (10): PassBox, UsernameBox, ValidationText, Window, CredentialsDialog, Password, Username, PasswordBox (+2 more)

### Community 104 - "Review: adhoc-conflict-staging-diff-fixes"
Cohesion: 0.16
Nodes (12): /pkl:review-pr, Spawned Review Agent, Severity Summary + Verdict Format, /pkl:security-review, /pkl:static-analyze, Review: adhoc-conflict-staging-diff-fixes, GetConflictStageBytes Binary Memory, DecodedTextFallback Reads Whole Binaries (+4 more)

### Community 106 - "BlameLine"
Cohesion: 0.14
Nodes (9): BlameLine, AuthorDate, AuthorName, Content, IsBandAlt, LineNumber, MessageShort, Sha (+1 more)

### Community 107 - "Unstaged Files List"
Cohesion: 0.18
Nodes (14): Staging & Commit Panel Screenshot, Amend Last Commit Checkbox, Commit & Push Button, Commit Button, Commit Message Box, Committing-As Identity Line, Discard All (Trash) Button, File Status Badges (M / ?) (+6 more)

### Community 108 - "ConflictResultItem"
Cohesion: 0.14
Nodes (14): ConflictResultItem, BlockVm, Display, Kind, LineNumber, LineText, SourceLine, ConflictResultRowKind (+6 more)

### Community 110 - "ValueConverters.cs"
Cohesion: 0.13
Nodes (5): ConditionalGraphWidthConverter, CurrentFindMatchConverter, DiffLineBackgroundConverter, ShowHideWidthConverter, TaskbarProgressStateConverter

### Community 111 - "DetachedHeadConverter"
Cohesion: 0.21
Nodes (4): DetachedHeadConverter, Invert, ResolutionToVisibilityConverter, Invert

### Community 112 - "Diff View Screenshot"
Cohesion: 0.19
Nodes (13): Diff View Screenshot, Commit Message Composer, Unified Diff Pane, Diff Toolbar (Ignore whitespace, Show entire file, Unified/Side-by-side, Diff/History), Discard Hunk Button, File History Mode, Hunk Header Row, Main Toolbar (+5 more)

### Community 113 - "GitHubProvider"
Cohesion: 0.16
Nodes (7): GitHubProvider, ApiBase, Domain, IsConfigured, Owner, ProviderName, Slug

### Community 114 - "NewBranchDialog"
Cohesion: 0.21
Nodes (8): BranchName, NameBox, ValidationText, Window, NewBranchDialog, BranchName, TextBlock, TextBox

### Community 115 - "StagedListView"
Cohesion: 0.27
Nodes (12): EnumEqualsVis, FileViewMode, StagedFiles, StagedFileTreeRows, WorkingDirFiles, WorkingFileTreeRows, StagedListView, StagedTreeListView (+4 more)

### Community 116 - ".HandleFindBoxKeyDown"
Cohesion: 0.18
Nodes (7): DataContext.LeftFind.SearchText, DataContext.ResultFind.SearchText, DataContext.RightFind.SearchText, LeftFindBox, ResultFindBox, RightFindBox, TextBox

### Community 117 - "Commit Graph Screenshot"
Cohesion: 0.21
Nodes (12): Commit Graph Screenshot, Commit Detail Panel, Commit List, Dark Theme, Commit Graph Lanes, Local Branch Tree, Main Toolbar, Branch/Tag Badges (+4 more)

### Community 118 - "Main Window Overview Screenshot"
Cohesion: 0.21
Nodes (12): Main Window Overview Screenshot, Hierarchical Local Branch Tree, Commit Graph Column, Commit List, Dark Theme, Ref Badges, Fetch / Pull / Push Split Buttons, Repository Tabs (+4 more)

### Community 119 - "IHostingProvider"
Cohesion: 0.17
Nodes (8): GitRemoteUrlParser, HostingProviderFactory, IHostingProvider, Domain, IsConfigured, Owner, ProviderName, Slug

### Community 120 - "Retro Log (pkl:retro process improvements)"
Cohesion: 0.22
Nodes (5): quality-engineer agent, self-review agent, pkl:retro, Retro Log (pkl:retro process improvements), workflow.md (Session Resume / phases)

### Community 121 - "/pkl:create-branch - Branch Setup"
Cohesion: 0.22
Nodes (9): Plan file .plans/gh-<N>.md, /pkl:create-branch - Branch Setup, github.sh (gh CLI / GITHUB_TOKEN wrapper), /pkl:create-issue, /pkl:fetch-issue - GitHub Issue Intake, Plan type guides (bug/feature/refactor/investigation/script), Deviation Register, /pkl:handoff - Save Progress (+1 more)

### Community 122 - "GitKraken-Class Master Plan"
Cohesion: 0.22
Nodes (6): UI Performance Improvements Plan, DiffItem Model, FlatDiffItems Virtualized DiffView, GitKraken-Class Master Plan, Implementation Phases 0-6, Bulk Take Ours/Theirs Needs Confirmation

### Community 123 - "checkpoint.sh"
Cohesion: 0.38
Nodes (10): _add_register(), do_checkpoint(), final_report(), main(), _mutate_body(), _plan_path(), _report_path(), self_test() (+2 more)

### Community 124 - "Codebase Design Skill"
Cohesion: 0.31
Nodes (7): Deepening Guide, Dependency Categories, Adapter, Codebase Design Skill, Leverage, Locality, Seam

### Community 125 - "Decouple Background Refreshes Plan"
Cohesion: 0.25
Nodes (10): GitExecutor, GitExecutor, Phase: Background Refresh, Decoupled, AppViewModel tab-close guard, Decouple Background Refreshes Plan, GitExecutor, Ignored-path filter, IsBusy exclusive lock (+2 more)

### Community 126 - "Building PickleGit Changelog"
Cohesion: 0.18
Nodes (10): Building PickleGit Changelog, Phase: Conflicts & Undo, Phase: Core Operations, Phase: Diff & Staging, Phase: Foundations, Phase: Native Git Backend & Conflict Rewrite, Phase: Performance & Git LFS Workflow, Phase: Rebase & Drag-Drop (+2 more)

### Community 127 - "UserControl"
Cohesion: 0.20
Nodes (6): DataContext.CloneRepoCommand, DataContext.InitRepoCommand, DataContext.OpenRepoCommand, CommitDetailView, UserControl, NewTabPlaceholderView

### Community 128 - "/pkl:commit - Commit with Correct Format (SHIP flow)"
Cohesion: 0.24
Nodes (8): commit-format.md spec, /pkl:commit-only, /pkl:commit - Commit with Correct Format (SHIP flow), pkl:issue-comment, pkl:qa-list, pkl:review-pr, /pkl:issue-attach, /pkl:issue-close

### Community 130 - "Help: The Diff View"
Cohesion: 0.13
Nodes (13): DiffItemTemplateSelector, PatchBuilder.BuildLinePatch, Amend last commit, Help: Staging & Committing, Hunk and line staging, Read-only committer identity, Sign-off, Change-map strip (+5 more)

### Community 131 - "Plan: LFS clean filter staging"
Cohesion: 0.29
Nodes (7): ChunkPathsByLength, CLI-routed smudge-side operations, Git LFS clean filter, GitService.Reopen, Plan: LFS clean filter staging, Run report: LFS clean filter staging, StageCli

### Community 133 - ".ShowSection_Click"
Cohesion: 0.39
Nodes (8): AboutSectionButton, GeneralSectionButton, GitBehaviorSectionButton, IntegrationsSectionButton, ProfileSectionButton, ShortcutsSectionButton, UISectionButton, Button

### Community 134 - "frontend-architect agent (WPF/MVVM)"
Cohesion: 0.25
Nodes (4): frontend-architect agent (WPF/MVVM), architecture.md, /pkl:build - Compilation Check (MSBuild), PickleGit (WPF Git client)

### Community 135 - "Roadmap Phase 4 - Infrastructure & architecture"
Cohesion: 0.22
Nodes (8): security-engineer agent, PickleGit/CLAUDE.md implementation notes, 4.1 AppLog rolling file logger, 4.6 Force-push safety (--force-with-lease), 4.3 Incremental refresh (RefMask single history walk), Roadmap Phase 4 - Infrastructure & architecture, 4.7 Signature verification (GPG), 4.5 SSH diagnostics

### Community 136 - "plan-frontmatter.sh"
Cohesion: 0.50
Nodes (7): _atomic_awk(), cmd_self_test(), cmd_set_field(), cmd_set_psn(), main(), plan-frontmatter.sh script, _validate_fm()

### Community 137 - "ADR 0001 Hybrid Git Backend"
Cohesion: 0.39
Nodes (8): ADR 0001 Hybrid Git Backend, CliGitService, git.exe, GitCli, GitCli.IsGitAvailable, GitService, GitService.Reopen, LibGit2Sharp

### Community 138 - "ADR 0003 No Third-Party Controls"
Cohesion: 0.31
Nodes (8): ADR 0003 No Third-Party Controls, AvalonEdit, CommitGraphControl, FlatDiffItems, MergeConflictEditorWindow, SyntaxHighlighter, WordDiffHighlighter.SyntaxBrushes, DiffView virtualization

### Community 139 - "PickleGit.csproj"
Cohesion: 0.22
Nodes (7): net472, AvalonEdit (6.3.1.120), LibGit2Sharp (0.27.2), Markdig.Wpf (0.5.0.1), Microsoft.Xaml.Behaviors.Wpf (1.1.77), Newtonsoft.Json (13.0.3), Microsoft.NET.Sdk

### Community 140 - "Provider-Native PRs"
Cohesion: 0.31
Nodes (9): Phase: Hosting & Ecosystem, BitbucketCloudProvider, Branch context menu PR creation, Drag branch onto branch PR, GitHubProvider, GitLabProvider, IHostingProvider, Provider-Native PRs (+1 more)

### Community 141 - "ReflogEntry"
Cohesion: 0.22
Nodes (8): ReflogEntry, Index, Message, NewSha, OldSha, Selector, ShortNewSha, Timestamp

### Community 142 - "RepositoryAccount.cs"
Cohesion: 0.15
Nodes (12): BranchFilterMode, All, StarredOnly, DiffItemKind, HunkHeader, Line, DiffPaneMode, Diff (+4 more)

### Community 143 - "UI Performance Improvements Plan"
Cohesion: 0.39
Nodes (7): CommitGraphCell, CommitGraphControl, GitService, RefLabel, RepositoryViewModel, Sidebar TreeView virtualization, UI Performance Improvements Plan

### Community 145 - "TextBox"
Cohesion: 0.20
Nodes (10): AutoFetchIntervalMinutes, DefaultCommitLoadLimit, GitExePathOverride, AutoFetchIntervalBox, CommitLimitBox, GitPathBox, HostDomainBox, HostUsernameBox (+2 more)

### Community 146 - "AggregatedFileTreeRow"
Cohesion: 0.20
Nodes (10): AggregatedFileTreeRow, DisplayName, File, FullPath, IndentLevel, IsExpanded, Kind, FileTreeRowKind (+2 more)

### Community 147 - "Plan File (.plans/gh-N.md)"
Cohesion: 0.25
Nodes (7): Plan File (.plans/gh-N.md), /pkl:qa-list, Black-Box Test List, /pkl:retro, Retro Log (.claude/retro-log.md), /pkl:test-plan, Manual App Test Checklist

### Community 149 - "build-installer.ps1"
Cohesion: 0.36
Nodes (5): Inno Setup, Per-user no-admin install, PickleGit installer README, Setup.iss, Version.props

### Community 150 - "PickleGit Master Plan"
Cohesion: 0.25
Nodes (6): RepositoryWatcher, FileSystemWatcher instead of polling, Gap Analysis, Hosting integration (Bitbucket/GitHub/GitLab), PickleGit Master Plan, Two history walks per refresh

### Community 151 - "Default Identity"
Cohesion: 0.32
Nodes (8): Settings Window Screenshot, Dark Theme, Default Identity, Commit Identity Precedence, Profile Tab, This Repository's Override, Settings Dialog, Settings Tabs (General, Profile, UI, Git Behavior, Integrations, Shortcuts, About)

### Community 152 - "BaseViewModel"
Cohesion: 0.25
Nodes (7): BaseViewModel, ConflictFileListEntry, ExistenceBadge, FileChange, IsExistenceConflict, IsResolved, Path

### Community 153 - "RefreshScope"
Cohesion: 0.25
Nodes (8): RefreshScope, Branches, Full, History, Remotes, Stashes, Tags, WorkingDir

### Community 154 - "Window"
Cohesion: 0.29
Nodes (4): StatusMessage, InteractiveRebaseDialog, Window, SplashWindow

### Community 155 - "MainTabControl"
Cohesion: 0.29
Nodes (4): Tabs, MainTabControl, ActiveTab, TabControl

### Community 156 - "4.4 RepositoryViewModel decomposition (partial classes, StagingService, WorktreeService, HostingViewModel)"
Cohesion: 0.29
Nodes (6): refactoring-expert agent, GitService / CliGitService, Hybrid LibGit2Sharp + git.exe backend, RepositoryViewModel (one per tab), 2.1 Cancel long operations (CancellationToken in GitCli.RunAsync), 4.4 RepositoryViewModel decomposition (partial classes, StagingService, WorktreeService, HostingViewModel)

### Community 157 - "FileTreeRow"
Cohesion: 0.13
Nodes (9): FileTreeRow, DisplayName, File, Files, FullPath, HasConflicts, IndentLevel, IsExpanded (+1 more)

### Community 158 - "ADR 0002 No DI Container"
Cohesion: 0.33
Nodes (5): ADR 0002 No DI Container, DataContext child ViewModel property, DataTemplate ViewModel resolution, MEF, VB InputBox prompts

### Community 161 - "Plan: Icon font fallback"
Cohesion: 0.48
Nodes (7): CommitGraphControl, Bootstrap Icons migration (deferred), Plan: Icon font fallback, Run report: Icon font fallback, Segoe Fluent Icons, Segoe MDL2 Assets fallback, SidebarView

### Community 163 - "ADR 0004 Thumb-Based Pane Resizing"
Cohesion: 0.53
Nodes (4): ADR 0004 Thumb-Based Pane Resizing, AppSettings pane-width persistence, GridSplitter, Phase: Pane Resizing, Rebuilt

### Community 165 - "BoolToVisibilityConverter"
Cohesion: 0.33
Nodes (3): BoolToVisibilityConverter, FalseValue, Invert

### Community 166 - "Staging & Picker Polish"
Cohesion: 0.33
Nodes (6): Phase: Staging & Workflow Polish, ComboBox Bug, ListViewMultiSelectBehavior, Multi-Select Everywhere, Staging & Picker Polish, Unstage All

### Community 167 - "Create Branch Dialog"
Cohesion: 0.40
Nodes (6): Create Branch Dialog Screenshot, Branch Name Field, Cancel Button, Create Branch Dialog, Create Button (Accent), Dark Theme Dialog Styling

### Community 169 - "SearchBox"
Cohesion: 0.40
Nodes (3): SearchText, SearchBox, TextBox

### Community 172 - "Closing the List"
Cohesion: 0.40
Nodes (5): Closing the List, Diff & History, Hardening, Interactive Rebase, Settings & Shortcuts

### Community 174 - ".ThemeBox_SelectionChanged"
Cohesion: 0.40
Nodes (3): SelfHostedKindBox, ThemeBox, ComboBox

### Community 176 - "hitl-loop.template.sh"
Cohesion: 0.83
Nodes (3): capture(), hitl-loop.template.sh script, step()

### Community 178 - "FileSortMode"
Cohesion: 0.22
Nodes (9): FileSortMode, NameAsc, NameDesc, PathAsc, PathDesc, Status, FileSortModeOption, Label (+1 more)

### Community 179 - "Border"
Cohesion: 0.50
Nodes (4): Bd, TabBorder, TabDragGhost, Border

### Community 180 - "Op"
Cohesion: 0.50
Nodes (4): Op, Delete, Equal, Insert

### Community 181 - "ExistenceChoice"
Cohesion: 0.50
Nodes (4): ExistenceChoice, DeleteFile, KeepFile, Undecided

### Community 184 - "CommitList"
Cohesion: 0.67
Nodes (3): GraphNodes, CommitList, ListView

### Community 187 - "ConflictResolution"
Cohesion: 0.29
Nodes (7): ConflictResolution, Base, Both, BothReverse, Ours, Theirs, Unresolved

### Community 189 - "AggregatedFileChange"
Cohesion: 0.40
Nodes (5): AggregatedFileChange, CommitCount, Kind, Path, StatusLabel

### Community 192 - "TabScrollLeftButton"
Cohesion: 0.67
Nodes (3): TabScrollLeftButton, TabScrollRightButton, Button

### Community 196 - "UndoEntry"
Cohesion: 0.17
Nodes (12): UndoEntry, BranchName, Description, Kind, PostHeadSha, PreHeadSha, Sha, UndoKind (+4 more)

### Community 222 - "BulkObservableCollection"
Cohesion: 0.19
Nodes (3): BulkObservableCollection, ISuspendableNotifications, Scope

### Community 223 - "Plan: adhoc-background-auto-fetch — Background auto-fetch of all open tabs"
Cohesion: 0.12
Nodes (12): Acceptance Criteria, After Implementation, Constraints, Deviation Register, Handoff, Manual / Black-Box, Plan: adhoc-background-auto-fetch — Background auto-fetch of all open tabs, Problem Statement (+4 more)

### Community 224 - "ConflictResultEditBox"
Cohesion: 0.13
Nodes (4): IsManuallyEdited, ConflictResultEditBox, BoolToVis, TextEditor

### Community 225 - ".Warn"
Cohesion: 0.10
Nodes (4): Code Review: adhoc-background-auto-fetch (vs main), Summary, AppLog, Step 2: Silent per-repo background fetch ✓

### Community 227 - ".Load"
Cohesion: 0.18
Nodes (4): HostingCredentials, HostTokenEntry, Domain, Username

### Community 230 - "Help: The Commit Graph"
Cohesion: 0.31
Nodes (9): CommitInfo.RefMask, ListViewMultiSelectBehavior, Badge hover highlight, Branch filter, Help: The Commit Graph, Multi-commit selection, Branch operations, Help: Branches & Stashes (+1 more)

### Community 231 - "App.xaml"
Cohesion: 0.25
Nodes (4): Application, App.ApplyTheme, DetachedHeadConverter, Theme setting

### Community 232 - "TokenKind"
Cohesion: 0.25
Nodes (7): TokenKind, Comment, Keyword, Number, Preprocessor, String, Type

### Community 233 - "Language"
Cohesion: 0.25
Nodes (8): Language, CSharpLike, Css, Json, Markdown, PowerShell, Python, Xml

## Ambiguous Edges - Review These
- `CredentialsDialog` → `ConfirmDialog`  [AMBIGUOUS]
  .plans/adhoc-dialog-button-clipping.md · relation: conceptually_related_to

## Knowledge Gaps
- **1859 isolated node(s):** `graphify-guard.sh script`, `detect-base-branch.sh script`, `diff-profile.sh script`, `plan-drift-check.sh script`, `IsLightTheme` (+1854 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 2243 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **71 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **What is the exact relationship between `CredentialsDialog` and `ConfirmDialog`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **Why does `RepositoryViewModel` connect `RepositoryViewModel` to `system`, `GitService`, `AppViewModel`, `GraphNode`, `.RefreshAsync`, `.RefreshOnceAsync`, `RepositoryAccount.cs`, `.InitializeFind`, `AggregatedFileTreeRow`, `.Confirm`, `DiffView`, `RepositoryWatcher`, `BaseViewModel`, `CommitInfo`, `RefreshScope`, `Task`, `FileTreeRow`, `.RaisePropertyChanged`, `.ShowError`, `CommitListView`, `FileChange`, `.BranchNode_Drop`, `SettingsWindow`, `BranchNodeViewModel`, `SidebarView`, `FileSortMode`, `FileDiffResult`, `RemoteDialog`, `PaneFindState`, `DiffLine`, `AggregatedFileChange`, `HostingViewModel`, `.OpenRepoAsync`, `UndoEntry`, `BisectState`, `.OpenRepoInNewTabAsync`, `.LoadWorkingDirAsync`, `MainWindow`, `DiffChangeMapControl`, `ConflictState`, `BulkObservableCollection`, `SideBySideItem`, `DiffHunk`, `.Warn`, `StagingPatchOp`, `BlameLine`?**
  _High betweenness centrality (0.451) - this node is a cross-community bridge._
- **Why does `AppViewModel` connect `AppViewModel` to `.GetEffectiveColumnWidth`, `system`, `.Warn`, `RepositoryViewModel`, `HelpWindow`, `CommitListView`, `.OpenRepoInNewTabAsync`, `SettingsWindow`, `.RefreshAsync`, `MainWindow`, `SidebarView`, `CloneDialog`, `CommandPaletteWindow`, `AppSettings`, `.OnStartup`, `.RunBackgroundFetchPassAsync`, `BaseViewModel`?**
  _High betweenness centrality (0.175) - this node is a cross-community bridge._
- **Why does `App` connect `.OnStartup` to `AppViewModel`, `system`, `App.xaml`?**
  _High betweenness centrality (0.140) - this node is a cross-community bridge._
- **Are the 2 inferred relationships involving `RepositoryViewModel` (e.g. with `.AddEmptyTab()` and `Requirements`) actually correct?**
  _`RepositoryViewModel` has 2 INFERRED edges - model-reasoned connections that need verification._
- **Are the 3 inferred relationships involving `AppViewModel` (e.g. with `.OnStartup()` and `Requirements`) actually correct?**
  _`AppViewModel` has 3 INFERRED edges - model-reasoned connections that need verification._
- **What connects `graphify-guard.sh script`, `detect-base-branch.sh script`, `diff-profile.sh script` to the rest of the system?**
  _1859 weakly-connected nodes found - possible documentation gaps or missing edges._