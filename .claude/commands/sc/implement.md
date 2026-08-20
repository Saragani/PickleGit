---
name: implement
description: "Feature and code implementation with intelligent persona activation and MCP integration"
category: workflow
complexity: standard
---

# /sc:implement - Feature Implementation

> **Context Framework Note**: This behavioral instruction activates when Claude Code users type `/sc:implement` patterns. It guides Claude to coordinate specialist personas for comprehensive implementation.

## Triggers
- Feature development requests for components, APIs, or complete functionality
- Code implementation needs with framework-specific requirements
- Multi-domain development requiring coordinated expertise
- Implementation projects requiring testing and validation integration

## Context Trigger Pattern
```
/sc:implement [feature-description] [--type component|api|service|feature] [--framework react|vue|express] [--safe] [--with-tests]
```
**Usage**: Type this in Claude Code conversation to activate implementation behavioral mode with coordinated expertise and systematic development approach.

## Behavioral Flow
1. **Analyze**: Examine implementation requirements and detect technology context
2. **Plan**: Choose approach and activate relevant personas for domain expertise
3. **Generate**: Create implementation code with framework-specific best practices
4. **Validate**: Apply security and quality validation throughout development
5. **Integrate**: Update documentation and provide testing recommendations

Key behaviors:
- Context-based agent activation: system-architect, frontend-architect, backend-architect, security-engineer, quality-engineer
- Comprehensive testing integration with Playwright for validation

## Tool Coordination
- **Write/Edit/MultiEdit**: Code generation and modification for implementation
- **Read/Grep/Glob**: Project analysis and pattern detection for consistency
- **TodoWrite**: Progress tracking for complex multi-file implementations
- **Agent**: Spawn domain experts before generating code (see Agent Delegation below)

## Agent Delegation

**Default**: reason inline — no agent spawning.

Spawn agents only when:
- `--type feature` spanning 4+ files with unclear cross-component design, OR
- `--with-tests` on a complex feature where test strategy needs independent analysis, OR
- Security-sensitive implementation (auth, crypto, IPC) where an independent review adds value

| Role | Lens |
|------|------|
| Architect | Component boundaries, scalability, long-term maintainability, dependency management |
| Frontend | UI/UX patterns, component hierarchy, state management, rendering performance |
| Backend | Service internals, data flows, API contracts, concurrency, resource ownership |
| Security | Threat modeling, input validation, auth/authz, least privilege, data protection |
| QA | Test coverage, edge cases, regression safety, behavior verification |

When spawning: use `general-purpose` subagent, include the role lens in the agent prompt. Run independent domains in parallel, synthesize before writing any code.

## Key Patterns
- **Context Detection**: Framework/tech stack → appropriate persona
- **Implementation Flow**: Requirements → code generation → validation → integration
- **Multi-Persona Coordination**: Frontend + Backend + Security → comprehensive solutions
- **Quality Integration**: Implementation → testing → documentation → validation

## Examples

### React Component Implementation
```
/sc:implement user profile component --type component --framework react
# Frontend persona ensures best practices and accessibility
```

### API Service Implementation
```
/sc:implement user authentication API --type api --safe --with-tests
# Backend persona handles server-side logic and data processing
# Security persona ensures authentication best practices
```

### Full-Stack Feature
```
/sc:implement payment processing system --type feature --with-tests
# Agents: system-architect, frontend-architect, backend-architect, security-engineer
```

### Framework-Specific Implementation
```
/sc:implement dashboard widget --framework vue
# Framework-appropriate implementation with official best practices
```

## Boundaries

**Will:**
- Implement features with intelligent persona activation
- Apply framework-specific best practices and security validation
- Provide comprehensive implementation with testing and documentation integration

**Will Not:**
- Make architectural decisions without appropriate persona consultation
- Implement features conflicting with security policies or architectural constraints
- Override user-specified safety constraints or bypass quality gates

## COMPLETION CRITERIA

**Implementation is DONE when**:
- Feature code is written and compiles
- Basic functionality verified
- Files saved and ready for testing

**Post-Implementation Checklist**:
1. Code compiles without errors
2. Basic functionality works
3. Ready for `/pkl:static-analyze`