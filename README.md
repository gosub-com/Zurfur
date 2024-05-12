# Zurfur

![Logo](Zurfur.jpg)

I am moving the documentation to
[Confluence](https://zurfur.atlassian.net/wiki/external/ZjJlYjUwZmIzMzg0NGJkY2ExMmJlY2MwNDVlNTU4ODU)

Zurfur is a programming language I'm designing for fun and enlightenment.
The language is named after our cat, Zurfur, who was named by my son.  It's
spelled **_ZurFUR_** because our cat has fur. The syntax is still
being developed and nothing is set in stone.  If you want to try it, click 
here https://gosub.com/zurfur

I love C#.  It's my favorite language to program in.  But, I'd like to have
some features from other languages built in from the ground up.  I'm thinking
about ownership, immutability, nullability, and functional programming.

Status update: Still porting to https://avaloniaui.net so it runs in the browser.

![](Doc/IDE.png)

## Design Goals

Zurfur takes its main inspiration from C#, but borrows syntax and design
concepts from 
[Lobster](https://strlen.com/lobster/), 
[Zig](https://ziglang.org/), 
[Midori](https://joeduffyblog.com/2016/02/07/the-error-model/), 
Golang, Rust, Python, JavaScript, and other languages.

* **Prime directives:**
    * Fun and easy to use
    * Faster than C# and unsafe code just as fast as C
    * Target WebAssembly in the browser with easy JavaScript interop
* **Ownership, mutability, and nullabilty are part of the type system:**
    * All objects are value types except for pointers (e.g. `^MyType`) and borrowed references (e.g. `&myValue`)
    * `ro` means read only *all the way down* (not like C#, where `readonly` only protects the top level)
    * Function parameters must be explicitly marked `mut` if they mutate anything
    * References and pointers are non-nullable, but may use `?MyType` or `?^MyType` for nullable
    * Deterministic destructors (e.g. `FileStream` closes itself automatically)
* **Fast and efficient:**
    * Return references and span used everywhere. `[]int` is `Span<int>`
    * Functions pass parameters by reference, but will pass a copy when it is more efficient
    * Explicit `clone` required when copying an object that requires dynamic allocation
    * Most objects are deleted without needing GC.  Heap objects are reference counted.

## Variables and Mutability

Variables are declared and initialized with the `let` or `var` keyword. These keywords
refer to the assignability of the variable. `let` for un-assignable, `var` for
assignable. In both cases, the object they point to is immutable unless the `mut` keyword
is used.  For example:

    let a = getList()        // a cannot be assigned, and the list is immutable
    let b mut = getList()    // b cannot be assigned, but the list is mutable
    var c = getList()        // c can be assigned, but the list is immutable
    var d mut = getList()    // d can be assigned, and the list is mutable

It is an error to use `var` when `let` is sufficient, or to use `mut` when it is not needed.
  
## Types

The ones we all know and love:

    nil, bool, i8, byte, i16, u16, i32, u32, int, u64, f32, float, str

`int` and `float` are 64 bits wide.  `str` is an array of bytes.

| Type | Description
| :--- | :---
| List\<T\> | Re-sizable mutable list of elements.  `ro List\<T\>` is the immutable counterpart. 
| Span\<T\> | A view into a `List`.  It has a constant length.  Mutability of elements depends on usage (e.g Span from `ro List` is immutable, Span from `List` is mutable)
| Map<K,V> | Unordered mutable map.  `ro Map<K,V>` is the immutable counterpart. 
| Maybe\<T\> | Identical to `?T`.  Always optimized for pointers and references.
| Result\<T\> | Same as `!T`. An optional containing either a return value or an `Error` interface.
| Error | An interface containing a `message` string and an integer `code`
| str, str16 | A `ro List<byte>` or `ro List<u16>` with support for UTF-8 and UTF-16.  `str16` is a JavaScript or C# style Unicode string

All types have a compiler generated `ro` counterpart which can be copied
very quickly since cloning them is just copying a reference without dynamic
allocation.

### Privacy

At the module level, functions, methods, and types are private to that
module and it's children unless the `[pub]` qualifier is specified.  

Fields are public by default but can be made private by prefixing them
with an `_` underscore.  Private fields can have public getters and setters.
The scope of a private variable is the file that it is declared in.

    [pub]                               // Make this type public
    type Example
        list1 List<int> = [1,2,3]       // Public, initialized with [1,2,3]
        _list2 List<int>                // Private, initialized with []
        _list3 List<int> pub let        // Private, but with public read-only access
        _list4 List<int> pub let mut    // Private, but with public modify, but not assignable

The public getter or setter has the same name as the private field, except without the leading `_`.

### Strings

Strings (i.e. `str`) are immutable byte lists (i.e. `ro List<byte>`), generally
assumed to hold UTF8 encoded characters.  However, there is no rule enforcing
the UTF8 encoding so they may hold any binary data.

String literals start with a quote `"` (single line) or with `"""` (multi-line), and
can be translated at runtime using `tr"string"` syntax.  They are interpolated
with curly braces (e.g `"{expression}"`). Control characters may be put inside
an interpolation (e.g. `"{\t}"` is a tab).

![](Doc/Strings.png)

There is no `StringBuilder` type, use `List<byte>` instead:

    @sb = List<byte>()
    sb.push("Count from 1 to 10: ")
    for @count in 1..+10
        sb.push(" {count}")
    return sb.toStr()

### Span

Span is a view into a `List`, `ro List`, or `str`, etc..  They are `type ref` and
may never be stored on the heap.  Unlike in C#, a span can be used to pass
data to an async function.  

The declaration syntax `[]Type` translates to `Span<Type>`.  The following
definitions are identical:

    // The following definitions are identical:
    fun writeData(data Span<byte>) !int
    fun writeData(data []byte) !int


Mutating the `len` or `capacity` of a `List` (not the elements of it) while
there is a `Span` or reference pointing into it is a programming error, and
fails the same as indexing outside of array bounds.

    let list = mut List<byte>()
    list.push("Hello Pat")      // list is "Hello Pat"
    let slice = mut list[6..+3] // slice is "Pat"
    slice[0] = "M"[0]           // slice is "Mat", list is "Hello Mat"
    list.Push("!")              // Runtime failure with stack trace in log file

**TBD:** Consider how to `break` out of the lambda.  Use a return type of `Breakable`?

## Operators

Operator precedence is mostly from Golang, but more compatible
with C and gives an error where not compatible:

|Operators | Notes
| :--- | :---
|`x.y`  `f<type>(x)` `x.(type)` `a[i]` | Primary
|- ~ & `ref` `not` `sizeof` `typeof` `unsafe` | Unary
|@| Capture new variable
|?| Use default for `Maybe`
|!| For `Result` and `Maybe`, generate value or throw error when `nil`
|!!!| For `Result` and `Maybe`, generate value or panic when `nil`
|`is` `is not` `as` | Type conversion and comparison
|<< >>| Bitwise shift (can't mix arithmetic and bit operators, **TBD:** always require parentheses)
|* / % & | Multiply, divide, modulus, and bitwise *AND* (can't mix arithmetic and bit operators)
|~| Bitwise *XOR* (can't mix with arithmetic operators)
|+ - &#124; | Add, bitwise *OR* (can't mix arithmetic and bit operators)
|.. ..+| Range (Low..High) and range count (Low..+Count).  Inclusive of low, exclusive of high. 
|== != < <= > >= === !== `in` `not in`|Not associative, === and !== is only for pointers
|`and`| Conditional *and*, short circuit
|`or`| Conditional *or*, short circuit
|`a ?? b : c`| Ternary operator.  Not associative, no nesting (see below for restrictions)
|=>| Lambda
|key:value| Key value pair (only inside `()`, `[]` or where expected)
|,| Comma Separator (not an expression)
|= += -= *= /= %= &= |= ~= <<= >>=| Assignment Statements (not an expression)


The `~` operator is both xor and unary complement, same as `^` in Golang.

The `@` operator captures the expression into a new variable.

The `!` opererator passes an error up to the caller when a `Result` has an
`Error`.  For example `while stream.read(buffer)!@length != 0` passes
an error up to the caller, or captures the value returned by `read` into the
new variable `length`.

The range operator `..` takes two `int`s and make a `Range` which is a
`type Range(High int, Low int)`.  The `..+` operator also makes a
range, but the second parameter is a count (`High = Low + Count`).  

Operator `==` does not default to object comparison, and only works when it
is defined for the given type.  Use `===` and `!==` for object comparison. 
Comparisons are not associative, so `a == b == c` is illegal.

**TBD:** The ternary operator is not associative and cannot be nested.  
Examples of illegal expresions are `c1 ?? x : c2 ?? y : z` (not associative),
`c1 ?? x : (c2 ?? y : z)` (no nesting).  The result expressions may not
directly contain an operator with lower precedence than range.
For example, `a==b ?? x==3 : y==4` is illegal.  Parentheses can be
used to override that behavior, `a==b ?? (x==3) : (y==4)` and
`a==b ?? (@p=> p==3) : (@p=> p==4)` are acceptable.

The pair operator `:` makes a key/value pair which can be used
in a list to initialize a map.

Assignment is a statement, not an expression.  Therefore, expressions like
`a = b = 1` and `while (a = count) < 20` are not allowed. In the latter
case, use `while count@a < 20`.  Comma is also not an expression and may
only be used where they are expected, such as a function call or lambda.

#### Operator Overloading

`+`, `-`, `*`, `/`, `%`, and `in` are the only operators that may be individually
overloaded.  The `==` and `!=` operator may be overloaded together by implementing
`fun _opEq(a myType, b myType) bool`.  All six comparison operators,
`==`, `!=`, `<`, `<=`, `==`, `!=`, `>=`, and `>` can be implemented with just
one function: `fun _opCmp(a myType, b myType) int`.  If both comparison functions
are defined, `_opEq` is used for equality comparisons, and `_opCmp` is used
for the others.  **TBD**: `_opCmpOrdered` vs `_opCmp` for unordered?

## Statements

Like Golang, semicolons are required between statements but they are
inserted automatically at the end of lines based on the last non-comment
token and the first token of the next line. 

Unlike Golang and C#, compound statements (`if`, `else`, `while`, `for`, lambdas, etc.)
can accept multiple lines without needing braces.  The indentation is checked to make
sure it matches the expected behavior.

1. Indentation is four spaces per scope level. No tabs anywhere in the source code except within multi-line string literals
2. One statement per line, unless it's a continuation line.  It's a continuation line if:
   1. The end of the previous line is `[`, `(`, `,`, or `=>`.
   2. The line begins with an operator, including  `]`, `)`, `,`, `"`, `and`, `or`, `in`, `+`, `.`, `=`, etc.
3. Compound statements (e.g. `if`, `while`, `for`, etc.) may use or omit curly braces, but the convention is to omit them.

#### While and Do Statements

The `while` loop is the same as C#.  There is no `do` statement, but it is easy to make one using `scope`.

#### Scope Statement

The `scope` statement creates a new scope:

    scope
        let file = File.open("My File")
        doStuff(file)

    // File variable is out of scope here

The `scope` statement can be turned into a loop using the `continue` statement:

    scope
        DoSomething()
        if WeWantToRepeat()
            continue

Likewise, `break` can be used to exit the scope early.

#### For Loop

For the time being, `for` loops only allow one format: `for newVariable in expression`. 
The simplest form of the for loop is when the expression evaluates to an integer:

    // Print the numbers 0 to 9
    for i in 10
        Log.info("{i}")

    // Print numbers from 1 to 10
    for i in 1..+10
        Log.info("{i}")

    // Increment all the numbers in a list
    for i in list.len
        list[i] += 1

    // Log key value pairs of all elements in a map
    for kv in map
        Log.info("Key: {kv.key} is {kv.value}")

When iterating over a collection, just like in C#, it is illegal to add
or remove elements from the collection. 

#### Switch and Match

Both `switch` and `match` are reserved for future use.  For now, use `if`,
`elif`, and `else` to simulate them:

    if myNum < 1
        DoStuff()
        DoOtherStuff()
    elif myNum in 1..3
        DoMoreStuff()
    else myNum >= 3
        DoTheLastThing()

## Packages and Modules

A package is like a C# assembly.  It is the basic unit for distributing
a library or application and is a .`zip` file with a `.zil` extension.
It will be defined here [ZIL Specification](Doc/Zil.md).

Modules are like a C# static class and namespace combined.  They can contain
static functions, fields, and extension methods.  From within a package,
module names act like namespaces and stitch together just as they do in C#.
From outside the package, they look and act like a C# static class.

The `mod` keyword does not nest, or require curly braces.  The module name
must be declared at the top of each file, after `use` statements, and before
type, function, or field definitions.  A file may contain other modules, but
all of them must be nested inside the top level module:


    mod MyCompany.MyProject               // Top level module
    mod MyCompany.MyProject.Utils         // Ok since it is nested in the top level
    mod MyCompany.MyProject.OtherUtils    // Ok since it is also nested
    mod MyCompany.MyOtherProject          // ILLEGAL since it is not nested

Package names should be unique across the world, such as a domain name
followed by a project (e.g. `com.gosub.zurfur`).  For now, top level module
names must be unique across an entire project.  If there are any top level
module name clashes, the project will fail to build.  In the future, there
may be syntax or project settings to resolve that.
