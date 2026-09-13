# Syntax 

## Whitespace and Offside Rule

Zurfur uses the *offside rule* to insert curly braces `{` and `}` automatically based on indentation.
Semicolons `;` are also inserted automatically based on a simple set of rules.

The basic scope rules:

1. Explicit (user typed) curly braces (`{` and `}`) are reserved exclusively for scope blocks and string
   interpolation. They are never used in an expression.
2. The *offside rule* is enabled by default.  When enabled, curly braces (`{` and `}`) are inserted
   automatically based on indentaition.  
3. Indentation is exactly four spaces per scope level. No tabs anywhere in the source code except within
   multi-line string literals.
4. The *offside rule* is disabled inside explicit curly brace blocks and also inside string interpolations.
   When disabled, whitespace is ignored, indentation rules are not enforced, and all enclosed scopes must
   also use explicit curly braces.
5. Compound statements (e.g., `if`, `while`, `for`, etc.) require a non empty body starting a new scope.
   This means that either the next statement is properly indented or there is an explicit scope block with `{}`

The basic line continuation rules:

1. Semicolons `;` are inserted automatically at the end of every line, unless the next line is a continuation. 
2. It's a continuation if:
   1. The line begins with an operator, including  `{`, `]`, `)`, `,`, `"`, `and`, `or`, `in`, `+`, `.`, `=`, etc.
      This allows either curly brace at the end of the line or curly brace on its own line style.
   2. The end of the previous line is `[`, `(`, `,`, or `=>`.
3. Line continuations must use hanging indentation unless inside an explicit scope block.
   The hanging indentation also applies to the scope of the line below it, so the hanging indentation
   is always 4 or 8 spaces depending on the scope invoved.

This deterministic rule set, processed after lexical analysis but before parsing, eliminates ambiguity
and makes the language easier for both humans and AI systems to generate and understand.

## Keywords `return` and `yield`

The `return` keyword always exits the function declaration it appears in, regardless of nesting depth.
Zurfur requires an explicit `return` statement at the end of every function that returns a value.
Explicit `return` statements make the returned value immediately obvious without requiring knowledge
of expression-vs-statement rules.  Functions that don't return a value can omit `return` entirely.

Within lambda expressions, the `yield` keyword is used to exit the lambda early and return control
to the calling code.  Using `yield` instead of `return` in lambdas eliminates ambiguity when reading
nested code, as it clearly indicates that the control flow is returning to the caller of the lambda
rather than exiting an outer function. 

## No need for `await` keyword

Zurfur does not use an `await` keyword. Async functions (`afun`) are called with the same syntax as sync
functions (`fun`) and automatically suspend until completion. The compiler prevents sync functions from 
calling async functions, maintaining clear boundaries in the type system. 

Editor tooling should visually distinguish async call sites (e.g., underlining or color-coding) to help
developers understand control flow. This eliminates the syntactic overhead of `await` keywords while
preserving the semantic clarity of structured concurrency, similar to Go's approach but with explicit 
async/sync separation at the function signature level.

While the default blocking behavior is appropriate for sequential logic, Zurfur provides the `astart`
keyword for launching async functions concurrently without blocking. The `astart` keyword starts an async
function in the background and immediately returns a `Task<T>`.


## Variables and Mutability

Local variable binding uses `let` for immutable, `mut` for mutable (but not assignable), `var` for
assignable (but not mutable), and `var mut` for assignable and mutable.  For example:

    // NOTE: getList returns List<Int>
    let a = getList         // `a` is un-assignable, the list is immutable
    mut b = getList         // `b` is un-assignable, the list is mutable
    var c = getList         // `c` is assignable, the list is immutable
    var mut d = getList     // `d` is assignable and the list is mutable

    // NOTE: getRoList returns ro List<Int>
    // `mut` and `var mut` are illegal because the list is immutable
    let a = getRoList       // `a` is un-assignable, the list is immutable
    var c = getRoList       // `c` is assignable, the list is immutable

    // NOTE: getStruct returns some struct type
    // `mut` and `var mut` are illegal because a struct is immutable
    let a = getStruct         // `a` is un-assignable, the struct cannot be modified
    var c = getStruct         // `c` is assignable, the struct can be modified


