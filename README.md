# ![Logo](Zurfur.jpg) Zurfur

Zurfur is is a programming language I'm designing just for fun and enlightenment.
The language is named after our cat, Zurfur, who was named by my son.  It's
spelled **_ZurFUR_** because out cat has fur.

## Design Goals

* Fun and easy to use 
* Managed code is safe, efficient, and garbage collected
* Unmanaged code is just as efficient as C++
* Ahead of time compile to WebAssembly with tiny run-time library
* Stretch goal: Rewrite compiler and IDE in Zurfur on Node.js

![](Doc/IDE.png)

Zurfur is similar to C#, but borrows syntax and design concepts from
Golang.  Here are some differences between Zurfur and C#:

* Strings are UTF8 byte arrays initialized to "" by default
* Structs are immutable by default
* Type declaration syntax and operator precedence is from Golang
* Built from the ground up using `ref` returns, so `List` acts exactly like `Array`
* Interfaces connect to any object with matching signature (just like Golang)
* Lots of other differences, but if you're familiar with C# it'll all make sense

## Overview

Read about the lexer, editor, and parser here: https://github.com/gosub-com/Bit

Thoughts about where to go and how to do it:

* [Async](Doc/Async.md)
* [Class Objects](Doc/ClassObjects.md)
* [Func and Func Pointers](Doc/FuncAndFuncPointers.md)
* [Garbage Collector](Doc/GarbageCollector.md)
* [Multithreding](Doc/Multithreading.md)
* [Simple Intermediate Language (SIL)](Doc/Sil.md)
* [Stack Frames](Doc/StackFrames.md)

## Hello world

    pub static func main()
    {
	    Console.Log("Hello World")
    }

Functions are declared with the `func` keyword.

## Operator Precedence

    Primary: . () [] @ (T)x
    Unary: + - ! ~ & ^
    Multiplication and bits: * / % << >> & 
    Add and bits: + - | ~
    Range: .. ::
    Comparison: == != < <= > >= === !== in
    Conditional: &&
    Conditional: ||
    Ternary: a ? b : c
    Lambda: =>
    Assignment Statements: = += -= *= /= %= &= |= ~= <<= >>= 
    Comma: ,

The `@` operator is the same as using `var` in front of a variable in C#.

TBD: The `*` operator is only for multiplication.   The `^` operator is
only for dereferencing pointers.  The `~` operator is used for both
unary complement and binary xor.  These changes were made so the parser can
insert invisible `;`'s on lines that don't begin with a binary operator. 

The `->` operator is not used.  Pointers are dereferenced by the `.` operator,
as if they were regular references.  TBD: Change lambda from `=>` to `->`?

Operator `==` does not default to object comparison, and only works when it
is defined for the given type.  Use `===` for object comparison.  Comparison
operators are not overloadable, however you can implement just one function,
`static func Compare(a myType, b myType) int` to get all six relational operators.
Or, if you only care about equality, implement `static func Equals(a myType, b myType) bool`
to get `==` and `!=` operators.


## Basic types

    int8, uint8, byte, int16, uint16, int32, int, uint32, uint, int64, uint64
    float32, float64, xint, xuint, decimal, string, Array, List, Map

`byte`, `int`, and `uint` are aliases for `uint8`, `int32`, and `uint32`.
`string` is an immutable array of UTF8 encoded bytes.  `xint` and `xuint` are
extended integer types, which could be 32 or 64 bits depending on run-time
architecture.

TBD: Lower case `array`, `list`, and `map`?  `span`, `roSpan`?

`Array<type>` is identical to an array.  Type definitions like `[]int` are
shorthand for `Array<int>`.

`List<type>` works just like an array, but has a capacity and dynamic
size.  It's similar to C#'s `List`, except that it indexes using a `ref`
return.  It acts just like an array including the ability to modify a
field of a struct.

## Namespace/using

Similar to C# namespaces, but a little more like modules since
static functions, variables, constants, and extension methods may be declared
at the namespace level.  `using Zurur.Math` imports the intrinsic math functions,
`Cos`, `Sin`, etc. into the global symbol table.  If you want to froce them to be
prefixed with `Math.`, it can be done with `using Math=Zurfur.Math`.

TBD: Do we want to use the keyword `module` and `import` instead?  We'll
keep `namespace` for now since a module may contain different namespaces.

## Classes

A class is a heap only object, pretty much the same as in C#.  Field
definitions are `field` followed by `type`, just like in go lang:

    pub class Example
    {
	    pub F1 string                             // F1 is a string initialized to ""
	    pub F2 Array<int>;                        // F2 is an array of int, Initialized to null
	    pub F3 List<string>({"Hello", "World"})   // F3 is an initialized list
	    pub F4 Map<string,int>({{"A",1},{"B",2}}) // F4 is an initialized map
	    pub func Func1(a int) float => F1 + a + " hi"  // Func1 is a member function
    }

Extension methods are defined outside the class and may be placed directly in
the namespace.  An extension method of `example` looks like this:

    pub func Example::MyExtension() => Func1(10)

TBD: Use `fn` instead of `func`?

## Cast

Casting is used much less than in C# because a cast is not used to covert
struct types.  A cast such as `(int)myFloat` should be written as
`int(myFloat)`.  Casts are only used to convert between class/interface
types or for pointer conversions.

The cast construct is determined at the parse level.  Whenever a closing
parenthisis `)` is found, if the next symbol is an identifier or an open
parenthisis `(`, it's a cast.  Otherwise, it is not.  For example,
`(a)b`, `(a)(b)` are always casts regardless of what `a` or `b` is.
`(a+b)c` is always an invalid cast.  `(a)*b` is never a cast.  If you
need to cast a dereferenced pointer, an extra parenthisis is required
as in `(a)(*b)`.





## Open Questions

Do we want lower case to be private by default and upper case to
be public similar to Golang?  My personal preference is to have 
`pub` be explicit even though it is a bit tedious.

Should we switch to i32, i64, f32, f64, etc., like WebAssembly?

Should `Array`, `Map`, and `List` be lower case since they are almost as
basic as `string` and `decimal`?  If so, should `Span` and `RoSpan` be lower
case?  My preference is leaning toward them lower case but leaving `Span` and
`RoSpan` upper case.  What about `Sin`, `Cos`, etc.?

Should NAN`s compare the way IEEE numbers are supposed to? Since this is a new
language, my preference would be to have them compare so NAN != NAN and they
sort so NAN > INF.  This way we don't have to worry about doing special
things for Map or sorted collections.  OTOH, it could be confusing when
porting code from other places.  OTOOH, I've had plenty of problems with
the IEEE comparison behavior as it is.




