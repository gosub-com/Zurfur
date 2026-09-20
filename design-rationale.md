# Design Rationale


## Explicit Module Declaration (`mod`)

Every source file must explicitly declare its fully qualified module path (e.g.,
`mod BillingService.Billing`). The compiler uses this declaration as the source of
truth instead of inferring identity from the directory structure.

Explicit declarations make structural identity clear and keep public APIs stable when
files move. Modern IDEs can maintain the small amount of boilerplate, making this a worthwhile
trade-off.

## Redundant, Enforced Module-Level Visibility (`pub mod`)

If a module is split across multiple files, every file must declare the same visibility
modifier (for example, all files must use `pub mod ...` or all must omit `pub`). A mismatch
is a compiler error.

This makes visibility immediately apparent from any file, without requiring developers to
find a module entry point. Although changing visibility requires updating multiple files,
compiler enforcement catches omissions and keeps the change deliberate.

## File-Scoped Type Field Privacy (`_field`)

Fields starting with an underscore (`_`) are private to the file in which the type is defined,
rather than the entire logical module. This balances encapsulation with file-level organization.

File-scoped `_` fields make state easier to reason about: debugging or refactoring requires
searching only the defining file, rather than every file in the module that might mutate the
field. They also encourage cohesive files containing related types, helpers, and state logic,
instead of allowing modules to become collections of tightly coupled but unrelated code.

This provides a predictable alternative to C#-style access patterns, where `private` can force
large nested types and `internal` exposes members across the assembly. Related helper or
extension-like logic can still act as a friend when kept in the same file, while the privacy
boundary remains clear and stops at the file.