## Privacy

At the module level, functions, methods, and types are private to that module and it's children unless
the `[pub]` qualifier is specified.  

Fields are public by default but can be made private by prefixing them with an `_` underscore.  
Private fields can have public getters and setters. The scope of a private variable is the file
that it is declared in.

    [pub]                               // Make this type public
    type Example
        list1 List<Int> = [1,2,3]       // Public, initialized with [1,2,3]
        _list2 List<Int>                // Private, initialized with []
        _list3 List<Int> pub let        // Private, but with public read-only access
        _list4 List<Int> pub let mut    // Private, but with public modify, but not assignable

The public getter or setter has the same name as the private field, except without the leading `_`.

## String Literals and Interpolation

Strings (i.e. `Str`) are immutable byte lists (i.e. `ro List<Byte>`), generally assumed to hold UTF
8 encoded characters.  However, there is no rule enforcing the UTF8 encoding so they may hold any
binary data.

String literals start with a quote `"` (single line) or with `"""` (multi-line), and can be translated
at runtime using `tr"string"` syntax.  They are interpolated with dollar-braces (e.g `"${expression}"`).
Control characters may be put inside an interpolation (e.g. `"${\t}"` is a tab, `"${\r\n}"` is a carriage
return and newline).

![](Doc/Strings.png)

There is no `StringBuilder` type, use `List<Byte>` instead:

    let sb = mut List<Byte>()
    sb.push("Count from 1 to 10: ")
    for count in 1..+10
        sb.push(" ${count}")
    return sb.toStr()

## Span

Span is a view into a `List`, `ro List`, or `Str`, etc..  They are `type ref` and may never be stored
on the heap.  Unlike in C#, a span can be used to pass data to an async function.  

The declaration syntax `[]Type` translates to `Span<Type>`.  The following definitions are identical:

    // The following definitions are identical:
    fun writeData(data Span<Byte>) !Int
    fun writeData(data []Byte) !Int


Mutating the `len` or `capacity` of a `List` (not the elements of it) while there is a `Span` or
reference pointing into it is a programming error, and fails the same as indexing outside of array bounds.

    let list = mut List<Byte>()
    list.push("Hello Pat")      // list is "Hello Pat"
    let slice = mut list[6..+3] // slice is "Pat"
    slice[0] = "M"[0]           // slice is "Mat", list is "Hello Mat"
    list.push("!")              // Runtime failure with stack trace in log file

**TBD:** Consider how to `break` out of the lambda.  Use a return type of `Breakable`?

## Operators

Operator precedence is mostly from Golang, but more compatible
with C and gives an error where not compatible:

