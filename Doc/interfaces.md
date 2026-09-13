# Zurfur Interface System

The current implementation uses Go style interfaces, which are structural and implicit. 
This is a very flexible system, but it has some downsides concerning ambiguity, accidental
interface satisfaction, and a few other frictions related to Zurfur specific design decisions.

The new system is closer to Swift's system.

**NOTE:** This has not been implemented yet, but I am working on it.

If a type's existing methods naturally match the interface, you can explicitly bind them with a single
line: `bind MyType to MyInterface`.  If the type does not conform, the body of the `bind` statement
may implement additional functions that allow it to conform. 

When the type definition is in the same **file** as the `bind` statement, it is globally bound 
to the interface and the type satisfies the interface everywhere.  This is effectively the
same as C#'s `class MyType : MyInterface`.

When the type definition is in a different **file** as the `bind` statement, the binding is local
to only that single file.  Other files may import the local binding with `use OtherModule[bind TypeName]`.
Even though the bind usage is local to the file, the binding itself is global to the module,
meaning it can't be declared multiple times in the same module.

When a type globally binds to an interface, local bindings generate a compiler error.  In cases
where a local binding is required (legacy code, library added it's own bind, test case, etc.)
the global binding can be overriden with `override bind` syntax.

There can be multiple local bindings for the same type.  This is expected and accountd for.
Zurfur uses Go style fat interface pointers (an interface table, and a pointer to the type)
to select the correct implementation at the call site.
