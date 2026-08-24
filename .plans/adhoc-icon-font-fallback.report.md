# Run report — adhoc-icon-font-fallback (run_mode: auto)

Step 1 ✓ — Add Segoe MDL2 Assets fallback to every XAML icon glyph (deviation: None)
Step 2 ✓ — Add Segoe MDL2 Assets fallback to the code-behind FontFamily construction (deviation: None)
Step 3 ✓ — Full visual regression pass (deviation: Verified 8 of 13 icon codepoints visually (sidebar headers, ahead/behind indicators, commit-graph tag badge, commit-detail flat-list icon) rather than all 13 — remaining 4 (Pull Requests header, tab-scroll chevrons, folder icon, delete icon) require app state (hosting config, tab overflow, nested folder, working-dir view) not easily reachable without further live-desktop automation. User accepted this level of evidence given Risk=None for all steps and the identical, already-proven fallback mechanism.)
