---
name: estimate
description: "Provide development estimates for tasks, features, or projects with intelligent analysis"
category: special
complexity: standard
disable-model-invocation: true
---

# /sc:estimate - Development Estimation

## Triggers
- Development planning requiring time, effort, or complexity estimates
- Project scoping and resource allocation decisions
- Feature breakdown needing systematic estimation methodology
- Risk assessment and confidence interval analysis requirements

## Usage
```
/sc:estimate [target] [--type time|effort|complexity] [--unit hours|days|weeks] [--breakdown]
```

## Behavioral Flow
1. **Analyze**: Examine scope, complexity factors, dependencies, and framework patterns
2. **Calculate**: Apply estimation methodology with historical benchmarks and complexity scoring
3. **Validate**: Cross-reference estimates with project patterns and domain expertise
4. **Present**: Provide detailed breakdown with confidence intervals and risk assessment
5. **Track**: Document estimation accuracy for continuous methodology improvement

Key behaviors:
- Agent coordination: system-architect, performance-engineer, requirements-analyst based on estimation scope
- Intelligent breakdown analysis with confidence intervals and risk factors

## Agent Delegation

**Default**: reason inline — no agent spawning.

Spawn agents only when:
- `--breakdown` on a feature spanning 4+ components, OR
- Project-level estimate where architecture complexity and scope are both unclear

| Role | Lens |
|------|------|
| Architect | Component boundaries, scalability, long-term maintainability, dependency management |
| Performance | Measure before optimizing, bottleneck identification, resource contention, latency vs throughput |
| Analyst | Functional vs non-functional, acceptance criteria, edge cases, stakeholder intent |

When spawning: use `general-purpose` subagent, include the role lens in the agent prompt. Run in parallel, aggregate confidence intervals before presenting.

## Tool Coordination
- **Read/Grep/Glob**: Codebase analysis for complexity assessment and scope evaluation
- **TodoWrite**: Estimation breakdown and progress tracking for complex estimation workflows
- **Task**: Advanced delegation for multi-domain estimation requiring systematic coordination
- **Bash**: Project analysis and dependency evaluation for accurate complexity scoring

## Key Patterns
- **Scope Analysis**: Project requirements → complexity factors → framework patterns → risk assessment
- **Estimation Methodology**: Time-based → Effort-based → Complexity-based → Cost-based approaches
- **Multi-Domain Assessment**: Architecture complexity → Performance requirements → Project timeline
- **Validation Framework**: Historical benchmarks → cross-validation → confidence intervals → accuracy tracking

## Examples

### Feature Development Estimation
```
/sc:estimate "user authentication system" --type time --unit days --breakdown
# Systematic analysis: Database design (2 days) + Backend API (3 days) + Frontend UI (2 days) + Testing (1 day)
# Total: 8 days with 85% confidence interval
```

### Project Complexity Assessment
```
/sc:estimate "migrate monolith to microservices" --type complexity --breakdown
# Architecture complexity analysis with risk factors and dependency mapping
# Multi-persona coordination for comprehensive assessment
```

### Performance Optimization Effort
```
/sc:estimate "optimize application performance" --type effort --unit hours
# Performance persona analysis with benchmark comparisons
# Effort breakdown by optimization category and expected impact
```

## Boundaries

**Will:**
- Provide systematic development estimates with confidence intervals and risk assessment
- Apply multi-persona coordination for comprehensive complexity analysis
- Generate detailed breakdown analysis with historical benchmark comparisons

**Will Not:**
- Guarantee estimate accuracy without proper scope analysis and validation
- Provide estimates without appropriate domain expertise and complexity assessment
- Override historical benchmarks without clear justification and analysis

