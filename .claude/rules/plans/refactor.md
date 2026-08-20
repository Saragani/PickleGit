# Plan: refactor

If the user asks to write the plan directly, skip SC analysis and invoke `pkl:plan`.

**Code quality / cleanup** — invoke `sc:improve --type quality` then `sc:cleanup`.

**Performance** — invoke `sc:improve --type performance`.

When analysis is complete → invoke `pkl:plan` to write the enriched plan file.

## Tests section guidance
- **Unit / Integration Stubs**: regression stubs that verify observable behavior is unchanged after the refactor
- **Device / Black-Box**: smoke tests confirming nothing broke from the user's perspective
