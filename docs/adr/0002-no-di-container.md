---
status: accepted
---

# No MEF / DI container — plain WPF/MVVM wiring

PickleGit is a single-project WPF app with no plugin architecture and no need for swappable service implementations, so there's no dependency-injection container (no MEF, no Ninject, nothing). A View gets its ViewModel one of two plain ways: `DataTemplate DataType="{x:Type vm:MyViewModel}"` resolution through a `ContentControl`, or a parent ViewModel exposing a child VM as a bound `DataContext` property.

## Considered Options

- **MEF** (the pattern used in larger, plugin-oriented WPF codebases in this space) — rejected as unnecessary ceremony: PickleGit has no extensibility requirement, no third-party plugin surface, and a single project assembly, so `[Export]`/`[ImportingConstructor]` composition would add indirection without buying anything.

## Consequences

- Adding a new child ViewModel means adding a plain property on its parent (or a `DataTemplate` entry) — never a `[Export]`/`[Import]` pair.
- There is no service-locator substitute either — a View must never do `DataContext = new ViewModel()` for a child that already has (or should have) a VM property on its parent; see `code-style.md`.
- Testability trade-off accepted: swapping an implementation means editing the call site directly rather than re-composing a container. This is fine for a single-developer desktop app with no need for parallel implementations.
