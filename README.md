# ![Logo](Zurfur.jpg) Zurfur

Zurfur is is a programming language I'm designing just for fun and enlightenment.
The language is named after our cat, Zurfur, who was named by my son.  It's
spelled **_ZurFUR_** because our cat has fur.

## Overview

I love C#.  It's my favorite language to program in.  But, I'd like to fix
some [warts](http://www.informit.com/articles/article.aspx?p=2425867) and have
some features from other languages built in from the ground up.

Thoughts about where to go and how to do it: [Internals](Doc/Internals.md).

## Design Goals

* Fun and easy to use (almost as easy as C#, avoid C++ complexity)
* Safe code is more efficient than C# (more value types, less GC)
* Unsafe code is just as efficient as C (drop down to pointers when necessary)
* Ahead of time compile to WebAssembly with tiny run-time library
* Stretch goal: Rewrite compiler and IDE in Zurfur on Node.js

![](Doc/IDE.png)

Zurfur is similar to C#, but borrows syntax and design concepts from Golang,
Rust, and many other languages.  Here are some differences between Zurfur and C#:

* Strings are UTF8 byte arrays, always initialized to ""
* Struct and class are both value types created on the stack unless explicitly boxed
* Stack allocated classes have deterministic finalization (no garbage collection)
* References are non-nullable by default, may use `?` for nullable
* Functions pass parameters by reference, unless it's more efficient to pass by value
* Function parameters are immutable by default, must explicitly be mutable
* Type declaration syntax and operator precedence is from Golang
* Interfaces may be implemented by the class (C# style) or externally (Rust style)
* Get/set of mutable structs acts like you think it should (e.g. `MyListOfStructPoints[1].X = 5`)
* `==` operator fails if it is not defined on a class

#### Status Update

The syntax is still being developed, nothing is set in stone yet.  Feel
free to send me comments letting me know what you think should be changed.

## Functions

    /// This is a public documentation comment.  Do not use XML.
    /// Use `name` to refer to variables in the code. 
    pub static fun Main(args []str)
    {
        // This is a regular private comment
        Console.Log("Hello World, 2+2=" + add(2,2))
    }

    // Regular static function
    pub static fun add(a int, b int) int
        => a + b

    // Extension method for MyClass
    pub fun MyClass::MyExtensionFunc(a str) str
        => a + ": " + memberVariable


Functions are declared with the `fun` keyword. The type names come
after each argument, and the return type comes after the parameters.
Functions, classes, structs, enums, variables and constants are
private unless they have the 'pub' qualifier.  Functions are allowed
at the namespace level, but must be static or extension methods.

#### Parameters are passed by reference and immutable by default

Unlike in C#, a `class` is owned by its creator.  Its reference
cannot be captured when passed to a function.  This allows classes to
be created on the stack without needing garbage collection. 

By default, functions pass parameters by immutable reference.  The exception
is that small structs may be passed by value when it is more efficient to do so.

    pub fun Test( a      f64,       // Pass by value since that's more efficient
                  s      MyStruct,  // Pass by value or reference whichever is more efficient
                  ms mut MyStruct,  // Pass by reference, preserve `ro` fields
                  rs ref MyStruct,  // Pass by reference, nothing is preserved
                  os out MyStruct,  // MyStruct is returned by reference
                  c      MyClass,   // Pass by value or reference, the reference cannot be captured
                  mc mut MyClass,   // Pass by reference, object is mutable, reference cannot be captured
                  ic ref MyClass,   // ILLEGAL
                  oc out MyClass)   // MyClass is returned by reference, object is mutable

If `s` or `c` is big, such as a matrix containing 16 floats, it is passed by
reference.  If they are small, such as a single float or int, they are passed
by value.  A struct or class containing two integers might be passed by value or
by reference depending on the compiler, options, and optimizations.

## Local Variables

Within a function, variables are declared and initialized with the `@` symbol,
which is similar to `var` in C#.

    @a = 3                              // `a` is an int
    @b = "Hello World"                  // `b` is a str
    @c = MyFunction()                   // `c` is whatever type is returned by MyFunction
    @d = List<int>([1,2,3])             // `d` is a list of integers, intialized with {1,2,3}
    @e = Map<str,int>({"A":1,"B":2})    // `e` is a map of <str, int>
    @f = Json({"A":1,"B":[1,2,3]})      // `f` is a Json object containing a number and an array
   
The above form `@variable = expression` creates a variable with the same type as
the expression.  A second form `@variable type [=expression]` creates an explicitly
typed variable with optional assignment from an expression.  If the expression
cannot be converted, an error is generated

    @a int = MyIntFunc()                // Error if MyIntFunc returns a float
    @b str                              // `b` is a string, initialized to ""
    @c List<int>                        // `c` is an empty List<int>
    @d List<int> = [1, 2, 3]
    @e Map<str,int> = MyMapFunc()       // Error if MyMapFunc doesn't return Map<str,int>
    @f Map<int,str> = {0:"a", 1:"b"}

#### Non-Nullable References

References are non-nullable (i.e. may never be `null`) and are initialized
when created.  The optimizer may decide to delay initialization until the
variable is actually used which could have implications if the constructor
has side effects.  For instance:

    @myList List<int>()             // Optimizer may remove this constructor call
    if MyFunc()
        { myList = MyListFunc() }   // Constructor might not be called above
    else
        { myList.Add(1) }           // Optimizer may move constructor call here

It is possible to create a nullable reference.
    
    @myNullStr ?str         // String is null
    @myEmptyStr ?str()      // String is ""

A non-nullable reference can be assigned to a nullable, but a cast
or conditional test must be used to convert nullable to non-nullable.  

Pointers are always nullable and they default to null.  Pointers can
only be used in an unsafe context, and it is up to you to make sure
they are not null before being used.

## Basic types

    i8, u8, byte, i16, u16, i32, int, u32, uint, i64, u64, f32, f64
    xint, xuint, decimal, object, str, strs, Array<T>, List<T>, Map<K,V>, Json
    Span<T>

`byte`, `int`, and `uint` are aliases for `u8`, `i32`, and `u32`.
`xint` and `xuint` are extended integer types, which could be 32 or 64 bits
depending on run-time architecture.

`str` is an immutable UTF8 byte array.`

`Span<T>` is a slice of an array.

**TBD:** Use lower case for `array`, `list`, `map`, `json`, `span`, `roSpan`, and
others library class types?  Or use upper case for `Str`, `i8`, etc?

#### Strings

Strings are immutable byte arrays, generally assumed to hold UTF8 encoded characters.
However, there is no rule enforcing the UTF8 encoding so they may hold any binary data.

String literals in source code can start with either a quote `"` or a backtick
`` ` ``, which is useful if the string conains a quote.  They can be translated
using `tr"string"` syntax, and may be followed by an escape constant such as
`crlf` or an `{interpolation-expression}`.

    @a = "Regular string literal"
    @b = "Column1" tab "Column2" tab "Column3" crlf     // Containing tabs and crlf
    @c = tr"Translated string"
    @d = "Hello world, 2+2="{2+2}"!"crlf                // Interpolated with crlf at end
    @e = `Jack says "Hello World!"`                     // Include quote character

Escape constants are `cr`, `lf`, `crlf`, `tab`, `ff`, `bs`

**TBD:** Remove quote strings and use only backtick?

#### Array

`Array` is your standard C# fixed sized array.  The constructor takes the count,
which can never be changed after that.  An an array has an immutable `Count` property
instead of the `Length` property in C#

    @a Array<int>               // `a` is an array of length zero
    @b Array<int>(32)           // `b` is an array of length 32
    @c Array<Array<int>>(10)    // `c` is an array of 10 arrays of integer

The C# syntax for creating an array with `[]` has been dropped in
favor of a generic array class implementing `ICollection`, and support
for initializer expressions:

    @a Array<int> = [1,2,3]     // Instead of 'var a = new int[]{1,2,3}
    @b = Array<int>([1,2,3])    // Alternative way of initializing the array
    @c Array<Array<int>> = [[1,2,3], [4,5,6], [7,8,9]]  // Jagged matrix

Arrays can be sliced.  See below for more information.

#### List

`List` is a variable sized array with a `Count` and `Capacity` that
changes as items are added or removed.  Lists act more like arrays
than they do in C# because setters are automatically called to modify
fields when necessary:

    struct MyPoint { pub X int;  pub Y int}
    @a List<MyPoint> = [(1,2),(3,4),(5,6)]
    a[1].Y = 12    // Now `a` contains [(1,2),(3,12),(5,6)]

See below for information about initializer expressions and slice operator.

#### Map

`Map` is a hash table and is similar to `Dictionary` in C#.

    @a Map<str,int> = {"Hello":1, "World":2}
    @b = a["World"]                             // `b` is 2
    @c = a["not found"]                         // throws exception
    @d = a.Get("not found")                     // `d` is 0
    @e = a.Get("not found", -1)                 // `e` is -1

#### Json

`Json` is the built in Json object with support communication with JavaScript.  
Using an invalid index does not throw an exception, but instead returns a default
empty object.

    @a Json = {"Hello":1, "World":2}
    @b = a["World"]                         // `b` is Json(2), not an int
    @c = a["World"].Int                     // `c` is 2
    @d = a["World"].Str                     // `d` is "2"
    @e = a["not found"]["?"].int            // `e` is 0

The `Json` data structure is meant to be quick and easy to use, not necessarily
the fastest or most efficient. For efficient memory usage, `Json` will support
[Newtonsoft](https://www.newtonsoft.com/json) style serialization:

    @a = Json.Serialize(object)                 // `a` is a Json `str`
    @b = Json.Deserialize<MyObject>(jsonStr)    // `b` is a `MyObject`

## Operator Precedence

Operator precedence comes from Golang.

    Primary: x.y f(x) a[i] #type(expr)
    Unary: - ! & ~ * box
    Multiplication and bits: * / % << >> & 
    Add and bits: + - | ~
    Range: .. ::
    Comparison: == != < <= > >= === !== in
    Conditional: &&
    Conditional: ||
    Ternary: a ? b : c
    Lambda: ->
    Comma Separator: ,
    Assignment Statements: = += -= *= /= %= &= |= ~= <<= >>=
    Statement separator: => (not an operator, not lambda)

The `*` operator is both multiplication and unary dereference, same as in C.
When used in type definitions, it means *pointer to*, for example `@a *int`
means `a` is a pointer to `int`.

The `^` operator means *reference to* object when used in type definitions.
The `box` operator is used to make a reference to a struct or class.
**TBD:** Might change unary `box` operator to `^` in the future.

The `~` operator is both xor and unary complement, same as `^` in Golang.

The range operator`..` takes two `int`s and make a `Range` which is a
`struct Range { Start int; End int}`.  The `::` operator also makes a
range, but the second parameter is a count instead of end index.  
See `For Loop` below for examples.

Assignment is a statement, not an expression.  Therefore, expressions like
`while (a += count) < 20` and `a = b = 1` are not allowed.  Comma is also
not an expression, and may only be used where they are expected, such as
a function call or lambda expession.

Operator `==` does not default to object comparison, and only works when it
is defined for the given type.  Use `===` and `!==` for object comparison. 

#### Operator Overloading

`+`, `-`, `*`, `/`, `%`, and `in` are the only operators that may be individually
defined.  The `==` and `!=` operator may be defined together by implementing
`static fun Equals(a myType, b myType) bool`.  All six comparison operators,
`==`, `!=`, `<`, `<=`, `==`, `!=`, `>=`, and `>` can be implemented with just
one function: `static fun Compare(a myType, b myType) int`.  If both functions
are defined, `Equals` is used for equality comparisons, and `Compare` is used
for the others.

Overloads using the `operator` keyword are static.  Only static
versions of `Equals` and `Compare` are used for the comparison operators.
Zurfur inherits this from C#, and Eric Lippert
[gives a great explanation of why](https://blogs.msdn.microsoft.com/ericlippert/2007/05/14/why-are-overloaded-operators-always-static-in-c).

## Initializer Expressions

An initializer is a Json-like list or map enclosed within `{}` or `[]` and
may be used any place a function parameter takes either an `ICollection`,
`IRoMap`, or for `()` an object with a matching constructor:


    @a Array<int> = [1,2,3]                 // Array syntax
    @b Map<str,int> = {"A":1, "B":2}        // Map syntax
    @c Map<str,int> = [("A",1), ("B", 2)]   // Use ICollection and KeyValuePair constructor

    // Alternative way to initialize (not recommended, but equivalent to `=` format)
    @a = Array<int>([1,2,3])
    @b = Map<str,int>({"A":1, "B":2})
    @c = Map<str,int>([("A",1), ("B", 2)])

The first expression `a` uses array syntax to initialize the array.  The second
expression `b` uses map syntax to initialize the map.  The third expression, `c`
is accepted because the `Map` constructor takes an `ICollection<KeyValuePair<str,int>>`
parameter. The `KeyValuePair` constructor takes `str` and `int` parameters, so
everything matches up and is accepted.  

A `Map<str,MyPointXY>` can be initialized like this as long as `MyPointXY` has a
constructor taking two integers:

    @a Map<str, MyPointXY> = {"A": (1,2), "B": (3,4)}      // Use map initializer syntax

#### Json Initializer Expressions

The initializer syntax has support for Json.  The library will have a class to
support Json objects with syntax something like this:

    @a Json = {
        "Param1" : 23,
        "Param2" : [1,2,3],
        "Param3" : {"Hello":12, "World" : "Earth"},
        "Time" : "2019-12-07T14:13:46"
    }
    @b = a["Param1"].Int            // `b` is 23
    @c = a["param2"][1].Int         // `c` is 2
    @d = a["Param3"]["World"].Str   // `d` is "Earth"
    @e = a["Time"].DateTime         // `e` is converted to DateTime
    a["Param2"][1].Int = 5          // Set the value

## Statements

Like Golang, semicolons are required between statements but they are automatically
inserted at the end of lines based on the last non-comment token and the first token
of the next line.

The general rule is that any line beginning with a binary operator does not put
a semicolon on the previous line.  Additionally, `{`, `[`, `(`, or `,` at the end
of a line prevents a semicolon on that line.

**Excepton:** A line beginning with an `*` always has a semicolon before it, so
multiplication cannot be used to continue a line.  This is necessary so a
dereference statement such as `*a = 3` cannot be continued from the previous line.

#### For Loop

For the time being, `for` loops only allow one format: `for @newVariable in expression`. 
The new variable is read-only and its scope is limited to within the `for` loop block.
The simplest form of the for loop is when the expression evaluates to an integer:

    // Print the numbers 0 to 9
    for @i in 10
        { Console.WriteLine(i) }   // `i` is an integer

    // Increment all the numbers in an array
    for @i in array.Count
        { array[i] += 1 }

The range operators can be used as follows:

    // Print the numbers from 5 to 49
    for @i in 5..50
        { Console.WriteLine(i) }   // `i` is an integer

    // Print all the numbers in the array except the first and last
    for @i in 1..array.Count-1
        { Console.WriteLine(array[i]) }

    // Collect elements 5,6, and 7 into myArray
    for @i in 5::3
        { myList.Add(myArray[i]) }

Any object that supplies an enumerator (or has a `get` indexer and a `Count` property)
can be enumerated.  The `Map` enumerator supplies key value pairs:

    // Print key value pairs of all elements in a map
    for @kv in map
        { Console.WriteLine("Key: " + kv.Key.ToString() + " is " + kv.Value.ToString()) }

The expression after `in` is evaluated at the start of the loop and never
changes once calculated:

    // Print the numbers from 1 to 9
    @x = 10
    for @i in 1..x
    {
        x = x + 5               // This does not affect the loop bounds 
        Console.WriteLine(i)
    }

When iterating over a collection, just like in C#, it is illegal to add
or remove elements from the collection.  An exception is thrown if
it is attempted.  Here are two examples of things to avoid:

    for @i in myIntList
        { myIntList.Add(1) }   // Exception thrown on next iteration

    // This does not remove 0's and increment the remaining elements
    // The count is evaluated only at the beginning of the loop.
    for @i in myIntList.Count
    {
        if myIntList[i] == 0
            { RemoveAt(i) }        // There are at least two problems with this
        else
            { myIntList[i] += 1 } // This will throw an exception if any element was removed
    }
    

**TBD:** Explore syntax to iterate with different count steps.  Perhaps something
like `for @newVar in expression : stepExpression` where `stepExpression` is a
positive compile time constant.

#### While and Do Loops

Same as C#.

**TBD:** Explore syntax to allow post increment statement to make up
for the `for` loop not allowing it.  Perhaps something like
`while treePointer != null, treePointer = treePointer.Next {statements}`
From what I've seen, this is not necessary and would be abused too often.

#### Switch

The switch statement is mostly the same as C#, except that a `case` statement
has an implicit `break` before it.  `break` is not allowed at the same
level as a `case` statement.

    switch expression
    {
    case 0, 1, 2:
        DoStuff0()  // No fall through here.
    case 3:
        DoStuff1()
        break;      // SYNTAX ERROR: Break is illegal here
    case 4,5,6:
        DoStuff2()
        if x==y
            { break }  // Exit switch statement early, don't DoStuff3
        DoStuff3()
    }

## Class, Boxed Class, and Struct

**TBD:** For now, `^` is used in type names to mean *reference to*, and
the unary `box` operator is used in expressions to box the value.  Consider
using `^` for both since it could be confusing to have one thing for type
names and another for expressions.  Alternatively, `box` could be used for both.

There are three types of objects:

1. `class` - An *owned* object, allocated directly on the stack or inside another class
2. `class boxed` - A heap object, always allocated on the heap just like in C#
3. `struct` - A value object, always copied except when passed to functions

The goal is to minimize the number of objects created on the heap without
overly burdening the programmer.  To accomplish this, classes are *owned*
by the creator, which means they cannot be copied, nor can they be stored
in a collection or array unless they are explicitly boxed. 

#### Classes

A class is an owned object that cannot be copied unless it is returned from
a function that created it, assigned to a class field in a constructor, or
immediately boxed after being created.

The memory for a class can live directly on the stack or inside another
class without the need for dynamic memory allocation.  Stack allocated class
objects have deterministic destruction via `dispose` function.  When boxed,
they do not have deterministic destruction, but may be disposed manually.

This means that references to a class can never be captured or stored
in a collection.

    @myList = List<int>([1,2,3])        // My list is a stack local variabe
    MyFunc(myList)                      // MyFunc can use myList, but not get a reference to it
    myArrayOfLists[0] = myList          // ILLEGAL since myList is not boxed

    @myListRef = box List<int>([1,2,3]) // Create it in a box
    myArrayOfLists[0] = myListRef       // OK
    MyFunc(myListRef)                   // OK, reference is implicitly dereferenced
    // myList is deterministally destructed at end of scope
    // myListRef is not deterministacally destructed

This allows memory to be reclaimed and destructors to be called determinstically
when objects are declared locally on the stack or an async stack frame. Further more,
objects may be returned from functions:

    @myFileStream = File.Open("Hello.txt")
    @myList = CallAsyncFuncThatReturnsListOfInt(myFileStream)
    // myFileStream is closed at end of scope and memory is reclaimed
    // myList memory is also reclaimed unless it is returned from the function

Objects ownership can be changed in only two cases.  The first case, shown above,
is when an object is returned from a function.  The second case is when created
in the constructor and immediately assigned to a field of the class.

    class MyClass
    {
        myListField1    List<int>               // Must be assigned in constructor
        myListField2    List<int>([1,2,3])      // Created here
        myListField3    ^List<int>              // Not owned, reference must be initialized in constructor
        myListField4    ^List<int>([1,2,3])     // Created here
    }

    pub class Example
    {        
        F1 str                                      // Private string initialized to ""
        pub F2 Array<int>                           // Array of int, initialized to empty (i.e. Count = 0)
        pub F4 Array<int>([1,2,3])                  // Array initialized with 1,2,3
        pub F5 List<str>(["Hello", "World"])        // Initialized list
        pub F6 Map<str,int>({"A":1, "B":2})         // Initialized map
        pub ro F7 str("Hello world")                // Initialized read only string
        pub fun Func1(a int) f64 => F1 + " hi"    // Member function
        pub prop Prop1 str => F1                    // Property returning F1
        pub prop ChangedTime DateTime = DateTime.Now // Default value and default get/set
            => default get private set
    }

**TBD:** All fields and functions could be read only by default, requiring
`mut` if they can be mutated.  This would be more explicit, but maybe more
of a pain.  Possibly remove the need for `MutSpan`, `IMutArray`, and all
other mutable/immutable type differentiation.

The `prop` keyword is used to define a property.  Properties can be given
a default value by `=` immediately after the type name.  The compiler can
auto implement them with `=> default` followed by `get` and optionally `set`.
Or you can implement them the regular way `prop GetExpr { get { return expressio } }`

Extension methods are defined outside the class, always static, and may be
placed directly in the namespace of the class they are for.

    pub fun Example::MyExtension() => Prop1 + ":" + Func1(23)

Classes are sealed by default.  Use the `unsealed` keword to open them up.
Sealed generic classes may be inherited to create specializations
(e.g. `class MyIntList : List<int> { }`, etc), however the virtul functions
may not be overridden.

**TBD:** Require `@` for member variables?  This would make it easier to add
new qualifiers in the future.  It would also be more consistent overall,
both for the parser and the person looking at variable declarations.
A struct with fields would look like this: `struct MyPoint { pub @a int; pub @b int}`

**TBD:** Exploring removing "heap only" and have classes on the stack by default.
An embedded class would be owned by the outer class, and it would be impossible
to copy the inner class reference.  There would be much less garbage since many
classes would be stack allocated or embedded directly in an outer class.  It
would be more painful because the programmer would need to `box` anything going
on the heap.

#### Boxed Class

A `boxed class` is similar to a C# sealed class in that it is a heap
reference object and cannot ever be owned. A `str` is a boxed class.

Note that an `Array` is always created on the heap, but since it is not
a boxed struct, it has an owner and its memory can be deterministically
reclaimed when it goes out of scope. 


#### Struct

A `struct` is a value object (just like C#), and can be used where speed and
efficiency are desired.  `int`, `byte`, and `float` are structs. They are
immutable by default, but can be made mutable using the `mut` keyword.

    // Immutable point
    pub struct MyPoint
    {
        pub X int
        pub Y int
        pub new(x int, y int) { X = x; Y = y}
    }
    
    // Mutable point (each mutable field/fun must also be marked, but not properties)
    pub mut struct MyMutablePoint
    {
        pub mut X int
        pub mut Y int
        pub new(x int, y int) { X = x; Y = y}
        pub mut fun SetY(y int) { Y = y }
    }

A mutable struct returned from a getter can be mutated in-place provided there is a corresponding setter.

    @a = List<MyMutablePoint>({{1,2},{3,4}, {5,6}})
    a[1].X = 23         // `a` contains {{1,2},{23,4}, {5,6}}
    a[1].SetY(24)       // `a` contains {{1,2},{23,24}, {5,6}}

This works because `SetY` is a mutating function so the corresponding
`List` setter is called to save the result. 

Structs are passed to functions by value or by reference, whichever is more
efficient.  So, `@a = Multiply(b,c)` would pass `b` and `c` by value
if they are integers, or by reference if they are large matricies.

A struct may not contain a class, however it may contain a reference to a class.

    struct MyThing
    {
        pub myList List<int>()      // ILLEGAL - myList is owned and can't be copied
        pub myRefList ^List<int>()  // OK, myRefList is not owned
    }


#### Lambdas

Lambdas are also owned by default, but can be boxed as long as they don't
contain a reference including a reference to `this`. 

**TBD:** Work out how much bad this will be.  Passing local parameters
and `this` by reference is fine for owned lambdas restricted to function
call boundaries.  Lambdas that get stored in a collection must
not 

#### Enums

Enumerations are similar to C# enumerations, in that they are just
a wrapped `int`.  But they are implemented internally as a `struct`
and do not use `,` to separate values.

    pub enum MyEnum
    {
        A           // A is 0
        B; C        // B is 1, C is 2
        D = 32      // D is 32
        E           // E is 33
    
        // Enumerations can override ToString
        override fun ToString() => MyConvertToTranslatedName()
    }

The default `ToString` function shows the value as an integer rather
than the name of the field, but it is possible to override and make it
display anything you want.  This allows enumerations to be just as light
weight as an integer and need no metadata in the compiled executable.

**TBD:** Differentiate an enum having only scalar values vs one with flags?
The one with flags allows `|` and `&`, etc but the other doesn't.

## Casting

The cast as we know it from C and C# has a couple of problems.  First, the parser
doesn't know a type name is expected until after it has been parsed, meaning
the IDE can't show a list filtered by type name while you are typing.  Second,
the syntax for cast looks strange for simple types `@myInt = (int)(a+b*myFloat)`
when you'd much rather see it like a function call `@myInt = int(a+b*myFloat)`.

Zurfur uses `#` to cast from one type to another.  It looks like this `#type(expression)`:

    @a = (int)(a+myFloat)       // C# (not allowed in Zurfur)
    @a = #int(a+myFloat)        // Zurfur style

    ((List<Stuff>)yourStuff.SeeMyStuff).Add(Stuff())    // C# (not allowed in Zurfur)
    #List<Stuff>(yourStuff.SeeMyStuff).Add(Stuff())     // Zurfur style

A cast is used when a type conversion should be explicit, including
conversion from a base class to a derived class, conversion between
pointer types, and conversion of integer types that may lose precision.

A constructor can be used to convert types that don't lose precision,
like `byte` to `int`, but a cast must be used to convert `int` to `byte`
because precision can be lost.  In the definitions below, we want an
error if MyIntFunc() should ever return a float.

    // Field definitions
    a int(MyByteFunc())         // Ok, no loss of precision
    a int(MyIntFunc())          // Ok, but fails if MyIntFunc returns a float
    a int(MyFloatFunc())        // Fail, constructor won't risk losing precision
    a int(#int(MyFloatFunc()))  // Ok, explicit cast

## Interfaces

**TBD:** Change keyword to `trait`?

Interfaces are a cross between C#, GoLang, and Rust, but a little different
from each.  They are similar to C# 8.0 (including default implementations, etc.)
but also allow Golang style *explicit* conversion from any class that defines
all the required functions.

Here is `IEquatable<T>` from the standard library:

    pub static interface IEquatable<T>
    {
        static fun GetHashCode(a T) uint => imp
        static fun Equals(a T, b T) bool => imp
    }

Unimplemented functions and properties are explicitly marked with
`imp`.   Functions and properties must either have an implementation or
specify `imp`, `default`, or `extern`.  

NOTE: The implemntations will use fat pointers.

**TBD:** Describe syntax for creating externally defined traits, like
in Rust.  For example `implement TRAIT for TYPE`


#### Structural Typing

In C#, a class must explicitly support an interface.  This is good because
it forces the class designers to consider the supported interfaces when
making API changes.  Golang will convert any class to an interface as long
as the class implements all the matching functions.  This is convenient, but
there is no contract forcing the class designer to think about each supported interface. 
[Some people don't seem to like this too much.](https://bluxte.net/musings/2018/04/10/go-good-bad-ugly/#interfaces-are-structural-types)

Zurfur takes the middle ground.  Classes should list the interfaces they
support.  But, an *explicit* cast can be used to build any interface provided
the class implements all the functions.  The explicit cast is to remind us
that the class does not necessarily support the interface, and it's on the
user (not the library writer) to make sure it's all kosher.

#### Optional Conversion Back to the Concrete Class

**TBD:** The default will be that the interface cannot be cast back
to the original class.  The documentation below will be updated.

An interface can optionally be `protected`.  A protected interface
cannot be converted back to the original class or any other concrete
class, including `object`.  It can be implicitly converted to a base
interface or explicitly converted to any interface that implements a
subset of its methods.

This prevents people from using a cast to bypass the intended
use of the interface.  For example:

    pub class MyStuff
    {
        // Nobody should modify my stuff, but it's ok if they look at it
        myStuff List<Stuff>()
        pub ro SeeMyStuff IRoArray<Stuff> = #protected IRoArray<Stuff>(myStuff);
    }
    pub static fun MyFunc(yourStuff MyStuff)
    {
        // Modify your stuff.  ILLEGAL!
        #List<Stuff>(yourStuff.SeeMyStuf).Add(Stuff());
    }

#### Static Interfaces and Functions

Interfaces may include static functions.  Static functions are
a better fit than virtual functions for some operations.
For instance,  `IComparable` has only static functions.  This is
because, when you want to know if `a >= b`, it doesn't make sense
to ask `a` (via virtual function dispatch) to compare itself to `b`
which could be a different type.  What does it mean if they are
different types?  `a` wouldn't know what `b` is.  Note that `a`
and `b` can still be different types as long as the base class
implements `IComparable`, but the comparison function is on the
base class, not the derived classes.

`IArithmetic` is a static only interface, allowing this generic
function:

    // Return `value` if it `low`..`high` otherwise return `low` or `high`.  
    pub static fun BoundValue<T>(value T, low T, high T) T
            where T : IAritmetic
    {
        if value <= low
          => return low;
        if value >= high
          => return high;
        return value;
    }

#### Implementation Note

An interface is implemented using a fat pointer containing a reference to a VTable
and a reference to the object.  There is very little overhead calling an interface
method (less than calling a virtual function), but there is a little more overhead
casting an object to an interface.

See [Interface Dispatch](https://lukasatkinson.de/2018/interface-dispatch/)
and scroll down to *Fat Pointers*.  A comment by Russ Cox explains why
this is a good design choice "*The key insight for Go was that in a statically
typed language, type conversions happen far less often than method calls, so doing
the work on the type conversion is actually quite cheap.*"

Note that an interface containing only static functions can be implemented using
a thin pointer.


## Arrays and Slicing

Given a range, arrays can be sliced:

    @a = myArray[2..32]     // Elements starting at 2 ending at 31 (excludes element 32)
    @b = myArray[2::5]      // Elements 2..7 (length 5, excludes element 7)

If `myArray` is of type `Array<byte>`, a string can be created directly from the slice:

    @s = str(myArray[2::5])     // Create a string
    MyStrFunc(myArray[2::5])    // Convert slice to string, pass to function

#### List Slice

**TBD:** Allow slicing a `List`?  It is a little unsafe because the slice
becomes detached from the underlying array whenever the capacity changes.

    pub static fun BadSlice()
    {
        @s = List<byte>()
        a.Add("Hello Bob")
        @slice = a[6::3]        // slice is "Bob"
        a[6] = "R"[0]           // slice is "Rob"
        a.Add(", we are happy") // slice is now detached from the original array
        a[6] = "B"[0]           // slice is still "Rob"
    }

#### ASpan, Span, Forward References, Return References

Arrays have implicit conversion to `ASpan`, `Span`, and `RoSpan`.  `ASpan` is used for async
functions, and `Span` for sync functions, such as `str` class. A lot needs to be said here,
but the condensed version is:

* ASpan: An array segment that can be stored on the heap or passed to async functions
* Span: A memory slice that cannot be stored on the heap or passed to async functions
* Forward Reference: A pointer to an interior struct passed **into** a function
* Return Reference: A pointer to an interior struct passed **out of** a function,
implmeneted as a fat pointer (pointer to object and pointer to interior struct)


A distinction is made between forward references and return references because the GC needs
to be aware of (and pin) all references on the stack.  Once a reference is stored on the
memory stack, it is pinned and held in memory so that it can be passed forward on the
execution stack without ever being looked at again.  When a reference is returned, there
must be a way to pin the object that holds the reference.

Describe more here...

## Garbage Collection

The heap allocation model of C#, and inherited by Zurfur, is designed to make
garbage collection fast and efficient.  Interior references (i.e. references
that point inside an object) are never stored on the heap.

Whenever an interior reference is obtained, the object that holds it is
pinned on the memory stack (or async task stack) until the reference is
discarded.  From then on, it can be passed down the WebAssembly execution
stack without worrying about it.  

There is more info on GC implementation here [Internals](Doc/Internals.md)

## Pointers

The unary `*` operator dereferences a pointer.  The `.` operator is used to access fields
or members of a pointer to the struct (so `->` is only used for lambdas). 
 
    pub static fun strcpy(dest *byte, source *byte)
    {
        while *source != 0
            { *dest = *source;  dest += 1;  source += 1 }
        *dest = 0
    }

Pointers are never tracked by the GC.  Any object that may have a
pointer pointing into it must be pinned (or covered, see GC section below)

## Namespaces

Namespaces are similar to C#, but can also contain static functions,
and extension methods.  `using Zurur.Math` imports the intrinsic
math functions, `Cos`, `Sin`, etc. into the global symbol table.  If you
want to froce them to be prefixed with `Math.`, it can be done with
`using Math=Zurfur.Math`.

The first namespace defined in a file does not use curly braces to start a
new scope, nor should it start a new level of indentation.  All other
namespaces are sub-namespaces of the top level namespace and must
use curly braces.  Only one top level namespace per file is allowed.

## Async

Golang's concept of async is awesome.  Everything should be async by
default, but look as if it were sync. 

The problem with this approach is that WebAssembly doesn't support
the same kind of stack switching used by Golang. It would be difficult
to optimize function calls through a delegate that may or may not be
async.  One of the goals of Zurfur is that it be as fast and efficient
as C, so this is too high a price to pay.

For the time being, async is built into the type system but it looks and
acts as if it were sync.  Calling an async function from async code blocks
without using the `await` keyword:

    afun MySlowIoFunctionAsync(server str) str 
    {
        // In C# `await` would be needed before both function calls, but not in Zurfur
        @a = MakeXhrCallToServerAsync(server)  // Blocks without await keyword
        Task.Delay(100);                       // Also blocks without a keyword
        return a;
    }

[What color is your function?](https://journal.stuffwithstuff.com/2015/02/01/what-color-is-your-function/)
In Zurfur, functions do have a color but function call syntax is colorless.  
When running from async code, `F(X)` may or may not be async.

Async code normally looks and acts as if it were sync.  But, when we want
to start or wait for multiple tasks, we can also use the `astart` and
`await` keywords.

    afun GetStuffFromSeveralServers() str 
    {
        // Start the functions, but do not block
        @a = astart { MySlowIoFunctionAsync("server1") }
        @b = astart { MySlowIoFunctionAsync("server2") }
        @c = astart { MySlowIoFunctionAsync("server3") }

        // The timeout cancels the task after 10 seconds, but we'll hand
        // the task to the user who may push a button to cancel early
        // TBD: Timeouts and cancellation are still TBD
        @timeout = astart Task.Delay(10000); 
        GiveThisTaskToAUserWhoCancelTheOperationEarly(timeout)

        // Collect the results in the order they complete order
        @sum = new list<str>()
        await a, b, c, timeout
        {
            case a.HasResult: sum += a.Result
            case b.HasResult: sum += b.Result
            case c.HasResult: sum += c.Result
            case a.HasException: sum += "a failed"   // It threw an exception but swallow it and continue
            case b.HasException: sum += "b failed"   // Cancel remaining tasks and exit immediately
            case timeout.HasResult: break            // 10 seconds has passed, cancel automatically
            case timeout.HasException: break         // The user has canceled the operation early
            // TBD: break cancels all remaining tasks
            // TBD: If `c` throws, all remaining tasks are canceled.
        }
        // TBD: The only way to get out of an `await` is when all of the awaited
        // tasks have completed completed (possibly with an exception)

        // Not strictly necessary, but TBD good practice? 
        // TBD: Make sure Task functions can use `FinalizeNotify` to clean up
        timeout.Cancel()
    }

A sync function cannot implicitly call an async function, but it can start it
using the `astart` keyword, like this: `func MySyncFunction() { astart MyAsyncFunction() }`

#### Async Implementation 

Async will be implemented with an actual stack, not with heap objects. 
This should improve GC performance since each task call won't be
required to create a heap allocation.  Stacks themselves won't be
GC objects.  Instead there will be a reusable list of stack arrays.


#### Async by Default?

Should everything be async by defualt? The compiler can figure out if a
function needs to be async, and can optimize most sync code into sync
functions.  There are two problems here.

First, the compiler would have trouble optimizing lambda function calls.
If `List<T>.Sort(compare fun(a T, b T) bool)` is compiled
as async, it would be an efficiency disaster.

Second, it would be far to easy for a function to *accidentally* be changed
from sync to async.  Imagine the consequences of changing `malloc` or `new`
to async.  A library that was previously sync and fast could all of a sudden
become async and slow without even realizing it was happening.

One solution could be to mark functions `sync`, something like
`List<T>.Sort(compare sfun(a T, b T) bool)`.  This seems almost as bad
as marking them async.  Are there better solutions?

## Threading

The first version of Zurfur will not support multi-threading.  But since async
looks like just like sync (and everything in JavaScript is async) it will look
as if we are using plain old blocking calls.  Hopefully the async "blocking"
calls will be able to solve the [Async Pressure](https://lucumr.pocoo.org/2020/1/1/async-pressure/)
problem.

The `lock` keyword is reserved for future use.  One thing that won't ever be
supported is locking on any random object.  There will be a `Mutex` object 
for that purpose.


Real multi-threading can be achieved using Web Workers.  To support that
option, there will be a quick easy way to serialize object graphs and
transport them to other Web Assembly instances.  Probably with `Json`.

One benefit of doing it this way is that each module has its own memory
space, so the garbage collector will have a smaller working set
of objects to reclaim for each instance.

## Open Questions

Should NAN`s compare the way IEEE numbers are supposed to? Since this is a new
language, my preference would be to have them compare so NAN != NAN and they
sort so NAN > INF.  This way we don't have to worry about doing special
things for Map or sorted collections.  OTOH, it could be confusing when
porting code from other places.  OTOOH, I've had plenty of problems with
the IEEE comparison behavior as it is.




