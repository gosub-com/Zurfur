# Zurfur

Zurfur is a programming language I'm designing for fun and enlightenment. The language is named
after our cat, Zurfur, who was named by my son.  It's spelled **_ZurFUR_** because our cat has fur.
The syntax is still being developed and nothing is set in stone.  If you want to try it, click 
here **https://gosub.com/zurfur**

![Logo](Zurfur.jpg)

## Documentation

* [Syntax](Doc/syntax.md): Auto inserted `{`, `}`, `;` and explicit curly brace scopes. 
  Keywords like `yield` and why there is no need for `await`.  Operators and operator
  precedence. Mutability and const correctness examples.  
* [Module System](Doc/module-system.md): Packages, modules, and symbol visibility rules.
  Use of `pub` and private fields use `_` prefix for file scope.
* [Ownership Model](Doc/ownership-model.md): How ownership, mutability, and the type
  system work together in Zurfur.
* [Type System](Doc/type-system.md): Types represent a data model. They consist of fields 
  and properties, but do not contain functions or methods. 
* [Interfaces](Doc/interfaces.md): Interfaces can be implemented for any type, even if
  they are declared in other libraries. If the type already conforms to the interface, it's
  very simple, just this: `bind MyType to MyInterface`. 
* [Multi-Threading](Doc/multi-threading.md): Why Zurfur starts single-threaded and how
  true multi-threading will be added in the future.
* [Runtime Model](Doc/runtime-model.md): How the first version of Zurfur will manage memory.
* [Design Rationale](Doc/design-rationale.md): Some explanations behind the design decisions.

## Design Goals

Zurfur takes its main inspiration from C#, but borrows syntax and design
concepts from 
[Lobster](https://strlen.com/lobster/), 
[Zig](https://ziglang.org/), 
[Midori](https://joeduffyblog.com/2016/02/07/the-error-model/), 
Golang, Rust, Python, JavaScript, and other languages.

* **Prime directives:**
    * Fun and easy to use.  AI friendly.
    * Faster than C# and unsafe code just as fast as C.
    * Target WebAssembly in the browser with easy JavaScript interop.
* **Ownership, mutability, and nullability are part of the type system:**
    * `ro` means read-only *all the way down*, not like C# where `readonly` protects only the top level.
    * All types are values (i.e. *owned*) except for `ro` types (e.g. `Str`), pointers (`^T`) and borrowed references (`&T`).
    * All mutable types have a `ro` counterpart which can be copied quickly via single pointer assignment (e.g. `Str` and `ro List<Byte>`).
    * Function parameters must be explicitly marked `mut` if they modify anything.
    * Borrowed references may survive async suspension, but structurally invalidating operations on `List` require uniqueness while outstanding borrows exist.
    * References and pointers are non-nullable, but may use `?T` for nullable.
* **Fast and efficient:**
    * Return references and span used everywhere. `[]Int` is `Span<Int>`.
    * Functions pass parameters by reference, but will pass a copy when it is more efficient.
    * Explicit `copy` required when copying an object that requires dynamic allocation.
