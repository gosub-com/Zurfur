# Zurfur

![Logo](Zurfur.jpg)

I love C#. It's my favorite language to program in. But I'd like to have some features from other
languages built in from the ground up. I'm thinking about ownership, immutability, nullability,
and functional programming.

Zurfur is a programming language I'm designing for fun and enlightenment. The language is named
after our cat, Zurfur, who was named by my son.  It's spelled **_ZurFUR_** because our cat has fur.
The syntax is still being developed and nothing is set in stone.  If you want to try it, click 
here https://gosub.com/zurfur

Documentation:

* [Basic Syntax](Doc/syntax.md): Auto inserted `{`, `}`, `;` and explicit scopes. 
  Keywords like `yield` and why there is no need for `await`.  Operators and operator precedence.
  Mutability and const correctness.  Private fields use `_` prefix.  Examples.
* [Ownership Model](Doc/ownership-model.md): How ownership, mutability, and the type
  system work together in Zurfur.
* [Multi-Threading](Doc/multi-threading.md): Why Zurfur starts single-threaded and
  how true multi-threading will be added in the future.
* [Runtime Model](Doc/runtime-model.md): How the first version of Zurfur will manage memory.
* [Interfaces](Doc/interfaces.md): Interfaces can be implemented for any type, even if
  they are declared in other libraries. If the type already conforms to the interface, it's
  very simple, just this: `bind MyType to MyInterface`. 

I'm also working on [Zurfur Gui](https://github.com/gosub-com/ZurfurGui), which
you can see here https://gosub.com/zurfurgui.

Older documentation on
[Confluence](https://zurfur.atlassian.net/wiki/external/ZjJlYjUwZmIzMzg0NGJkY2ExMmJlY2MwNDVlNTU4ODU)

## Design Goals

Zurfur takes its main inspiration from C#, but borrows syntax and design
concepts from 
[Lobster](https://strlen.com/lobster/), 
[Zig](https://ziglang.org/), 
[Midori](https://joeduffyblog.com/2016/02/07/the-error-model/), 
Golang, Rust, Python, JavaScript, and other languages.

* **Prime directives:**
    * Fun and easy to use.
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

## Types

Types fall into the basic categories of `struct`, `data`, `object`, and `interface`.  Data types can
be subdivided into `enum` and `flags`.  Finally, there are type attributes, such as `ro`, `box`, and 
`ref` which can be applied to types to change their behavior. For a deeper understanding of these types,
review the [Ownership Model](Doc/ownership-model.md)

| Type | Description
| :--- | :---
| struct | `type struct` can be trivially copied, bit for bit, and may not contain heap allocated data structures of any kind, including `data`, `object`, or `interface` types.  
| data | `type data` may hold mutable or immutable data.  It should act like data, must be copyable, and the compiler will generate a `ro` counterpart which should have proper semantics. Private data is generally not used, except for caching. It may not contain an `object` or `interface` type.
| object | `type object` is expected to contain mutable hidden data.  It does not have a `ro` counterpart, does not need to be copyable nor have an `==` operator.  An object type is stored in-line in memory and obeys the ownership rules as data types (i.e. it's not a reference type like in C#).
| enum | `type enum` is a discriminated union, similar to Rust's `enum`.
| flags | `type flags` is similar to C#'s `enum`.  Flags take a numeric type parameter like `type flags<Byte>` or `type flags<Int>` to hold typed constants.
| interface | `type interface` is a set of functions that can be implemented by any type.  It is similar to Golang's interfaces (i.e. duck typed), but with support for default implementations.
| ro | A `ro` type means it's read only.  This can be used at the type declaration `type ro data MyType` or at the variable declaration `ro List<Int>`.
| box | A `box` stores its data on the heap.  It can be used at the type declaration `type box data MyType` or at the variable declaration `List<box MyType>`.
| ref | A `ref` type is the only type that can contain a reference.  It is restricted to being owned by the stack.

### Inheritance is not Supported

Modern programming languages like Go and Rust have proven that classical implementation
inheritance is not necessary for a language to be highly expressive and successful.  


### Basic Types
The ones we all know and love:

    nil, Bool, I8, Byte, I16, U16, I32, U32, Int, U64, F32, Float, Str

`Int` and `Float` are 64 bits wide.

| Type | Description
| :--- | :---
| List\<T\> | Resizable mutable list of elements. `ro List\<T\>` is the immutable counterpart. Structural mutations such as append/remove may require `unique` if there are outstanding borrowed views into the list.
| Span\<T\> | A view into a `List`.  It has a constant length.  Mutability of elements depends on usage (e.g Span from `ro List` is immutable, Span from `List` is mutable)
| Map<K,V> | Unordered mutable map.  `ro Map<K,V>` is the immutable counterpart. 
| Maybe\<T\> | Identical to `?T`.  Always optimized for pointers and references.
| Result\<T\> | Same as `!T`. An optional containing either a return value or an `Error` interface.
| Error | An interface containing a `message` string and an integer `code`
| Str | Strings are an immutable list of bytes with support for UTF-8, similar to how [Golang](https://go.dev/blog/strings) strings.

All types have a compiler generated `ro` counterpart which can be copied very quickly since cloning
them is just copying a reference without dynamic allocation.

