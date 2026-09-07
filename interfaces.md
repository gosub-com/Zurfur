## Zurfur Interface System

**Zurfur** introduces a hybrid, nominal interface system that bridges
the gap between Go's structural flexibility and Rust/Swift's explicit safety.

* **Expressive & Lightweight Conformance:** If a type's existing methods naturally match an interface,
  developers can explicitly link them with a single line: `bind MyType to MyInterface`. This prevents
  the "implicit conversion anarchy" of Go while eliminating the boilerplate of Rust/Swift.
* **Retroactive & Multi-Scope Modeling:** If a type does not conform naturally, the `bind` block can be
  opened to supply the missing functions. Authors can implement interfaces for types they do not
  own even if the type does not conform to the interface.
* **Scope-Based Resolution:** Implementations defined in the same scope as the type are definitive.
  Alternative implementations can exist in different scopes and must be explicitly imported or
  overridden, resolving the classic expression problem and collision risks.
* **Performance Architecture:** Zurfur uses monomorphization (static dispatch) for generic types and
  bounds to achieve zero-cost performance. For dynamic dispatch, it uses fat pointers (similar to Go
  interfaces and Rust trait objects).


## Language Comparison

| **Feature / Language** | **Zurfur** | **Rust** | **Swift** | **Go** | **C#** |
|---|---|---|---|---|
| **Type System** | **Nominal** <br> (Explicit binding) | **Nominal** (Explicit block) | **Nominal** (Explicit block) | **Structural** (Implicit) | **Nominal** (Explicit declaration) |
| **Boilerplate Level** | **Low** (1 line if type has matching methods) | **High** (Requires full block wrapper) | **High** (Requires full extension wrapper) | **Zero** (Completely implicit) | **Low** (Explicitly list on class) |
| **Retroactive Extensions?** | **Yes** (Even for foreign types/interfaces) | **Restricted** (Orphan rules prevent foreign-on-foreign) | **Yes** (Retroactive modeling allowed) | **No** (Cannot add methods to a type outside its definition package) | **No** (Extension methods cannot satisfy) |
| **Handling Conflicting Impls** | **Scope-Based** (Explicit imports isolate conflicts) | **Forbidden** (Compiler rejects any overlapping impls) | **Forbidden** (Compiler rejects duplicate conformances) | **N/A** (No explicit links; call-site errors) | **Explicit** (Disambiguated via type-prefixing) |
| **Underlying Mechanism** | Monomorphization or Fat Pointers | Monomorphization or Fat Pointers (trait objects) | Witness Tables & Metadata Pointers | Fat Pointers (eface/iface) | JIT Reification & vtables |