|Operators | Notes
| :--- | :---
|`.` `<T>` `.(T)` `[I]`  | **Primary:** Field access: `x.y`, Type argument: `f<type>(x)`, Type assertion: `x.(type)`, Index operator: `a[i]`
|`-` `~` `&` `not` `sizeof` `typeof` `unsafe` | **Unary:** The `~` operator is both xor and unary complement, same as `^` in Golang.
|`!`| Generate a value or short-circuit return an error for `Result` and `Maybe` when contained inside a function returning `Result` or `Maybe`. 
|`?`| Use default for `Maybe`, similar to `??` in C#.
|`@`| Capture the result of a sub-expression: `let a = fun1(x)@b + fun2(y)` captures the result of `fun1(x)` into `b`
|`!!!`| For `Result` and `Maybe`, generate value or panic.
|`is` `is not` `as` | Type conversion and comparison
|`<<` `>>`| Bitwise shift (can't mix arithmetic and bit operators, **TBD:** always require parentheses)
|`*` `/` `%` `&` | Multiply, divide, modulus, and bitwise *AND* (can't mix arithmetic and bit operators)
|`~`| Bitwise *XOR* (can't mix with arithmetic operators)
|`+` `-` `|` | Add, bitwise *OR* (can't mix arithmetic and bit operators)
|`..` `..+`| Range (Low..High) and range count (Low..+Count). Inclusive of low, exclusive of high. The range operator `..` takes two `Int`s and makes a `Range` which is a `type Range {high Int; low Int}`. The `..+` operator also makes a range, but the second parameter is a count (`high = low + count`).
|`==` `!=` `<` `<=` `>` `>=` `in` `not in`| Operator `==` does not default to object comparison and only works when it is defined by the given type. Comparisons are not associative, so `a == b == c` is illegal.
|`and`| Conditional *and*, short circuit
|`or`| Conditional *or*, short circuit
|`=>`| Lambda
|`key:value`| Key value pair, only allowed inside `()`, `[]` or where expected.
|`,`| The comma is a separator and not an expression.
|`=` `+=` `-=` `*=` `/=` `%=` `&=` `|=` `~=` `<<=` `>>=` | Assignment is a statement, so expressions `while (a = count) < 20` are illegal. The `@` operator can be used to capture a variable like this: `while count @ a < 20`.


### Operator Overloading

`+`, `-`, `*`, `/`, `%`, and `in` are the only operators that may be individually overloaded.  The
`==` and `!=` operator may be overloaded together by implementing `fun _opEq(a myType, b myType) bool`.
All six comparison operators, `==`, `!=`, `<`, `<=`, `>=`, and `>` can be implemented with just one
function: `fun _opCmp(a myType, b myType) Int`. If both comparison functions are defined, `_opEq` is
used for equality comparisons and `_opCmp` is used for the others. **TBD**: `_opCmpOrdered` vs `_opCmp`
for unordered?

## Statements


### While and Do Statements

The `while` loop is the same as C#.  There is no `do` statement, but it is easy to make one using `scope`.

### Scope Statement

The `scope` statement creates a new scope:

    scope
        let file = File.open("My File")
        doStuff(file)

    // File variable is out of scope here

The `scope` statement can be turned into a loop using the `continue` statement:

    scope
        doSomething()
        if weWantToRepeat()
            continue

Likewise, `break` can be used to exit the scope early.

### For Loop

For the time being, `for` loops only allow one format: `for newVariable in expression`. 
The simplest form of the for loop is when the expression evaluates to an integer:

    // Print the numbers 0 to 9
    for i in 10
        Log.info("${i}")

    // Print numbers from 1 to 10
    for i in 1..+10
        Log.info("${i}")

    // Increment all the numbers in a list
    for i in list.len
        list[i] += 1

    // Log key value pairs of all elements in a map
    for kv in map
        Log.info("Key: ${kv.key} is ${kv.value}")

When iterating over a collection, structurally invalidating operations such as adding or removing
elements require `unique`. If there is an outstanding borrowed view into the collection, the
operation will fail unless uniqueness can be proven.

### Switch

This hasn't been implemented, but the syntax is reserved:

    switch myEnum
    case Hello
        doHello
        doOtherHelloStuff
    case Coming, Going
        doComingAndGoing
    case Goodbye
        doGoodbye
    default
        // Other cases may be added in the future, but it's not a compile time error
        doDefualtStuff

Each case defines it's own scope and there is an implicit `break` at the end of each case.
This mirrors `if ... elif ... else` statements, but also compile-time checks that all
cases of an enum are handled.

Switch expressions will be supported, but they will require using the `yield` keyword.

    let result = switch myEnum
    case Hello
        yield "Hello"
    case Coming, Going
        yield "Coming or Going"
    case Goodbye
        yield "Goodbye"
    default
        yield "Other"


