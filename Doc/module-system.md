# Module System and Visibility


Zurfur organizes code using a nested hierarchy: Workspace ➔ Packages ➔ Modules.

* **Workspace:** The root of the project. It groups related packages together, manages global
  configurations, and defines the project scope.  It similar to a C# solution (.sln).
* **Package:** The basic unit of compilation used to generate a single executable or library.
  It is similar to a C# project (.csproj) or assembly.
* **Module:** A single namespace composed of multiple files, typically all in the same direcory.
  It acts like a single C# static class. 


```

my-workspace/               # <-- The Workspace Root (C# Solution)
├── workspace.json5         # <-- Global settings & package registry
│
├── billing-service/        # <-- PACKAGE 1: Compiles to an Executable
│   ├── package.json5       # <-- Optional metadata for BillingService package (C# .csproj)
│   ├── main.zurf           #     Both `main` and `app` belong to module `BillingService`
│   ├── app.zurf            #     they share private access across files
│   └── billing/            # <-- Subdirectory: Module `BillingService.Billing`
│       ├── invoice.zurf    #     Both `invoice` and `tax` belong to module `BillingService.Billing`
│       └── tax.zurf        #     the share private access across files
│
└── auth-library/           # <-- PACKAGE 2: Compiles to a Library (.dll)
    ├── package.json5       # <-- Optional metadata for AuthLibrary package (C# .csproj)
    ├── auth.zurf           #     Both `auth` and `tokens` belong to module `AuthLibrary`
    ├── tokens.zurf         #     they share private access across files
    └── crypto/             # <-- Subdirectory: Module `AuthLibrary.Crypto`
        └── hash.zurf       # <-- Belongs to module `AuthLibrary.Crypto`
```


## Package

A package is the basic unit of compilation used to generate an executable or library.  It consists
of a directory and all of its subdirectories.

## Module

A module is a single namespace composed of multiple files, typically all in the same direcory.

All source files must use the `mod` keyword to declare it's full module name (e.g.
`mod BillingService.Billing`, etc.). 

While it is recommended that the physical directory structure mirror the logical module
layout, this is not a hard requirement. The final authority on a module's identity is the
`mod` declaration at the top of the source file. 

Modules are closed compilation units. It is a compile-time error for a package to declare a
module name that clashes with a module in a referenced package, and no package may inject new
symbols or child modules into an existing module belonging to a different package.
This restriction does not apply within a package, although it should be considered bad
practice to do so.

## Package Scope Visibility

Circular dependencies may exist within a package, but may not exist between multiple packages. 

The `pub` keyword on a symbol (struct, function, etc.) makes it visible to **all other modules
within the same package.**

However, to be visible **outside** the package, both the symbol and its containing module path
must be marked `pub`. Therefore, a pub symbol inside a private module behaves exactly like an
internal declaration in C# — accessible anywhere inside the package, but hidden from external
consumers.

## Module Scope Visibility

All files inside a module are treated as part of the same scope, sharing full access
to each other’s private members.  It acts like a single C# static class.
Child and parent modules are separate entites and do not share any special relationship
regarding symbol visibility.

A module may be private or public. Every file in a module must specify the same
module visibility:

- `mod MyModule` declares a private module.
- `[pub] mod MyModule` declares a public module.

A module may contain types, interfaces, functions, and variables at module scope.
Each declaration may be private or public:

- `type struct MyType` declares a private type.
- `[pub] type struct MyType` declares a public type.

A type or function declaration without the `pub` keyword is strictly local to its exact 
module name. It can be seen and used by any file in that same module, but it is invisible
to parent, child, sibling modules, and other packages.


## Public Scope Visibility

Visibility across package boundaries relies on an unbroken chain of public access declarations
along the logical module path. An external package can access a symbol only if every dot-separated
module segment in its fully qualified path explicitly declares `[pub] mod`. For example, an external
consumer can access `AuthLibrary.Crypto.Hash` only if:

* `AuthLibrary` is declared as `pub mod AuthLibrary`
* `Crypto` is declared as `pub mod AuthLibrary.Crypto`
* `Hash` is a `pub` symbol within that module.

```text
[pub] module AuthLibrary
    -> [pub] module AuthLibrary.Crypto
        -> [pub] struct Hash
```

If any module segment along this path omits pub, the entire path beyond that point becomes 
invisible to external packages.


## Symbol Lookup and `use` Statements

All symbols declared in a module, whether private or public, are implicitly in
scope in every file belonging to that same module. No `use` statement is required.


Symbols declared in any other module require an explicit `use` statement in each file
that references them, even when both modules belong to the same package. 

## File Scope Visibility

Fields within a type are public if they start with a letter.  Fields that start with an
underscore are private to the file in which they are declared.


```text
type struct MyType
    _privateField Int   // Private field, visible only within this file
    publicField  Str    // Public field, visible to all scopes that have access to MyType
```

There may be some exceptions to allow private fields to be visible across multiple files
for code generation or other special cases.
