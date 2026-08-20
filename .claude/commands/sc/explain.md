---
name: explain
description: "Provide clear explanations of code, concepts, and system behavior with educational clarity"
category: workflow
complexity: standard
disable-model-invocation: true
---

# /sc:explain - Code and Concept Explanation

## Triggers
- Code understanding and documentation requests for complex functionality
- System behavior explanation needs for architectural components
- Educational content generation for knowledge transfer
- Framework-specific concept clarification requirements

## Usage
```
/sc:explain [target] [--level basic|intermediate|advanced] [--format text|examples|interactive] [--context domain]
```

## Behavioral Flow
1. **Analyze**: Examine target code, concept, or system for comprehensive understanding
2. **Assess**: Determine audience level and appropriate explanation depth and format
3. **Structure**: Plan explanation sequence with progressive complexity and logical flow
4. **Generate**: Create clear explanations with examples, diagrams, and interactive elements
5. **Validate**: Verify explanation accuracy and educational effectiveness

Key behaviors:
- Agent coordination: learning-guide, system-architect, security-engineer based on level and context
- Adaptive explanation depth based on audience and complexity

## Agent Delegation

**Default**: reason inline — no agent spawning.

Spawn agents only when:
- `--level advanced` + `--format interactive` on a complex multi-component system, OR
- `--context security` on a non-trivial auth/crypto system where threat model depth matters

| Role | Lens |
|------|------|
| Educator | Build from first principles, use analogies, check understanding, progressive complexity |
| Architect | Component boundaries, scalability, long-term maintainability, dependency management |
| Security | Threat modeling, input validation, auth/authz, least privilege, data protection |

When spawning: use `general-purpose` subagent, include the role lens in the agent prompt. Simple explanations (`--level basic/intermediate`, single function/file) — always inline.

## Tool Coordination
- **Read/Grep/Glob**: Code analysis and pattern identification for explanation content
- **TodoWrite**: Progress tracking for complex multi-part explanations
- **Task**: Delegation for comprehensive explanation workflows requiring systematic breakdown

## Key Patterns
- **Progressive Learning**: Basic concepts → intermediate details → advanced implementation
- **Multi-Domain Analysis**: Technical accuracy + educational clarity + security awareness
- **Interactive Explanation**: Static content → examples → interactive exploration

## Examples

### Basic Code Explanation
```
/sc:explain authentication.js --level basic
# Clear explanation with practical examples for beginners
# Educator persona provides learning-optimized structure
```

### Framework Concept Explanation
```
/sc:explain react-hooks --level intermediate --context react
# Structured explanation with progressive complexity
```

### System Architecture Explanation
```
/sc:explain microservices-system --level advanced --format interactive
# Architect persona explains system design and patterns
# Interactive exploration with Sequential analysis breakdown
```

### Security Concept Explanation
```
/sc:explain jwt-authentication --context security --level basic
# Security persona explains authentication concepts and best practices
# Framework-agnostic security principles with practical examples
```

## Boundaries

**Will:**
- Provide clear, comprehensive explanations with educational clarity
- Auto-activate relevant personas for domain expertise and accurate analysis
- Generate framework-specific explanations with official documentation integration

**Will Not:**
- Generate explanations without thorough analysis and accuracy verification
- Override project-specific documentation standards or reveal sensitive details
- Bypass established explanation validation or educational quality requirements