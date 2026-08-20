---
name: improve
description: "Apply systematic improvements to code quality, performance, and maintainability"
category: workflow
complexity: standard
---

# /sc:improve - Code Improvement

## Triggers
- Code quality enhancement and refactoring requests
- Performance optimization and bottleneck resolution needs
- Maintainability improvements and technical debt reduction
- Best practices application and coding standards enforcement

## Usage
```
/sc:improve [target] [--type quality|performance|maintainability|style] [--safe] [--interactive]
```

## Behavioral Flow
1. **Analyze**: Examine codebase for improvement opportunities and quality issues
2. **Plan**: Choose improvement approach and activate relevant personas for expertise
3. **Execute**: Apply systematic improvements with domain-specific best practices
4. **Validate**: Ensure improvements preserve functionality and meet quality standards
5. **Document**: Generate improvement summary and recommendations for future work

Key behaviors:
- Agent coordination: system-architect, performance-engineer, refactoring-expert, security-engineer based on improvement type
- Framework-specific optimization via Context7 integration for best practices
- Safe refactoring with comprehensive validation and rollback capabilities

## Agent Delegation

**Default**: reason inline — no agent spawning.

Spawn agents only when:
- `--interactive` on a large module where multiple improvement types overlap, OR
- `--type` explicitly covers 2+ domains simultaneously (e.g., quality + security)

| Role | Lens |
|------|------|
| Architect | Component boundaries, scalability, long-term maintainability, dependency management |
| Performance | Measure before optimizing, bottleneck identification, resource contention, latency vs throughput |
| Quality | Reduce coupling, improve naming, eliminate duplication, preserve behavior |
| Security | Threat modeling, input validation, auth/authz, least privilege, data protection |

When spawning: use `general-purpose` subagent, include the role lens in the agent prompt. Single `--type` (quality, performance, or security alone) — always inline.

## Tool Coordination
- **Read/Grep/Glob**: Code analysis and improvement opportunity identification
- **Edit/MultiEdit**: Safe code modification and systematic refactoring
- **TodoWrite**: Progress tracking for complex multi-file improvement operations
- **Task**: Delegation for large-scale improvement workflows requiring systematic coordination

## Key Patterns
- **Quality Improvement**: Code analysis → technical debt identification → refactoring application
- **Performance Optimization**: Profiling analysis → bottleneck identification → optimization implementation
- **Maintainability Enhancement**: Structure analysis → complexity reduction → documentation improvement
- **Security Hardening**: Vulnerability analysis → security pattern application → validation verification

## Examples

### Code Quality Enhancement
```
/sc:improve src/ --type quality --safe
# Systematic quality analysis with safe refactoring application
# Improves code structure, reduces technical debt, enhances readability
```

### Performance Optimization
```
/sc:improve api-endpoints --type performance --interactive
# Performance persona analyzes bottlenecks and optimization opportunities
# Interactive guidance for complex performance improvement decisions
```

### Maintainability Improvements
```
/sc:improve legacy-modules --type maintainability --preview
# Architect persona analyzes structure and suggests maintainability improvements
# Preview mode shows changes before application for review
```

### Security Hardening
```
/sc:improve auth-service --type security --validate
# security-engineer identifies vulnerabilities and applies security patterns
# Comprehensive validation ensures security improvements are effective
```

## Boundaries

**Will:**
- Apply systematic improvements with domain-specific expertise and validation
- Provide comprehensive analysis with multi-persona coordination and best practices
- Execute safe refactoring with rollback capabilities and quality preservation

**Will Not:**
- Apply risky improvements without proper analysis and user confirmation
- Make architectural changes without understanding full system impact
- Override established coding standards or project-specific conventions

## AUTO-FIX VS APPROVAL-REQUIRED

**Auto-fix (applies automatically)**:
- Style fixes (formatting, naming conventions)
- Unused variable removal
- Import organization
- Simple type annotations

**Approval Required (prompts user first)**:
- Architectural changes
- Logic refactoring
- Function signature changes
- Removing code used by public APIs
- Changes affecting multiple files

**Explicitly Will NOT** (without `--force` flag):
- Make architectural decisions
- Refactor code structure without confirmation
- Remove functionality
