---
name: cleanup
description: "Systematically clean up code, remove dead code, and optimize project structure"
category: workflow
complexity: standard
disable-model-invocation: true
---

# /sc:cleanup - Code and Project Cleanup

## Triggers
- Code maintenance and technical debt reduction requests
- Dead code removal and import optimization needs
- Project structure improvement and organization requirements
- Codebase hygiene and quality improvement initiatives

## Usage
```
/sc:cleanup [target] [--type code|imports|files|all] [--safe|--aggressive] [--interactive]
```

## Behavioral Flow
1. **Analyze**: Assess cleanup opportunities and safety considerations across target scope
2. **Plan**: Choose cleanup approach and activate relevant personas for domain expertise
3. **Execute**: Apply systematic cleanup with intelligent dead code detection and removal
4. **Validate**: Ensure no functionality loss through testing and safety verification
5. **Report**: Generate cleanup summary with recommendations for ongoing maintenance

Key behaviors:
- Agent coordination: system-architect, refactoring-expert, security-engineer based on cleanup type
- Safety-first approach with backup and rollback capabilities

## Agent Delegation

**Default**: reason inline — no agent spawning.

Spawn agents only when:
- `--type all` across a large codebase (50+ files), OR
- `--aggressive` where structural + security review is needed simultaneously

| Role | Lens |
|------|------|
| Architect | Component boundaries, scalability, long-term maintainability, dependency management |
| Quality | Reduce coupling, improve naming, eliminate duplication, preserve behavior |
| Security | Threat modeling, input validation, auth/authz, least privilege, data protection |

When spawning: use `general-purpose` subagent, include the role lens in the agent prompt. Run in parallel, merge findings before applying changes.

## Tool Coordination
- **Read/Grep/Glob**: Code analysis and pattern detection for cleanup opportunities
- **Edit/MultiEdit**: Safe code modification and structure optimization
- **TodoWrite**: Progress tracking for complex multi-file cleanup operations
- **Task**: Delegation for large-scale cleanup workflows requiring systematic coordination

## Key Patterns
- **Dead Code Detection**: Usage analysis → safe removal with dependency validation
- **Import Optimization**: Dependency analysis → unused import removal and organization
- **Structure Cleanup**: Architectural analysis → file organization and modular improvements
- **Safety Validation**: Pre/during/post checks → preserve functionality throughout cleanup

## Examples

### Safe Code Cleanup
```
/sc:cleanup src/ --type code --safe
# Conservative cleanup with automatic safety validation
# Removes dead code while preserving all functionality
```

### Import Optimization
```
/sc:cleanup --type imports --preview
# Analyzes and shows unused import cleanup without execution
```

### Comprehensive Project Cleanup
```
/sc:cleanup --type all --interactive
# Multi-domain cleanup with user guidance for complex decisions
# Activates all personas for comprehensive analysis
```

### Framework-Specific Cleanup
```
/sc:cleanup components/ --aggressive
```

## Boundaries

**Will:**
- Systematically clean code, remove dead code, and optimize project structure
- Provide comprehensive safety validation with backup and rollback capabilities
- Apply intelligent cleanup algorithms with framework-specific pattern recognition

**Will Not:**
- Remove code without thorough safety analysis and validation
- Override project-specific cleanup exclusions or architectural constraints
- Apply cleanup operations that compromise functionality or introduce bugs

## AUTO-FIX VS APPROVAL-REQUIRED

**Auto-fix (applies automatically)**:
- Unused imports removal
- Dead code with zero references
- Empty blocks removal
- Redundant type annotations

**Approval Required (prompts user first)**:
- Code with indirect references
- Exports potentially used externally
- Test fixtures/utilities
- Configuration values

**Safety Threshold**:
- If code has ANY usage path, prompt user
- If code affects public API, prompt user
- If unsure, prompt user