# ![Logo](Zurfur.jpg) Zurfur

Zurfur is is a programming language I'm designing just for fun and enlightenment.
The language is named after our cat, Zurfur, who was named by my son.  It's
spelled **_ZurFUR_** because our cat has fur.

## Overview

I love C#.  It's my favorite language to program in.  But, I'd like to fix
some [warts](http://www.informit.com/articles/article.aspx?p=2425867) and have
some features from other languages built in from the ground up.

**Status Update**

I am working on header file generation.  Hit F4 to see what's generated.
The syntax is still being developed, nothing is set in stone yet.  Feel
free to send me comments letting me know what you think should be changed.

![](Doc/IDE.png)

#### Design Goals

* Fun and easy to use
* Faster than C# and unsafe code just as fast as C
* Target WebAssembly with ahead of time static compilation
* Typesafe replacement for JavaScript
* Stretch goal: Rewrite compiler and IDE in Zurfur on Node.js

Zurfur takes its main inspiration from C#, but borrows syntax and design
concepts from Golang, Rust, Zig, Lobster, and many other languages.
Here are some key features:

* Mutability and nullabilty are part of the type system:
    * Function parameters must be explicitly marked `mut` if they mutate anything
    * Children of read only fields (i.e. `ro` fields) are also read only
    * References are non-nullable by default and may use `?type` for nullable
    * Get/set of a mutable type acts like you expect (e.g. `myObject.VectorProperty.X = 3` works)
* Fast and efficient:
    * All non-array objects are value types (many fewer heap objets, great for functional programming)
    * Return references and span used everywhere. `[]int` is `Span<int>`, and OK to pass to async functions
    * Single threaded model (from Javascript) allows efficient reference counted heap objects
    * Safe multithreading via web workers (`clone` and `bag` defined as fast deep copy for message passing)
    * Functions pass parameters by reference, but will pass by value when it is more efficient
    * Async acts like a Golang blocking call without the `await` keyword (no garbage created for the task)
* More:
    * Interfaces support Rust style traits and Golang style duck typing via fat pointers
    * Type declaration syntax is from Golang
    * Strings are UTF8 byte arrays, always initialized to "" (all types initialized to non-null values)
    * Operator `==` fails if it is not defined on a type (does not default to object comparison)

#### Inspirations

* [Lobster](http://strlen.com/lobster/) - A really cool language that uses reference counting GC
* [Zig](https://ziglang.org/) - A better and safer C
* [Pinecone](https://github.com/wmww/Pinecone/blob/master/readme.md) - Inspiration to keep plugging away

## Local Variables

Within a function, variables are declared and initialized with the `@` 
operator (i.e. the `var` keyword from C#):

    @a = 3                          // a is an int
    @b = "Hello World"              // b is a str
    @c = MyFunction()               // c is whatever type is returned by MyFunction
    @d = [1,2,3]                    // d is List<int>, initialized with [1,2,3]
    @e = ["A":1.0, "B":2.0]         // e is Map<str,f64>
    @f = [[1.0,2.0],[3.0,4.0]]      // f is List<List<f64>>

The above form `@variable = expression` creates a variable with the same type as
the expression.  A second form `@variable type [=expression]` creates an explicitly
typed variable with optional assignment from an expression. 

    @a int = MyIntFunc()                // Error if MyIntFunc returns a float
    @b str                              // b is a string, initialized to ""
    @c List<int>                        // c is an empty List<int>
    @d List<f64> = [1, 2, 3]            // Create List<f64>, elements are converted
    @e Map<str,f32> = ["A":1, "B:1.2]   // Create Map<str,f32>
    @f Json = ["A":1,"B":[1,2,3.5]]     // Create a Json

An array of expressions `[e1, e2, e3...]` is used to initialize a list and
an array of pairs `[K1:V1, K2:V2, K3:V3...]` is used to initialize a map.
Brackets `[]` are used for both arrays and maps. Curly braces are reserved
for statement level constructs.  Constructors can be called with `()`.  
The following are identical, although, the simplest form is preferred:

    class MyPointXY(X int, Y int)
    @c Map<str, MyPointXY> = ["A": (1,2), "B": (3,4)]           // MyPointXY Constructor
    @d Map<str, MyPointXY> = ["A": (X:1,Y:2), "B": (X:3,Y:4)]   // MyPointXY field initializer
    @a = ["A": MyPointXY(1,2), "B": MyPointXY(3,4)]


## Functions

    /// This is a public documentation comment.  Do not use XML.
    /// Use `name` to refer to variables in the code. 
    pub static fun Main(args Array<str>)
    {
        // This is a regular private comment
        Log.Info("Hello World, 2+2=" add(2,2))
    }

Functions are declared with the `fun` keyword. The type name comes
after each argument, and the return type comes after the parameters.
Functions, types, enums, variables and constants are private unless
they have the 'pub' qualifier.  Functions are allowed at the namespace
level, but must be static or extension methods.

By default, functions pass parameters as read-only reference.  The exception
is that small structs (e.g. `Span<T>`) may be passed by value when it is more
efficient to do so.  Since all types are passed the same way, there is little
difference whether `MyType` is a `class` or `struct`:

    pub fun Test(a        f64,      // Pass by value since that's more efficient
                 s        MyType,   // Pass by value or reference whichever is more efficient
                 ros   ro MyType,   // Pass an immutable type
                 ms   mut MyType,   // Pass by reference, preserve `ro` fields
                 rs   ref MyType)   // Pass by reference, nothing is preserved

If `s` is big, such as a matrix containing 16 floats, it is passed by
reference.  If it is small, such as a single float or int, it is passed
by value.  A type containing two integers might be passed by value or
by reference depending on the compiler, options, and optimizations.
Even if `MyStruct` is mutable, `s` cannot be mutated because only parameters
marked with `mut` or `ref` can be mutated.  

Functions can return multiple values:

    // Multiple returns
    pub static fun Circle(a f64, r f64) -> (x f64, y f64)
    {
        return Cos(a)*r, Sin(a)*r
    }


**TBD:** Still thinking about using the Golang `func` keyword to define functions.

## Types

I am leaning towards making Zurfur a value oriented language, requiring `^`
for heap objects.  Every object is a value with a clear owner but is passed
to a function via reference.  The function may modify the object if it was
passed with `mut`, but cannot ever capture a reference to it.

### Owned Object Discussion

Zurfur wants to be just as easy as C#, but more efficient without the
complexity of Rust or unsafeness of C++.  So how much simpleness do we
trade for efficiency?

**Level 0: Class Is on the Heap**

When it comes to heap and references, C# is super simple.  A `class` is always
a heap object, and `struct` is always a value, embedded in its owner.
In many cases, the optimizer should be able to embed a class on the stack
and sometimes even within a class.  Consider this:

    static pub fun DoSomething(x mut SomeClass) { ... }
    pub class MyClass
    {
        a SomeClass
        b SomeClass
        c SomeClass     // Maybe this one needs to be cached somewhere
        pub fun DoStuff()
        {
            @q = SomeClass()
            DoSomething(q)
        }
    }

If the optimizer looks inside `DoSomething` and sees that `x` can never escape,
then `q` can be allocated on the stack.  Furthermore, if the optimizer can verify
`a` and `b` never escape, they too can be inlined inside `MyClass`.
A function that creates a local `MyClass` and calls `DoStuff` could go from
requiring 4 allocations down to 1 allocation.

However, these optimizations might be difficult or impossible, especially when
interfaces or lambdas are involved.  Even worse, a small change to one function
could cause all the optimizations dissapear.  Perhaps someone went to great
pains to make sure `a` and`b` were never captured.  The next person doesn't know
this, changes the program, and bam... efficiency disaster.

**Level 1: Specify When Functions Capture**

And also allow opt-in ownership. For this, we could define two new keywords:
`cap` for caputure, and `owned` for embedded (allocation free) objects.  The
compiler would enforce that an `owned` object cannot be used in a way that would
cause it to escape. An example:

    static pub fun Print(name str) { ... }  // `str` is already on the heap, so no `cap`
    static pub fun DoSomething(x mut SomeClass) { ... }
    static pub fun DoSomethingThatCaptures(cap x mut SomeClass) // Require `cap` keyword
    pub class MyClass
    {
        // NOTE: These must be `owned`, otherwise the program is too slow!
        owned a SomeClass
        owned b SomeClass
        c SomeClass        // This must be cached!
        pub fun DoStuff()
        {
            @q = SomeClass()
            DoSomething(q)
        }
    }

The price way pay for this is some extra complexity, especially when
considering libraries and generic parameters.  If we want to iterate
over `a`, the iterator may not capture the reference.  Converting a
function to one that captures is a breaking change.  Is it worth it?

What about strings and arrays?  Since they are always on the heap, the
`cap` keyword is not required.  There would be very little efficiency
gain.  And what benefit is there to write `pub fun DoSomething(cap x str)`
everywhere?  Strings and arrays would be `class boxed`, always on the heap.

Is it allowed for `DoStuff` to assign `a = q`?  If so, what happens to
`a`?  If SomeClass contains a large number of disposable objects, assignment
could become very expensive.  What about `q`?  Does it get moved?  Do
we require a `clone` if it is also returned from the function?

**Level 2: Make Owned Objects the Default**

First, let's use `^` instead of the `owned` and `cap` keywords.  Now we
have a type system that's much closer to garbage collected pointers: 

    static pub fun Print(name str) { ... }  // `str` is on the heap, so no `^`
    static pub fun DoSomething(x mut SomeClass) { ... }
    static pub fun DoSomethingThatCaptures(x ^mut SomeClass)
    pub class MyClass
    {
        // These must be `owned`, otherwise the program is too slow!
        a SomeClass
        b SomeClass
        c ^SomeClass       // This must be cached!
        d Map<str, SomeClass>   // TBD: Require ^ for generic types?
        pub fun DoStuff()
        {
            @q = SomeClass()
            DoSomething(q)
        }
    }

Do we require generic class parameters to be heap objects?  If not,
what does `d.Clear()` do with its owned objects?  If so, are we now
at C# levels of garbage?  I think not, especially when using reference
counting. Also, there are many opportunities for optimization and
smaller code by requiring generic class parameters to be on the heap.

Since efficiency is important, I am inclined to:
1. Require `^` for all heap objects (and we can extend this to `struct`)
2. Implicitly convert local objects from `type` to `^type` via move operation
3. Disallow owned objects to be assigned (no implicit clone or dispose for assignment )
4. Allow owned objects to be moved (`a` can be assigned in a constructor or returned from a function)
5. Require generic class parameters to be `^type`

Did we just inherit all the complexity of C++ and Rust with all the problems
of a garbage collected language?  Is it worth it?  For now, I believe these
rules don't make the language overly complex and they are worth it.

**Which Syntax to Use for Member Functions?**

    pub fun f(a MyClass)
    pub mut fun f(a mut MyClass)
    pub ^fun f(a ^MyClass)
    pub ^mut fun f(a ^mut MyClass)

    pub fun f(a MyClass)
    pub fun mut f(a mut MyClass)
    pub fun ^f(a ^MyClass)
    pub fun ^mut f(a ^mut MyClass)

    // This for now
    pub fun f(a MyClass)
    pub fun f mut(a mut MyClass)
    pub fun f^(a ^MyClass)
    pub fun f ^mut(a ^mut MyClass)

    pub fun f(a MyClass)                // Require explicit `this`?
    pub fun f(this mut, a mut MyClass)
    pub fun f(this ^, a ^MyClass)
    pub fun f(this ^mut, a ^mut MyClass)

### Mutability

Read only `ro` means children types are also read only.  A `ro ref`
return from a function prevents the calling function from mutating.

**Problem:** Many classes are mutable, but we want to use them
inside an immutable type without possibility of aliasing.  Furthermore,
we want them to be on the heap so cloning is cheap.  `str` is an
immutable type, but `Map` is not.  We want to create immutable maps
as data without possibility of change.

    // Require clone here since CalculateData could capture a ^Data?
    class Data(a int)
    static ro data Map<str, ^Data> = CalculateData()
   
    // Mutable data ends up inside immutable class
    class ro MyData(name str, stuff ^List<^Data>)
    @list List<^Data> = [1,2,3]
    @data = MyData("hello", list)
    // MyData is immutable, but can be changed via `list[0].a = 5`

Rust has this solved, I believe like this:

    ro @list List<^Data> = [1,2,3]
    @data = MyData("hello", list)

    // Clone required here depending on type returned by Calculate
    ro @list = Calculate().clone()      // Maybe clone required
    @data = MyData("hello", list)

`ro` becomes part of the type system as well as field modifier:

    pub Calculate1() ro ^List<^Data> { }    // Nothing captured/aliased
    pub Calculate2() ^List<^Data> { }       // ^List and ^Data may be captured/aliased
    pub Calculate3() ^List<ro ^Data> { }    // ^Data is immutable, ^List is not

`Calculate1` would be a reference copy, `Calculate2` would require clone.
For `Calculate3`, `ro` would not be allowed if `Data` is an immutable
type (e.g, `List<ro ^str>` doesn't need `^` because it's a boxed type and
also doesn't need `ro` because it's an immutable type)


**TBD:** Do we need a new type qualifier, `frozen`?  If we want to assign an
object to a `ro` field, it must be known not to have a mutable reference
aliased somewhere.  After, `clone`, we know this to be true.

**TBD:** Need to define how to do interior mutability.  Rust style `Cell`?
`unsafe mut`?  Is static mutable data allowed?  

### Simple Class and Struct

Simple types can declare thier fields in parentheses.  All fields are public
and no additional fields may be defined in the body.  The entire type must
be either mutable or `ro`, no mixing.

    // Simple struct and class
    pub struct SpecialPoint(X int, Y int)
    pub struct Line(p1 Point, p2 Point)
    pub struct WithInitialization(X int = 1, Y int = 2)
    pub class ro Person(Id int, FirstName str, LastName str, BirthYear int)

    // A simple struct may define properties, functions, and
    // constructors in the body but no additional fields or
    // field backed properties
    pub struct Point(X int, Y int)
    {
        fun new(p SpecialPoint)
            { todo() }
        pub fun SetY mut(y int)         // Mutable functions are marked `mut`
            { Y = y }
        pub prop PropX int
        {
            get: return X
            set: X = value
        }
        pub prop Illegal int get set    // ILLEGAL, no additional fields
    }

The default constructor can take all the fields in positional order, or any
of the fields as named parameters.  There is also a default `clone` function.

    @w = Point()                        // Default constructor
    @x = Point(1,2)                     // Constructor with all parameters
    @y = Point(X: 3, Y: 4)              // Initialized via named parameters
    @z = WithInitialization(X: 5)       // Constructor called first, so Y=2 here
    @p1 = Person(1, "John", "Doe", 32)
    @p2 = p1.clone(FirstName: "Jane")   // Clone is always a deep copy

A mutable type returned from a getter can be mutated in-place provided there
is a corresponding setter:

    @a List<Line> = [(1,2), (3,4), (5,6)]
    a[1].PropX = 23     // a = [(1,2),(23,4), (5,6)]
    a[1].SetY(24)       // a = [(1,2),(23,24), (5,6)]
    a[1].PropX = 0      // a = [(1,2),(0,24), (5,6)]

`List` returns the point via reference, but even if it used a getter/setter,
this would still work.  `SetY` is marked with `mut` so the compiler would
know to call the setter after the value is modified.  Same for `PropX`.

### Complex Class and Struct

If a type requires multiple constructors or private fields, it must declare
all fields in the body.  

    pub class Example
    {
        // Mutable fields
        text str // Private string initialized to ""
        pub MyNumbers List<int> = [1,2,3]
        pub MyStrings List<str> = ["Hello", "World"]
        pub MyMap Map<str,int> = ["A":1, "B":2]

        // Read-only fields and properties with backing fields
        pub ro HelloInit str = "Hello"                      // Constructor can override
        pub prop HelloNoInit str get private set = "Hello"  // Constructor cannot override
        pub prop MutableWithBackingField str get set
        pub ro Points List<MutablePointXY> = [(1,2),(3,4),(5,6)]
        
        // Functions and properties
        pub fun HelloText() str => "Hello: " text  // Member function
        pub mut fun SetText(a str) { text = a }    // Member function that mutates
        pub prop Prop1 str => text                 // Property returning text
        pub prop Text str
        {
        get => top
        set:
            if value == text
                { return }
            text = value
            SendTextChangedEvent()
        }
    }

Fields can use `ro` to indicate read only.  Unlike in C#, when `ro`
is used, the children are also read onyl (e.g. `Points[1].x = 0` is illegal)

The `prop` keyword is used to define a property.  When followd by `get`, it
has a backing field.  If there is no `set` or there is a `private set`, the
field is protected from changes via `clone` or default constructor. 

There is a default constructor taking all public fields and settable properties
as named parameters.  There is also a `clone` function. The default constructor
and `clone` function can override `ro` fields, but not properties unless they
have a public setter. **TBD:** Is that rule too easy to forget?

    @e1 = Example()
    @e2 = Example(Text: "Hello", MyMap: ["x":1, "y":2])
    @e3 = e1.clone(MyStrings: ["1", "2", "3"])

    @e4 = Example(HelloInit: "Hi")          // Overriding field is OK
    @e5 = Example(HelloNoInit: "Hi")        // Illegal, no public setter
    @e6 = e1.clone(HelloNoInit: "Hi")       // Illegal, same rule as constructor

Classes are sealed by default.  Use the `unsealed` keword to open them up.

**TBD:** Require `@` for field definitions?  Consider requiring `var` keyword instead.

### Anonymous Class and Struct

An anonymous class can be created like this: `@a = class(x f64, y f64)`
or `@a = class(x=1, y=MyFunc())`.  Fields are public, and do not need
explict type names when used as a local variable. 

    @a = class(x=1, y=2)
    Log.Info("X=" a.x ", Y=" a.y)   // Prints "X=1, Y=2"

### New, Init, Equality, Clone, Dispose, and Drop

The `new` function is the object constructor.  It does not have access to
`this` and may not call member functions except for another `new` function
(e.g. `new(a int)` may call `new()`).  `init` is called after the object is
created and it has access to `this` and may call other member functions.

`Equals`, `GetHashCode`, and `Clone` are generated automatically for types
that don't contain pointers or define a `dispose` function.  The `Equals`
function compares values, not object references (although object references
may be used to speed up the comparison).  Types that don't have an
`Equals` function may not be compared with `==` or `!=`.

`clone` without parameters is always a deep copy for mutable types, and a
shallow copy for immutable types.  Parameters can be used to create new
immutable objects with different values (e.g. `person.Clone(FirstName: "Jeremy")`).
Objects can be cloned to a buffer for transport to a Webworker
(e.g. `person.Clone(Buffer)`).  The buffer clone will be super fast,
laid out in memory so that it can be chopped up directly into DlMalloc
allocated objects.

A class may define `dispose`, which the user of the class may call or `use`
to dispose of the object (e.g. `@a = use File.Open(...)` calls `dispose`
at the end of the scope).  Calling `dispose` multiple times is not an error. 

A class may define `drop`, which is called by the garbage collector when the
object becomes unreachable.  Once unreachable, always unreachable, there
is no resurrection.  Therefore, the `drop` function does not have access to
`this` or any of its reference fields since they may have already been reclaimed.
It does have access to value types and pointer fields.  It should raise a debug
panic if the object is still  *open* when `drop` is called (in a release, an
error should be logged and resources cleaned up).

There are no guarantees as to when `drop` is called, it could be very quickly
if the compiler determines the object is dead at the end of the scope, or if
reference counting is used.  Drops may be queued and called later.  Or they
might be called a long time later in a fully garbage collected environmnet.
It is an error for the user of an object to allow it to be reclaimed while
in the *open* state.

### Lambda and Function Variables

**TBD:** The `@` is used to make it easy to recognize new local variables:

    // Find max value and sort the list using lambdas
    @a = 0
    myList.For(@item => { a.max = Math.Max(item, a.max) })
    myList.Sort(@(a,b) => a > b)
    Log.Info("Sorted list is " myList ", maximum value is: " a.max)

There is shortcut syntax for lambda-only functions that move
the code block outside the function:


    pub static fun UseLambda() bool
    {
        myList.For @a =>
        {
            if a < 1
                continue    // Continue in the lambda
            if a > 10
                break       // Break out of the lambda
            if a == 3
                return false // Return out of the function, not the lambda
        }
        return true
    }

**TBD:** Consider how to `break` out of the lambda.  Use a return type of `Breakable`?

### Enum

Enumerations are similar to C# enumerations, in that they are just
a wrapped `int`.  But they are implemented internally as a `struct`
and do not use `,` to separate values.

    pub enum MyEnum
    {
        A           // A is 0
        B; C        // B is 1, C is 2
        D = 32      // D is 32
        E           // E is 33
    
        // Enumerations can define ToStr
        fun ToStr() => MyConvertToTranslatedName()
    }

The default `ToStr` function shows the value as an integer rather
than the name of the field, but it is possible to override and make it
display anything you want.  This allows enumerations to be just as light
weight as an integer and need no metadata in the compiled executable.

**TBD:** Differentiate an enum having only scalar values vs one with flags?
The one with flags allows `|` and `&`, etc but the other doesn't.

## Basic types

    i8, u8, byte, i16, u16, i32, int, u32, uint, i64, u64, f32, f64, xint, xuint,
    decimal, object, str, Array<T>, List<T>, Map<K,V>, Span<T>, Json, SortedMap<K,V>

`byte`, `int`, and `uint` are aliases for `u8`, `i32`, and `u32`.
`xint` and `xuint` are extended integer types, which could be 32 or 64 bits
depending on run-time architecture.

**TBD:** Use lower case for `list`, `map`, `json`, `span`, and
other common types?  

#### Strings

Strings (i.e. `str`) are immutable byte arrays, generally assumed to hold
UTF8 encoded characters.  However, there is no rule enforcing the UTF8 encoding
so they may hold any binary data.

String literals in source code can start with either a quote `"` or a backtick
`` ` ``, which should only be used when the string literal contains a quote `"`.
They can be translated using `tr"string"` syntax.

They may contain escape constants when they are immediately followed
by `\` and a recognized constant.  For example `"Column 1"\tab"Column 2"`
contains a `\tab` character. Valid escape constants are `\lf` (i.e. `\n` in C),
`\cr` (i.e. `\r` in C), `\crlf`, `\tab`, and others.  Unicode numbers may be
encoded in decimal (e.g. `\127`) or in hexadecimal (e.g. `\x1F600` is the
unicode smiley face).  A `\` inside the quotes is not an escape character.
TBD: Do we want to stick with `\n` and `\r` out of tradition?  I find them
confusing, especially since they aren't marked that way on most ASCII charts.

Strings are interpolated when when followed by parenthesis `"Example:"(expression)`,
or an identifier beginning a primary expression `"Item #" i "=" X[i]`.  

![](Doc/Strings.png)

There is no `StringBuilder` class, use `List<byte>` instead:

    @sb = List<byte>()
    sb.Push("Count to 10: ")
    for @count in 1::10
        sb.Push(" " count)
    return str(sb)

Strings can be sliced.  See `List` (below)

#### Array

An array is the most primitive heap object.  The array count is immutable,
but elements are not.  They are declared with `Array(count)` syntax, not
with C# `[]type` syntax.  `[]type` translates to `Span<type>`.

**TBD:** Make all arrays immutable?  Strings are immutable, and that's very
useful.  If arrays are immutable, there would need to be a mutable version
called `Buffer`.

Arrays can be sliced.  See `List` (below)

#### List

`List` is the most primitive dynamically sized array type, used as the base
building block by all other data structures.  It is a variable sized array
with a `Count` and `Capacity` that changes as items are added or removed.

    @a = [1,2,3]                // a is List<int>
    @b = ["A", "B", "C"]        // b is List<str>
    @c = [[1,2,3],[4,5,6]]      // c is List<List<int>>

List acts like an array in that a field of a struct can be modified, like so:

    struct MyPoint(X int, Y int)
    @a List<MyPoint> = [(1,2),(3,4),(5,6)]  // Use array intializer with MyPoint constructor
    a[1].Y = 12                             // Now a contains [(1,2),(3,12),(5,6)]

The list class is embedded in its owning class (see *Owned Object Discussion*
above).  It uses `ref` returns with an underlying reference counted array.
They are fast and efficient.

#### Span

Span is a view into a string, array, or list.  They are `ref struct` and
may never be stored on the heap.  Unlike in C#, a span can be used to pass
data to an async function.  

Array syntax translates directly to Span (not to Array like C#).
The following definitions are identical:

    // The following definitions are identical:
    afun Write mut(data Span<byte>) int error => impl
    afun Write mut(data []byte) int error => impl

Span is as fast, simple, and efficient as it gets.  They are just a pointer
and count.  They are passed down the *execution stack* or stored on the async
task frame when necessary.  More on this in the GC section below.

Given a range, the index operator can be used to slice a List.  A change to
the list is a change to the span and a change to the span is a change to the list.

    @a = ["a","b","c","d","e"]  // a is List<str>
    @b = a[1..4]                // b is a span, with b[0] == "b" (b aliases ["b","c","d"])
    @c = a[2::2]                // c is a span, with c[0] == "c" (c aliases ["c","d"])
    c[0] = "hello"              // now a = ["a","b","hello","d","e"], and b=["b","hello","d"]

When the count or capacity of a list changes, all spans pointing into it
become detached.  A new buffer is cloned and used by the list, but the old
spans continue aliasing the old data. 

        @list = List<byte>()
        list.Push("Hello Pat")  // list is "Hello Pat"
        @slice = a[6::3]        // slice is "Pat"
        slice[0] = "M"[0]       // slice is "Mat", list is "Hello Mat"
        list.Push("!")          // DEBUG PANIC - slice is now detached, list is "Hello Mat!"
        slice[0] = "C"[0]       // slice is "Cat", list is still "Hello Mat!"

Mutating the size of a list (not the elements of it) while there is a span
pointing into it is a programming error; however it is not memory unsafe.
Therefore, when running in a debugger, it will stop and complain, but when
running in production it will log an error and continue.

#### Map

`Map` is a hash table and is similar to `Dictionary` in C#.

    @a = ["Hello":1, "World":2]     // a is Map<str,int>
    @b = a["World"]                 // b is 2
    @c = a["not found"]             // throws exception
    @d = a.Get("not found")         // d is 0
    @e = a.Get("not found", -1)     // e is -1

#### SortedMap

This will be similar to `Map`, but use a red black tree.

#### Json

`Json` is the built in Json object with support communication with JavaScript.  
Using an invalid key does not throw an exception, but instead returns a default
empty object.

    @a Json = ["Hello":1, "World":2]
    @b = a["World"]                         // b is Json(2), not an int
    @c = a["World"].Int                     // c is 2
    @d = a["World"].Str                     // d is "2"
    @e = a["not found"]["?"].int            // e is 0

The `Json` data structure is meant to be quick and easy to use, not necessarily
the fastest or most efficient. For efficient memory usage, `Json` will support
[Newtonsoft](https://www.newtonsoft.com/json) style serialization:

Another example:

    @a Json = [
        "Param1" : 23,
        "Param2" : [1,2,3],
        "Param3" : ["Hello":12, "World" : "Earth"],
        "Time" : "2019-12-07T14:13:46"
    ]
    @b = a["Param1"].Int            // b is 23
    @c = a["param2"][1].Int         // c is 2
    @d = a["Param3"]["World"].Str   // d is "Earth"
    @e = a["Time"].DateTime         // e is converted to DateTime
    a["Param2"][1].Int = 5          // Set the value


## Operator Precedence

Operator precedence is mostly from Golang, but more compatible
with C and gives an error where not compatible:

|Operators | Notes
| :--- | :---
|x.y  f<type>(x)  a[i] | Primary
|- ! & ~ * sizeof use unsafe cast| Unary
|@|Capture new variable
|as is | Type conversion and comparison
|<< >>| Bitwise shift (not associative, can't mix with arithmetic operators)
|* / % & | Multiply, bitwise *AND* (can't mix arithmetic and bit operators)
|~| Bitwise *XOR* (can't mix with arithmetic operators)
|+ - &#124; | Add, bitwise *OR* (can't mix arithmetic and bit operators)
|Low..High, Low::Count|Range (inclusive of low, exclusive of high)
|== != < <= > >= === !== in|Not associative
|&&|Conditional
|&#124;&#124;|Conditional
|a ? b : c| Not associative, no nesting (see below for restrictions)
|=>|Lambda
|key:Value|Pair
|,|Comma Separator (not an expression)
|= += -= *= /= %= &= |= ~= <<= >>=|Assignment Statements (not an expression)
|=>|Statement Separator

The `~` operator is both xor and unary complement, same as `^` in Golang.

The `@` operator captures the expression into a new variable.
For instance `while stream.Read(buffer)@length != 0 {...}`
captures the value returned by `Read` into the new variable `length`.
Or, `if maybeNullObjectFunc()@nonNullObject { }` can be used to
convert `?Object` into `Object` inside of the braces. 

The bitwise shift operators `<<` and `>>` are higher precedence than other
operators, making them compatible with C for bitwise operations.  Bitwise
and arithmetic operators may not be mixed, for example both `a + b | c` and
`a + b << c` are illegal.  Parentheses may be used (e.g. `(a+b)|c` is legal)

The range operator`..` takes two `int`s and make a `Range` which is a
`struct Range{ High int; Low int}`.  The `::` operator also makes a
range, but the second parameter is a count (`High = Low + Count`).  

Operator `==` does not default to object comparison, and only works when it
is defined for the given type.  Use `===` and `!==` for object comparison. 
Comparisons are not associative, so `a == b == c` is illegal.

The ternary operator is not associative and cannot be nested.  Examples
of illegal expresions are `c1 ? x : c2 ? y : z` (not associative),
`c1 ? x : (c2 ? y : z)` (no nesting).  The result expressions may not
directly contain an operator with lower precedence than range.
For example, `a==b ? x==3 : y==4` is  illegal.  parentheses can be
used to override that behavior, `a==b ? (x==3) : (y==4)` and
`a==b ? (@p=> p==3) : (@p=> p==4)` are acceptable.

The pair operator `:` makes a key/value pair which can be used
in an array to initialize a map.

Assignment is a statement, not an expression.  Therefore, expressions like
`a = b = 1` and `while (a = count) < 20` are not allowed. In the latter
case, use `while count@a < 20`.  Comma is also not an expression and may
only be used where they are expected, such as a function call or lambda.


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


## Statements

Like Golang, semicolons are required between statements but they are automatically
inserted at the end of lines based on the last non-comment token and the first token
of the next line.  See "Coding Style" (below) for more info about semicolons.

#### While and Do Statements

The `while` loop is the same as C#.  The `do` loop is also the same as C#
except that the condition executes inside the scope of the loop:

    do 
    {
        @accepted = SomeBooleanFunc()
        DoSomethingElse()
    } while accepted

#### Scope Statement

The `scope` statement creates a new scope:

    scope
    {
        @file = use File.Open("My File")
        DoStuff(file)
    }
    // File variable is out of scope here


The `scope` statement can be turned into a loop using the `continue` statement:

    scope
    {
        DoSomething()
        if WeWantToRepeat()
            continue
    }

Likewise, `break` can be used to exit early.

#### For Loop

For the time being, `for` loops only allow one format: `for @newVariable in expression`. 
The new variable is read-only and its scope is limited to within the `for` loop block.
The simplest form of the for loop is when the expression evaluates to an integer:

    // Print the numbers 0 to 9
    for @i in 10
        Console.WriteLine(i)   // `i` is an integer

    // Increment all the numbers in an list
    for @i in list.Count
        list[i] += 1

The range operators can be used as follows:

    // Print all the numbers in the list
    for @i in 0..list.Count
        Console.WriteLine(list[i])

    // Collect elements 5,6, and 7 into myList
    for @i in 5::3
        myList.Add(myArray[i])

Any object that supplies an enumerator (or has a `get` indexer and a `Count` property)
can be enumerated.  The `Map` enumerator supplies key value pairs:

    // Print key value pairs of all elements in a map
    for @kv in map
        Console.WriteLine("Key: " + kv.Key.ToString() + " is " + kv.Value.ToString())

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
        myIntList.Add(1)   // Exception thrown on next iteration

    // This does not remove 0's and increment the remaining elements
    // The count is evaluated only at the beginning of the loop.
    for @i in myIntList.Count
    {
        if myIntList[i] == 0
            RemoveAt(i)        // There are at least two problems with this
        else
            myIntList[i] += 1 // This will throw an exception if any element was removed
    }
 
#### Switch

The switch statement is mostly the same as C#, except that a `case` statement
has an implicit `break` before it.  `break` is not allowed at the same
level as a `case` statement.

    switch expression
    {
    case 0, 1, 2:
        DoStuff0()  // No fall through here.
    case 3..6:      // Same as 3,4,5
        DoStuff1()
        break;      // SYNTAX ERROR: Break is illegal here
    case 6,7,8:
        DoStuff2()
        if x==y
            break  // Exit switch statement early, don't DoStuff3
        DoStuff3()
    default:
    }

**TBD:** `default` required unless all cases covered.

#### Match

The `match` keyword is reserved, but the syntax is identical to a regular function call.

    @num = 3 + match(myConstant)[1:a, 2..5:b, 6:myFunc(), default: 0]

## Coding Style

Zurfur enforces a few style standards, but one style it does **not** enforce
where your curly brace goes.  Both end-of-line and beginning-of-next-line are
acceptable.  By convention, all code in the Zurfur code base uses curly brace
on beginning-of-next-line style.  The Zurfur IDE shrinks curly brace only lines
so they take the same space as the brace-at-end style as in a regular IDE.

Like Golang, semicolons are required between statements but they are automatically
inserted at the end of lines based on the last non-comment token and the first token
of the next line.

Here are the enforced style rules:

1. No tabs
2. No white space or visible semi-colons at the end of a line
3. Split lines require a `[`, `(`, or `,` at the end of the line
or a binary operator (except `*`) at the beginning of the next
line.  Also accepted, a few other places where a continuation might
be expected such as `implements`, `where`, or other keywords
4. A `{` cannot start a scope unless it is in an expected place such as after
`if`, `while`, `scope`, etc., or a lambda expression.
5. Compound statements without braces require the compound part to be on the
next line, non-empty, indented at least two spaces, can't contain another
compound statement, and can't have another statement on the same line
6. Modifiers must appear in the following order: `pub` (or `protected`, `private`),
`unsafe`, `static` (or `const`), `unsealed`, `abstract` (or `virtual`, `override`,
`new`), `ref`, `mut` (or `ro`)


## Errors and Exceptions

Errors are return values which can be caught, stored, or explicitly ignored.
Exceptions are programming errors that stop the debugger and complain loudly.
They cannot be caught, however, they are memory safe and do enough cleanup
so that a production system can log the error and continue running. (i.e.
they actually can be caught in special places, but never in synchronous code)

A function marked with `error` may either return a result or return an error.
If the result is used without error checking, either 1) the function must have
an error handler, or 2) the function must have an error return value, or 3) both.

Any scope can catch an error.  Given the following definitions:

    pub fun afun Open(name str, mode FileMode) File error => impl
    pub mut afun Read(data mut Span<byte>) int error  => impl
    pub mut afun Close() error => impl

We may decide to let errors percolate up:

<pre>
    pub afun ReadFileIntoString(name str) str <b>error</b>
    {
        @result = List<byte>()
        @buffer = List<byte>(256, byte(0)) // Fill with 256 0 bytes
        @stream = <b>use</b> File.<b>Open</b>(name, FileMode.ReadOnly)
        while stream.<b>Read</b>(buffer)@count != 0
            result.Push(buffer[0::count])
        return str(result)
    }
</pre>
    
There are 3 functions that can generate an error, `use`, `Open`, and `Read`.
They are highlighted by the IDE to let you know each one could immediately
generate an error and exit the function.  If the function were not marked
`error`, the code would fail to compile.  But we could handle those errors
in the function instead percolating them up.

<pre>
    // This is not an example of something that should be done
    pub afun ReadFileOrErrorIntoString(name str) str // not marked with `error`
    {
        @result = List<byte>();
        @buffer = List<byte>(256, byte(0))
        @stream = <b>use</b> File.<b>Open</b>(name, FileMode.ReadOnly)
        while stream.<b>Read</b>(buffer)@count != 0
            result.Push(buffer[0::count])
        return str(result);
    error e FileNotFound:
        return "ERROR: Can`t even open the file, " e.Message
    error e:
        return "ERROR: Can't read the file, " e.Message ", here is part of it: " str(result)
    }
</pre>

A scope can have only one error handler at the end of it, but it may have
multiple error cases.  Any un-tested error jumps directly to it.  It has
access to only the variables declared before the first un-caught error.
In this case it has access to `result` and `buffer`, but not `stream`.

An error handler at the end of a function requires the use of `return` above
it, even if the function is void.  Each case of the error handler must terminate
with either `return` to suppress the error or with `raise` to percolate the
error up.  Only when the final error case catches all errors (i.e. `error e:`)
and also `return`'s, can the the error be fully suppressed and the
function not marked with `error`.

Error handlers nested inside a scope must use `return`, `break`, or `continue`

**TBD:** Figure out how to test for an error instead of catching it.
Maybe `if try(File.Open(...)@stream) { use stream... }`  The above is still a WIP
[Midori](http://joeduffyblog.com/2016/02/07/the-error-model/)


## Interfaces

Interfaces are a cross between C#, GoLang, and Rust, but a little different
from each.  They are similar to C# 8.0 (including default implementations, etc.)
but also allow Golang style *explicit* conversion from any type that defines
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

NOTE: The implementation will use fat pointers.

**TBD:** Describe syntax for creating externally defined traits, like
in Rust.  For example `implement TRAIT for TYPE`


#### Structural Typing

In C#, a type must explicitly support an interface.  This is good because
it forces the type designers to consider the supported interfaces when
making API changes.  Golang will convert any type to an interface as long
as the type implements all the matching functions.  This is convenient, but
there is no contract forcing the type designer to think about each supported interface. 
[Some people don't seem to like this too much.](https://bluxte.net/musings/2018/04/10/go-good-bad-ugly/#interfaces-are-structural-types)

Zurfur takes the middle ground.  Types should list the interfaces they
support.  But, an *explicit* cast can be used to build any interface provided
the type implements all the functions.  The explicit cast is to remind us
that the type does not necessarily support the interface, and it's on the
user (not the library writer) to make sure it's all kosher.

#### Conversion Back to the Concrete Type

A base type can be cast to a derived type, but it is impossible* to cast
an interface back to its concrete type.  This prevents people from "fishing"
around to get at the concrete type.  Can you believe people actually do that?
Please don't look at my code :)

* Impossible in safe code that is.  Unsafe code can get at the underlying pointer.

#### Static Interfaces and Functions

Interfaces may include static functions.  Static functions are
a better fit than virtual functions for some operations.
For instance,  `IComparable` has only static functions.  This is
because, when you want to know if `a >= b`, it doesn't make sense
to ask `a` (via virtual function dispatch) to compare itself to `b`
which could be a different type.  What does it mean if they are
different types?  `a` wouldn't know what `b` is.  Note that `a`
and `b` can still be different types as long as the base type
implements `IComparable`, but the comparison function is on the
base type, not the derived type.

`IArithmetic` is a static only interface, allowing this generic
function:

    // Return `value` if it `low`..`high` otherwise return `low` or `high`.  
    pub static fun BoundValue<T>(value T, low T, high T) T
            where T is IAritmetic
    {
        if value <= low
            return low
        if value >= high
            return high
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


## Garbage Collection

**TBD:** Explain the difference between forward references and
return references, and how they will be "covered" when passed
down the execution stack.

Thanks to [Lobster](https://aardappel.github.io/lobster/memory_management.html)
and the single threaded nature of JavaScript, I have decided that it is
OK to use reference counting.  Since everything is single threaded, no locks
are needed and it's very fast.  A real-time embedded system could be written
in Zurfur, and all that needs to be done is verify that no object graph
cycles are created during program execution.

Even if we need a tracing collector because cycles are created, it can skip
all data structures that can't cycle.  For instance, a 100Mb `Map<str,str>`
doesn't need to be traced since it can't have a cycle.

Calling an async function doesn't create garbage because each task has its
own stack.  No dynamic allocations are needed for async calls.

## Threading

The first version of Zurfur is targeted at replacing JavaScript, and will
support multi-threading only via web workers and message passing.  Each web
worker has its own address space, so everything appears to be single threaded.

#### Discussion

It's hard to do multi-threading with speed and memory safety.
Once we drop multi-threading, we can make things fast and
memory safe:

* Interfaces can be implemented with fat pointers that won't tear
* Spans can be stored on the heap, also without tearing
* Garbage collection can use reference counting without an interlock

JavaScript has done pretty well with the single threaded model.
IO is async and doesn't block.  Long CPU bound tasks can be
offloaded to a web worker.  Even Windows uses a single threaded
model for user interface objects.

TBD: Mutable static data is not allowed.  This allows us to safely
add threads in the future since there won't be any way for a function
to have access to mutable data used by another thread.


## Pointers

The unary `*` operator dereferences a pointer.  The `.` operator is used to access fields
or members of a pointer to the type (so `->` is not used for pointers). 
 
    pub static fun strcpy(dest *byte, source *byte)
    {
        while *source != 0
            { *dest = *source;  dest += 1;  source += 1 }
        *dest = 0
    }

#### Pointer Safety

For now...

Pointers are not safe.  They act axactly as they do in C.  You can corrupt
memory and crash your application.  They can be null, and the compiler
does not add run time null checks.  The data they point to is always
mutable.  There are three reasons for this.

First, it makes porting from C easier.  I need to port DlMalloc without
worrying about pointer type safety.  Second, they are fast.  It is
necessary to have low level libraries and infrustructure running
fast and efficiently.  Third, without pointers, Zurfur is a type
safe language.  Pointers should only be used where necessary.

Perhaps, after Zurfur is running, I might add a few things from
[Zig](https://ziglang.org/).  Among them is null safety (e.g. `?*int`),
explicit array types (i.e. `*[]int`), and mutability attribute.
That would be a major breaking change, which might be acceptable if
done before version 1.0.  But, speed cannot be sacrificed.

Pointers are never tracked by the GC.  Any object that may have a
pointer pointing into it must be pinned.

In an unsafe context, pointers can be cast from one type to another
using the `cast` operator.  The format is `cast(type)expression`.
For those coming directly from C, it will look almost the same
as an old C style cast, except the keyword `cast` must be used.

    @b = cast(*int)myFloatPtr   // Cast myFloatPtr to *int

## Namespaces

Namespaces are similar to C#, but can also contain static functions,
and extension methods.  `use Zurfur.Math` imports the intrinsic
math functions, `Cos`, `Sin`, etc. into the global symbol table.  If you
want to froce them to be prefixed with `Math.`, it can be done with
`use Math=Zurfur.Math`.

Namespaces do not nest or require curly braces.  The namespace must be
declared at the top of each file, after `use` statements, and before
function or type definitions. All other namespaces in a file must be
sub-namespaces of the top one:

    namespace MyCompany.MyProject               // Top level namespace
    namespace MyCompany.MyProject.Utils         // Sub-namespace, ok in same file
    namespace MyCompany.MyProject.OtherUtils    // Another sub-namespace, also ok
    namespace MyCompany.MyOtherProject          // ILLEGAL when in the same file

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
        // In C# `await` would be needed before both function calls
        @a = MakeXhrCallToServerAsync(server)    // Blocks without await keyword
        Task.Delay(100);                            // Also blocks without a keyword
        return a;
    }

Notice that async functions are defined with the `afun` keyword.

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
using the `astart` keyword, like this: `fun MySyncFunction() { astart MyAsyncFunction() }`

#### Async Implementation 

Async will be implemented with an actual stack, not with heap objects. 
This should improve GC performance since each task call won't be
required to create a heap allocation.  Stacks themselves won't be
GC objects.  Instead there will be a reusable list of stack arrays.


#### Async by Default?

Should everything be async by default? The compiler can figure out if a
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

## Open Questions

Should NAN`s compare the way IEEE numbers are supposed to? Since this is a new
language, my preference would be to have them compare so NAN != NAN and they
sort so NAN > INF.  This way we don't have to worry about doing special
things for Map or sorted collections.  OTOH, it could be confusing when
porting code from other places.  OTOOH, I've had plenty of problems with
the IEEE comparison behavior as it is.




