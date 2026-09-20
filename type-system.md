# Type System

Before reading this section, check out the [Ownership Model](Doc/ownership-model.md)
which describes how ownership, mutability, and nullability are part of the type system.

This is still very **WIP**, and I will be updating as I go.  **TBD:** Integrate this old documentation:
https://zurfur.atlassian.net/wiki/external/ZjJlYjUwZmIzMzg0NGJkY2ExMmJlY2MwNDVlNTU4ODU

Types represents a data model†.  They contains fields and properties.  A field is space reserved for
some data, and a property is a `get` or `set` function that can be called to get or set a value.
A type **does not contain** functions or methods. Instead, functions and methods take the type as a
parameter.

† An *interface* represents a contract, and it **does** contain functions and methods.

```
// a type defining fields and properties
type MyType
    _field1 SomeType            // a private field that is a reference to SomeType
    field2 SomeType             // a public field that is a reference to SomeType
    prop1 get SomeType          // a public property that returns SomeType
    prop2 get set SomeType      // a public property that gets or sets SomeType
    prop3 get backing SomeType  // prop3 has a private backing field named `_prop3`
```

When a type declares a property, there must be a corresponding `get` or `set` function defined in the same
file.  A function that takes a type as the first parameter can be called like a method if it opts in with
special syntax.

In general (at the level of syntax), a field can be converted into a property without breaking
compatibility, although there are some subtleties regarding references.

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

