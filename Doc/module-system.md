# Module System and Visibility


Code is organized using a nested hierarchy: Package ➔ Module ➔ Namespace.

* **Package:** The project root and basic unit of distribution. It groups related modules,
  manages global configuration, and defines the project scope.
* **Module:** The basic unit of compilation, used to generate a single executable or library DLL.
  It is similar to a C# project (.csproj) or assembly.
* **Namespace:** A logical scope containing source files, usually located in the same directory.
  The top-level namespace must have the same name as the module. A namespace is not the same as a C#
  namespace. It is more like a static class that contains types, functions, and static variables.

A **package** is distributed, a **module** is compiled, and a **namespaces** organize the files inside.

```

MyPackage/                  # <-- The project root (like a C# solution)
├── package.json            # <-- Global settings and package dependencies, etc.
│
├── BillingApp/             # <-- MODULE 1: Compiles to an Executable.
│   ├── main.zurf           #     Both `main` and `app` belong to the namespace `BillingApp`
│   ├── app.zurf            #     and they share private access across files
│   └── Billing/            # <-- Subdirectory: namespace `BillingApp.Billing` 
│       ├── invoice.zurf    #     Both `invoice` and `tax` belong to namespace `BillingApp.Billing`
│       └── tax.zurf        #     and they share private access across files
│
└── AuthLibrary/            # <-- MODULE 2: Compiles to a Library (.dll)
    ├── auth.zurf           #     Both `auth` and `tokens` belong to the namespace `AuthLibrary`
    ├── tokens.zurf         #     and they share private access across files
    └── crypto/             # <-- Subdirectory: namespace `AuthLibrary.Crypto`
        └── hash.zurf       # <-- Belongs to the namespace `AuthLibrary.Crypto`
```


## Package

A package is the project root and the basic unit of distribution.  The `package.json` 
identifies the package identity, module registry, and dependencies.

## Module Types

A module is the basic unit of compilation used to generate an executable or library. It consists
of a directory and all its subdirectories. The types of module are:

* **App:** The root application executable containing a `main` entry point. Global mutable state
  is allowed, but is private to the app and is not shared with other apps or agents.
* **Agent:** A blueprint for an isolated, concurrent unit of execution. The runtime instantiates
  each agent into its own separate execution box.  They expose a strictly asynchronous public API
  used to communicate with the app or other agents via message passing. Global mutable state is
  allowed, but is private to that agent instance. An agent is similar to an operating system process
  or a web worker in JavaScript.
* **Service:** A stateful library containing global mutable state. Each app or agent using the service
  gets its own copy of that state.
* **Lib:** A stateless library containing reusable code and global immutable state. Each app or agent
  using the library gets its own copy of the global immutable state. **Note:** There might be an
  exception to allow mutable state for diagnostic hooks, counters, and timers, etc.

The purpose of differentiating libraries from services is to make it clear that using a service may
introduce a large amount of global mutable state into an app or agent, which may be undesirable.
A service acts as an infrastructure singleton localized to the current execution unit. While it provides
centralized, stateful APIs (like file systems or caches), WebAssembly's sandboxed isolation ensures that
each app and agent instantiates its own entirely private copy of the service's state.

Circular dependencies may exist within a module, but not between modules.

## Namespace

A namespace is a logical scope containing source files, not including child namespaces. Namespaces
are similar to C# static classes in that they may include global static data. The physical directory
structure should mirror the logical namespace structure, but it is not authoritative; each source file's
declaration determines its namespace.

Every source file must declare its namespace using the `app`, `agent`, `lib`, or `service` keyword
and the top level namespace must have the same name as the module.  The root namespace `BillingApp` is
an app namespace, so its files must all use the declaration `app BillingApp`. The child namespace
`BillingApp.Billing` is a library namespace, so its files must use `lib BillingApp.Billing`.

Child namespaces cannot be `app` or `agent`, therefore the entire *public* API of the *app* or
*agent* must be defined in the top level namespace.  Child namespaces may be `lib` or `service`,
but then they are not independent units of compilation.  Instead, they are compiled into the parent
module, have access to the parent's members following normal scope visibility rules.  
A `service` may contain a `lib`, but a `lib` may not contain a `service`.

A namespace can be either public or private. All files in a given namespace must have the same
visibility. For example, declaring `[pub] lib MyLib.Utils` in one file and `lib MyLib.Utils` in
another is illegal.

## Namespace Scope Visibility

All files inside a namespace are treated as part of the same scope, sharing full access to each
other's private members. Child and parent namespaces are separate entities and do not share any
special relationship regarding symbol visibility.

A type or function declaration without the `pub` keyword is strictly local to its exact 
namespace. It can be seen and used by any file in that same namespace, but it is invisible
to parent, child, or sibling namespaces, or to other packages.  This is similar to `internal`
in C#, except that it is scoped to that specific namespace instead of the entire module.

    // Visible to all members in this namespace, and nowhere else.
    type struct MyType {}

## Module Scope Visibility

The `pub` keyword on a symbol (struct, function, etc.) makes it visible to **all other files
within the module.**

    // Visible to all files in this module.
    // Maybe visible to external modules, but only if contained in a pub namespace.
    [pub] type struct MyType {}

## Public Scope Visibility

To be visible **outside** the module, both the symbol and its containing namespace path
must be marked `pub`. Therefore, a `pub` symbol inside a private namespace behaves exactly 
like an `internal` declaration in C# — accessible anywhere inside the module, but hidden
from external consumers.

Visibility across module boundaries relies on an unbroken chain of public access declarations
along the logical namespace path. An external module can access a symbol only if every dot-separated
namespace segment in its fully qualified path explicitly declares `[pub] lib`. For example, an external
consumer can access `AuthLibrary.Crypto.Hash` only if:

* `AuthLibrary` is declared as `pub lib AuthLibrary`
* `Crypto` is declared as `pub lib AuthLibrary.Crypto`
* `Hash` is a `pub` symbol within that module.

```text
[pub] lib AuthLibrary
    -> [pub] lib AuthLibrary.Crypto
        -> [pub] struct Hash
```

If any namespace segment along this path omits `pub`, the entire path beyond that point becomes
invisible to external modules.


## Symbol Lookup and `use` Statements

All symbols declared in a namespace, whether private or public, are implicitly in
scope in every file belonging to that same namespace. No `use` statement is required.


Symbols declared in any other namespace require an explicit `use` statement in each file
that references them, even when both namespaces belong to the same module. 

## File Scope Visibility

Fields within a type are public if they start with a letter. Fields that start with an
underscore are private to the file in which they are declared.


```text
type struct MyType
    _privateField Int   // Private field, visible only within this file
    publicField  Str    // Public field, visible to all scopes that have access to MyType
```

There may be exceptions that allow private fields to be visible across multiple files
for code generation or other special cases.
