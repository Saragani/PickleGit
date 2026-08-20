# Plan: feature

If the user asks to write the plan directly, skip SC analysis and invoke `pkl:plan`.

**Approach unclear** — invoke `sc:brainstorm`.
After it completes, offer:
> "Want to move to design, or write the plan now?"

**Architecture needed** — invoke `sc:design --type architecture --think-hard`.
After it completes, offer:
> "Ready to write the plan?"

**1–2 files, clear scope** — skip SC analysis, invoke `pkl:plan` directly.

When analysis is complete → invoke `pkl:plan` to write the enriched plan file.

## Tests section guidance
- **Unit / Integration Stubs**: one stub per functional requirement — verify each FR works in isolation
- **Device / Black-Box**: one scenario per acceptance criterion — confirm end-to-end behavior on device
