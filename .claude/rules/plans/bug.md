# Plan: bug

If the user asks to write the plan directly, skip SC analysis and invoke `pkl:plan`.

Invoke `sc:troubleshoot --type bug --think` (use `--ultrathink` for multi-component bugs) to identify root cause and approach.

If fix requires new classes / CMake / Qt restructuring, also invoke `sc:design --think-hard`.

When analysis is complete → invoke `pkl:plan` to write the enriched plan file.

## Tests section guidance
- **Unit / Integration Stubs**: stubs that verify the root cause is fixed and guard against regression
- **Device / Black-Box**: reproduce the original bug scenario + confirm it no longer occurs
