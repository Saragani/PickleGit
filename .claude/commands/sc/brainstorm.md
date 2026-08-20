---
name: brainstorm
description: "Interactive requirements discovery through Socratic dialogue and systematic exploration"
category: orchestration
complexity: advanced
---

# /sc:brainstorm - Interactive Requirements Discovery

> **Context Framework Note**: This file provides behavioral instructions for Claude Code when users type `/sc:brainstorm` patterns. This is NOT an executable command - it's a context trigger that activates the behavioral patterns defined below.

## Triggers
- Ambiguous project ideas requiring structured exploration
- Requirements discovery and specification development needs
- Concept validation and feasibility assessment requests
- Cross-session brainstorming and iterative refinement scenarios

## Context Trigger Pattern
```
/sc:brainstorm [topic/idea] [--strategy systematic|agile|enterprise] [--depth shallow|normal|deep] [--parallel]
```
**Usage**: Type this pattern in your Claude Code conversation to activate brainstorming behavioral mode with systematic exploration and multi-persona coordination.

## Behavioral Flow
1. **Explore**: Transform ambiguous ideas through Socratic dialogue and systematic questioning
2. **Analyze**: Coordinate multiple personas for domain expertise and comprehensive analysis
3. **Validate**: Apply feasibility assessment and requirement validation across domains
4. **Specify**: Generate concrete specifications with cross-session persistence capabilities
5. **Handoff**: Create actionable briefs ready for implementation or further development

Key behaviors:
- Agent orchestration: system-architect, requirements-analyst, frontend-architect, backend-architect, security-engineer
- Systematic execution with progressive dialogue enhancement and parallel exploration
- Cross-session persistence with comprehensive requirements discovery documentation

## Tool Coordination
- **Read/Write/Edit**: Requirements documentation and specification generation
- **TodoWrite**: Progress tracking for complex multi-phase exploration
- **Agent**: Spawn domain experts for parallel or sequential exploration (see Agent Delegation below)

## Agent Delegation

**Default**: reason inline using each domain lens — no agent spawning.

Spawn agents only when:
- `--parallel` flag is set, OR
- `--depth deep` with a topic spanning 3+ distinct domains, OR
- The idea is large enough that independent parallel research would meaningfully reduce bias

| Role | Lens |
|------|------|
| Architect | Component boundaries, scalability, long-term maintainability, dependency management |
| Analyzer | Functional vs non-functional, acceptance criteria, edge cases, stakeholder intent |
| Frontend | UI/UX patterns, component hierarchy, state management, rendering performance |
| Backend | Service internals, data flows, API contracts, concurrency, resource ownership |
| Security | Threat modeling, input validation, auth/authz, least privilege, data protection |

When spawning: use `general-purpose` subagent, include the role lens in the agent prompt. Run independent domains in parallel, synthesize before presenting to user.

## Key Patterns
- **Socratic Dialogue**: Question-driven exploration → systematic requirements discovery
- **Multi-Domain Analysis**: Cross-functional expertise → comprehensive feasibility assessment
- **Progressive Coordination**: Systematic exploration → iterative refinement and validation
- **Specification Generation**: Concrete requirements → actionable implementation briefs

## Examples

### Systematic Product Discovery
```
/sc:brainstorm "AI-powered project management tool" --strategy systematic --depth deep
# Agents: system-architect (system design), requirements-analyst (feasibility + requirements)
```

### Agile Feature Exploration
```
/sc:brainstorm "real-time collaboration features" --strategy agile --parallel
# Parallel agents: frontend-architect, backend-architect, security-engineer
```

### Enterprise Solution Validation
```
/sc:brainstorm "enterprise data analytics platform" --strategy enterprise --validate
# Agents: security-engineer, system-architect
```

### Cross-Session Refinement
```
/sc:brainstorm "mobile app monetization strategy" --depth normal
# Progressive dialogue enhancement with memory-driven insights
```

## Boundaries

**Will:**
- Transform ambiguous ideas into concrete specifications through systematic exploration
- Coordinate multiple personas for comprehensive analysis
- Provide cross-session persistence and progressive dialogue enhancement

**Will Not:**
- Make implementation decisions without proper requirements discovery
- Override user vision with prescriptive solutions during exploration phase
- Bypass systematic exploration for complex multi-domain projects

## CRITICAL BOUNDARIES

**STOP AFTER REQUIREMENTS DISCOVERY**

This command produces a REQUIREMENTS SPECIFICATION ONLY.

**Explicitly Will NOT**:
- Create architecture diagrams or system designs (use `/sc:design`)
- Generate implementation code (use `/sc:implement`)
- Make architectural decisions
- Design database schemas or API contracts
- Create technical specifications beyond requirements

**Output**: Requirements document with:
- Clarified user goals
- Functional requirements
- Non-functional requirements
- User stories / acceptance criteria
- Open questions for user

**Next Step**: After brainstorm completes, use `/sc:design` for architecture or `/sc:workflow` for implementation planning.