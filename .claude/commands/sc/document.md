---
name: document
description: "Generate focused documentation for components, functions, APIs, and features"
category: utility
complexity: basic
disable-model-invocation: true
---

# /sc:document - Focused Documentation Generation

## Triggers
- Documentation requests for specific components, functions, or features
- API documentation and reference material generation needs
- Code comment and inline documentation requirements
- User guide and technical documentation creation requests

## Usage
```
/sc:document [target] [--type inline|external|api|guide] [--style brief|detailed] [--format doxygen|jsdoc|docstring|javadoc|auto]
```

## Behavioral Flow
1. **Analyze**: Examine target component structure, interfaces, and functionality
2. **Detect Format**: Determine format from `--format` flag → existing comment patterns in codebase → file extension (see table below)
3. **Identify**: Determine documentation requirements and target audience context
4. **Generate**: Create appropriate documentation content based on type and style
5. **Format**: Apply consistent structure and organizational patterns
6. **Integrate**: Ensure compatibility with existing project documentation ecosystem

## Format Detection (auto mode)

| Extension | Format |
|---|---|
| `.c` `.cpp` `.h` `.hpp` `.cc` `.cs` | `doxygen` |
| `.js` `.ts` `.jsx` `.tsx` | `jsdoc` |
| `.py` | `docstring` |
| `.java` `.kt` | `javadoc` |
| Other | Mirror existing comment style in codebase |

## Doxygen-Specific Rules

These are non-obvious behaviors required for correct Doxygen output:

- **Always add `@file` header block** at the top of every C/C++ file with `@brief`, `@author`, `@date`
- **Use directional `@param` qualifiers**: `@param[in]`, `@param[out]`, `@param[in,out]` — never plain `@param`
- **Inline member comments** use `///<` suffix style, not a preceding block
- **Enum values** get `///<` inline comments on each entry
- **Group related API declarations** with `@defgroup` / `@{` / `@}` in header files
- **`@brief` is one sentence, imperative mood** — longer explanation goes in `@details`
- **`@note` and `@warning`** for non-obvious behavior or preconditions
- **`@see`** for cross-references to related functions or classes

Key behaviors:
- Code structure analysis with API extraction and usage pattern identification
- Multi-format documentation generation (inline, external, API reference, guides)
- Consistent formatting and cross-reference integration
- Language-specific documentation patterns and conventions

## Tool Coordination
- **Read**: Component analysis and existing documentation review
- **Grep**: Reference extraction and pattern identification
- **Write**: Documentation file creation with proper formatting
- **Glob**: Multi-file documentation projects and organization
- **Agent**: Delegate complex documentation tasks inline (see Agent Delegation below)

## Agent Delegation

**Default**: reason inline — no agent spawning.

Spawn only when:
- `--type guide` with 5+ sections requiring audience analysis, OR
- Documenting a full public API surface (10+ functions) with `--style detailed`

| Role | Lens |
|------|------|
| Writer | Audience-first clarity, progressive disclosure, consistent structure, actionable examples |

When spawning: use `general-purpose` subagent, include the role lens in the agent prompt. `--type inline` (code comments) — always inline, never delegate.

## Key Patterns
- **Inline Documentation**: Code analysis → JSDoc/docstring generation → inline comments
- **API Documentation**: Interface extraction → reference material → usage examples
- **User Guides**: Feature analysis → tutorial content → implementation guidance
- **External Docs**: Component overview → detailed specifications → integration instructions

## Examples

### C/C++ API Documentation
```
/sc:document src/network/socket.h --type api --format doxygen --style detailed
# Generates Doxygen /** */ blocks for all exported functions, structs, and enums
# Adds @file header, @param[in/out] directives, @return, @note, and @see tags
# Groups related declarations with @defgroup sections
```


### Inline Code Documentation
```
/sc:document src/auth/login.js --type inline
# Generates JSDoc comments with parameter and return descriptions
# Adds comprehensive inline documentation for functions and classes
```

### API Reference Generation
```
/sc:document src/api --type api --style detailed
# Creates comprehensive API documentation with endpoints and schemas
# Generates usage examples and integration guidelines
```

### User Guide Creation
```
/sc:document payment-module --type guide --style brief
# Creates user-focused documentation with practical examples
# Focuses on implementation patterns and common use cases
```

### Component Documentation
```
/sc:document components/ --type external
# Generates external documentation files for component library
# Includes props, usage examples, and integration patterns
```

## Boundaries

**Will:**
- Generate focused documentation for specific components and features
- Create multiple documentation formats based on target audience needs
- Integrate with existing documentation ecosystems and maintain consistency
- Apply Doxygen directional `@param[in/out]` qualifiers and `@file` headers automatically for C/C++

**Will Not:**
- Mix documentation formats within the same file
- Generate documentation without proper code analysis and context understanding
- Override existing documentation standards or project-specific conventions
- Create documentation that exposes sensitive implementation details