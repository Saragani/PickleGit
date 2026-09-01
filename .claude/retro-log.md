# Retro Log

Rolling list of process improvements from `pkl:retro`. Read by `pkl:plan` at context-gather (step 1) so lessons feed into the next plan. One line per issue — newest at the bottom. Prune oldest entries manually if this grows past ~30 lines.

<!-- - <YYYY-MM-DD> gh-<N>: <one process improvement> -->
- 2026-08-24 adhoc-icon-font-fallback: Before driving a desktop-GUI app with simulated mouse input for verification, first confirm the target window's bounds match the full virtual screen (single-monitor, isolated session) rather than assuming isolation.
- 2026-09-01 adhoc-bitbucket-oauth-credential-retry: When a step's verification stubs are (manual) and can't be safely exercised in-session (e.g. require a live external service/real user credentials), don't call checkpoint.sh to mark the step done until either real verification happens or the deferral is written first — it auto-ticks stubs unconditionally, producing a false-GREEN plan state that then needs manual correction.
