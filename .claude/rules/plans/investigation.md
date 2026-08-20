# Plan: investigation

If the user asks to write the findings report directly, skip SC analysis and invoke `pkl:plan`.

Spawn `Explore` agent: "Map call path for [symptom]. Return compact summary (max 200 words)."
Then invoke `sc:troubleshoot --type bug --think`.

When analysis is complete → invoke `pkl:plan` to write the plan file.

No commit required — omit `## Tests` and `## After Implementation` from the plan file and skip SHIP phase.

**Escalate to `bug` path if**: investigation reveals a code defect causing incorrect behavior, data corruption, or a crash.
