---
name: design
description: "Design system architecture, APIs, and component interfaces with comprehensive specifications"
category: utility
complexity: basic
---

# /sc:design - System and Component Design

## Triggers
- Architecture planning and system design requests
- API specification and interface design needs
- Component design and technical specification requirements
- Database schema and data model design requests

## Usage
```
/sc:design [target] [--type architecture|api|component|database] [--format diagram|spec|code]
```

## Behavioral Flow
1. **Analyze**: Examine target requirements and existing system context
2. **Plan**: Define design approach and structure based on type and format
3. **Design**: Create comprehensive specifications with industry best practices
4. **Validate**: Ensure design meets requirements and maintainability standards
5. **Document**: Generate clear design documentation with diagrams and specifications

Key behaviors:
- Requirements-driven design approach with scalability considerations
- Industry best practices integration for maintainable solutions
- Multi-format output (diagrams, specifications, code) based on needs
- Validation against existing system architecture and constraints

## Tool Coordination
- **Read**: Requirements analysis and existing system examination
- **Grep/Glob**: Pattern analysis and system structure investigation
- **Write**: Design documentation and specification generation
- **Agent**: Spawn architecture experts for complex design work (see Agent Delegation below)

## Agent Delegation

**Default**: reason inline — no agent spawning.

Spawn agents only when:
- `--think-hard` is set, OR
- Designing a new multi-service system where architecture and API contracts need independent review

| Role | Lens |
|------|------|
| Architect | Component boundaries, scalability, long-term maintainability, dependency management |
| Backend | Service internals, data flows, API contracts, concurrency, resource ownership |

When spawning: use `general-purpose` subagent, include the role lens in the agent prompt. Run in parallel for full-system design, sequential for focused single-layer design.

## Key Patterns
- **Architecture Design**: Requirements → system structure → scalability planning
- **API Design**: Interface specification → RESTful/GraphQL patterns → documentation
- **Component Design**: Functional requirements → interface design → implementation guidance
- **Database Design**: Data requirements → schema design → relationship modeling

## Examples

### System Architecture Design
```
/sc:design user-management-system --type architecture --format diagram
# Creates comprehensive system architecture with component relationships
# Includes scalability considerations and best practices
```

### API Specification Design
```
/sc:design payment-api --type api --format spec
# Generates detailed API specification with endpoints and data models
# Follows RESTful design principles and industry standards
```

### Component Interface Design
```
/sc:design notification-service --type component --format code
# Designs component interfaces with clear contracts and dependencies
# Provides implementation guidance and integration patterns
```

### Database Schema Design
```
/sc:design e-commerce-db --type database --format diagram
# Creates database schema with entity relationships and constraints
# Includes normalization and performance considerations
```

## Boundaries

**Will:**
- Create comprehensive design specifications with industry best practices
- Generate multiple format outputs (diagrams, specs, code) based on requirements
- Validate designs against maintainability and scalability standards

**Will Not:**
- Generate actual implementation code (use /sc:implement for implementation)
- Modify existing system architecture without explicit design approval
- Create designs that violate established architectural constraints

**Output**: Architecture documents containing:
- System diagrams (component, sequence, data flow)
- API specifications
- Database schemas
- Interface definitions

**Next Step**: After design is approved, use `/sc:implement` to build the designed components.