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

Zurfur is similar to C#, but borrows syntax and design concepts from
Golang.  Here are some differences between Zurfur and C#:

* Strings are UTF8 byte arrays
* Type declaration syntax and operator precedence is from Golang
* Built from the ground up using `ref` returns, so `list` acts exactly like `array`
* Interfaces connect to any object with matching signature (just like Golang)
* Lots of other differences, but if you're familiar with C# it'll all make sense

## Overview

I will write an architectural overview document, I promise.  But for now,
read this to get an idea of how it's put together: https://github.com/gosub-com/Bit

This isn't documentation, so much as thoughts about where to go:

* [Async](Doc/Async.md)
* [Class Objects](Doc/ClassObjects.md)
* [Func and Func Pointers](Doc/FuncAndFuncPointers.md)
* [Garbage Collector](Doc/GarbageCollector.md)
* [Multithreding](Doc/Multithreading.md)
* [Simple Intermediate Language (SIL)](Doc/Sil.md)
* [Stack Frames](Doc/StackFrames.md)

## Operator Precedence

    Primary: . () [] # (T)x
    Unary: + - ! ~ & *
    Exponentiation: **
    Multiplication and bits: * / % << >> & 
    Add and bits: + - | ^
    Range: ..
    Comparisons: == != === < <= > >= (must have parenthisis)
    Conditional: &&
    Conditional: ||
    Ternary: a ? b : c
    Lambda: ->
    Comma: ,
    Assignment Statement: = += -= *= /= %= &= |= ^= <<= >>= 

The `#` operator is the same as using `var` in front of a variable in C#.

Operator `==` does not default to object comparison, and only works when it
is defined for the given type.  Use `===` for object comparison.  Comparison
operators are not overloadable, however you can implement just one function,
`static func Compare(other MyType) int` to get all six relational operators.
Or, if you only care about equality, implement `Equals` to get `==` and
`!=` operators.

The `->` operator is only for lambda and not used to dereference a pointer.
Pointers are dereferenced by the `.` operator, just like a reference.

## Basic types

    int8, uint8, byte, int16, uint16, int32, int, uint32, uint, int64, uint64
    float32, float64, xint, xuint, decimal, string, array, list, map

`byte`, `int`, and `uint` are aliases for `uint8`, `int32`, and `uint32`.
`string` is an immutable array of UTF8 encoded bytes.  `xint` and `xuint` are
extended integer types, which could be 32 or 64 bits depending on run-time architecture.

`array<type>` is identical to an array.  Type definitions like `[]int` are
shorthand for `array<int>`.

`list<type>` works just like an array, but has a capacity and dynamic
size.  It's similar to C#'s `list`, except that it indexes using a `ref`
return.  It acts just like an array including the ability to modify a
field of a struct.

## Casting

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




