# ![Logo](Zurfur.jpg) Zurfur

Zurfur is is a programming language I'm designing just for fun and enlightenment.
The language is named after our cat, Zurfur, who was named by my son.  It's
spelled **_ZurFUR_** because our cat has fur.

## Design Goals

* Fun and easy to use 
* Managed code is safe, efficient, and garbage collected
* Unmanaged code is just as efficient as C++
* Ahead of time compile to WebAssembly with tiny run-time library
* Stretch goal: Rewrite compiler and IDE in Zurfur on Node.js

![](Doc/IDE.png)

Zurfur is similar to C#, but borrows syntax and design concepts from
Golang and Javascript.  Here are some differences between Zurfur and C#:

* Strings are UTF8 byte arrays initialized
* Reference types are initialized without `new` keyword and are non-nullable by default
* `==` operator does not default to reference comparison
* Type declaration syntax and operator precedence is from Golang
* Built from the ground up using `ref` returns
* Get/set of structs acts like a class, `MyListOfStructPoints[1].X = 5` works like you think it should
* Interfaces can be explicitly created from object with matching signature (like Golang)
* Lots of other differences, but if you're familiar with C# it'll all make sense

## Overview

Thoughts about where to go and how to do it: [Internals](Doc/Internals.md).

#### Status Update

The syntax is still being developed, nothing is set in stone yet.  Feel
free to send me comments letting me know what you think should be changed.
I am currently working on [ZSIL](Doc/Zsil.md) header file.

## Functions

    /// This is a public documentation comment.  Use `variable` to
    /// refere to variables in the code.  Do not use XML.
    pub static func Main(args []str)
    {
        // This is a regular private comment
        Console.Log("Hello World, 2+2=" + add(2,2))
    }

    // Regular static function
    static func add(a int, b int) int
        => return a + b

    // Extension method for MyClass
    pub func MyClass::MyExtensionFunc(a str) str
        => a + ": " + memberVariable


Functions are declared with the `func` keyword. The type names come
after each argument, and the return type comes after the parameters.
Functions, classes, structs, enums, variables and constants are
private unless they have the 'pub' qualifier.  Functions are allowed
at the namespace level, but must be static or extension methods.

TBD: Still considering using `fun` to declare a function.

## Semicolons and Statements

Like Golang, semicolons are required between statements but they are automatically
inserted at the end of lines based on the last non-comment token and the first token
of the next line.

The general rule is that any line beginning with an operator does not put
a `;` on the previous line.  Additionally, `{`, `[`, `(`, or `,` at the end
of a line prevents a semicolon on that line.  

All statement blocks require either curly braces (e.g. `if expr { statements }`)
or can use the statement separator `=>` (e.g. `if expr => statement`) 
provided that the statement itself is not a compound statement.
    
    // Ok
    if a
      => DoThis()

    // Not OK, curly braces required after `if a`
    if a
      => if b
        => DoThis()

## Local Variables

Within a function, variables are declared and initialized with the `@` symbol,
which is similar to `var` in C#.

    @a = 3                              // `a` is an int
    @b = "Hello World"                  // `b` is a str
    @c = MyFunction()                   // `c` is whatever type is returned by MyFunction
    @d = List<int>({1,2,3})             // `d` is a list of integers, intialized with {1,2,3}
    @e = Map<str,int>({"A":1,"B":2})    // `e` is a map of <str, int>

Creating arrays and maps of common types can be abbreviated:

    @a = {1,2,3}                    // `a` is Array<int>
    @b = {"Hello", "World"}         // `b` is Array<str>
    @c = {1:4, 2:5, 3:6}            // `c` is Map<int,int>
    @d = {"Hello":1,"Wold":2}       // `d` is Map<str,int>
   
The above form `@variable = expression` creates a variable with the same type as
the expression.  A second form `@variable type [=expression]` creates an explicitly
typed variable with optional assignment from an expression.  If the expression
cannot be converted, an error is generated

    @a int = MyIntFunc()            // Error if MyIntFunc returns a float
    @b str                          // `b` is a string, initialized to ""
    @c List<int> = {1,2,3}          // `c` is a List<int>, initialized with {1,2,3}
    @d Map<str,int> = {"A":1,"B":2} // `d` is an initialzied Map<str,int>

References are non-nullable (i.e. may never be `null`) and are initialized
when created.  The optimizer may decide to delay initialization until the
variable is actually used which could have implications if the constructor
has side effects.  For instance:

    @myList List<int>           // Note: `@myList List<int>()` is the same
    if MyFunc()
      => myList = MyListFunc()  // Constructor not called above
    else
      => myList.Add(1)          // Constructor called here

It is possible to create a nullable reference.
    
    @myNullStr ?str         // String is null
    @myEmptyStr ?str()      // String is ""

A non-nullable reference can be assigned to a nullable, but a cast
will be needed to convert nullable to non-nullable.  **TBD:** Find
a shorter way to cast this case, maybe #?(expression)

## Basic types

    int8, uint8, byte, int16, uint16, int32, int, uint32, uint, int64, uint64
    float32, float64, xint, xuint, decimal, str, Array, List, Map

`byte`, `int`, and `uint` are aliases for `uint8`, `int32`, and `uint32`.
`xint` and `xuint` are extended integer types, which could be 32 or 64 bits
depending on run-time architecture.

#### Strings

Strings are immutable UTF8 byte arrays.  They can be translated by using the
`tr"string"` syntax.  Translated strings are grouped separately, and then
looked up dynamically at run time.  The `Arg()` function can be used
to replace occurrences of `%number`.  There is no fromatting beyond
string replacement.  For example: `tr"Hello %1. You like %2.".Arg(name).Arg(adjective)`
might generate `Hello Jeremy.  You like ice cream.`

**TBD:** This will be sufficient for first release, but later we want
syntax for interpolated and pluralized strings.  Perhaps `tr(number)"String"`

#### Array

`Array` is your standard C# fixed sized array.  The constructor takes `Count`
which can never be changed after that. 

    @a Array<int>               // `a` is an array of length zero
    @b Array<int>(32)           // `b` is an array of length 32
    @c Array<Array<int>>(10)    // `c` is an array of arrays

#### List

`List` is a variable sized array with a `Count` and `Capacity`
that changes as items are added or removed.  In Zurfur, lists act more like
arrays than they do in C# because setters are automatically used to modify
fields when necessary:

    struct MyPoint { pub X int;  pub Y int}
    @a = List<MyPoint>({{1,2},{3,4},{5,6}})
    a[1].Y = 12    // Now `a` contains {{1,2},{3,12},{5,6}}

#### Map

`Map` is a hash table and is similar to `Dictionary` in C#.

    @a = {"Hello":1, "World":2}     // Map<str, int>
    @b = a["World"]                 // `b` is 2
    @c = a["not found"]             // throws excepton
    @d = a.Get("not found", -1)     // `d` is -1

TBD: Lower case `array`, `list`, and `map`?  `span`, `roSpan`?

## Operator Precedence

Operator precedence comes from Golang.

    Primary: x.y f(x) a[i] #type(expr)
    Unary: + - ! & ^ ~
    Multiplication and bits: * / % << >> & 
    Add and bits: + - | ~
    Range: .. ::
    Comparison: == != < <= > >= === !== in
    Conditional: &&
    Conditional: ||
    Ternary: a ? b : c
    Lambda: ->
    Comma: ,
    Assignment Statements: = += -= *= /= %= &= |= ~= <<= >>=
    Statement separator: => (not an operator, not lambda)

The `*` operator is only for multiplication, and `->` is only for
lambda, neither are for dereferencing pointers.  `~` is both xor 
and unary complement.  See pointers section below for discussion.    

Operator `==` does not default to object comparison, and only works when it
is defined for the given type.  Use `===` and `!===` for object comparison. 

#### Operator Overloading

`+`, `-`, `*`, `/`, `%`, and `in` are the only operators that may be individually
overloaded.  The `==` and `!=` operator may be overloaded together by implementing
`static func Equals(a myType, b myType) bool`.  All six comparison operators,
`==`, `!=`, `<`, `<=`, `==`, `!=`, `>=`, and `>` can be implemented with just one function:
`static func Compare(a myType, b myType) int`.  If both functions are overloaded,
`Equals` is used for equality comparisons, and `Compare` is used for the others.

Overloads using the `operator` keyword are static.  Only static
versions of `Equals` and `Compare` are used for the comparison operators.
Zurfur inherits this from C#, and Eric Lippert
[gives a great explanation of why](https://blogs.msdn.microsoft.com/ericlippert/2007/05/14/why-are-overloaded-operators-always-static-in-c).

#### Initializers

An initializer is a list or map enclosed within `{}` and may be used any place
a function parameter takes either an `ICollection` or an object with a matching
constructor.  The `ICollection` is always chosen over a constructor if both exist. 

    @a = Map<str,int>({{"A",1}, {"B", 2}})  // Use ICollection and KeyValuePair constructor
    @b = Map<str,int>({"A":1, "B":2})       // Use Map syntax (note the `:`)
    @c = {"A":1, "B":2}                     // Use abbreviated form for common types

The first expression is accepted because the `Map` constructor takes an
`ICollection<KeyValuePair<str,int>>` parameter. The constructor `KeyValuePair`
takes `str` and `int` parameters, so everything matches up and is accepted.  We can
initialize a map of points like this, provided `MyPointXY` has a constructor that
takes two integers:

    @a = Map<str, MyPointXY>({{"A",{1,2}}, {"B", {3,4}}})

## Classes

A class is a heap only object, same as in C#.  Field definitions are `field`
followed by `type`, just like in Golang:

    pub class Example
    {        
        F1 str                                      // Private string initialized to ""
        pub F2 Array<int>                           // Array of int, initialized to empty (i.e. Count = 0)
        pub F4 Array<int>({1,2,3})                  // Array initialized with 1,2,3
        pub F5 List<str>({"Hello", "World"})        // Initialized list
        pub F6 Map<str,int>({{"A",1},{"B",2}})      // Initialized map
        pub ro F7 str("Hello world")                // Initialized read only string
        pub func Func1(a int) float => F1 + " hi"   // Member function
        pub prop Prop1 str => F1                    // Property returning F1
        pub prop ChangedTime DateTime = DateTime.Now // Default value and default get/set
            => default get private set
    }

The `ro` keyword makes a field read only.  A class can be marked `ro` which
means all fields must be makred `ro` and there can be no properties with
setters.  

The `prop` keyword is used to define a property.  Properties can be given
a default value by `=` immediately after the type name.  The compiler can
auto implement them with `=> default` followed by `get` and optionally `set`.
Or you can implement them the regular way `prop GetExpr { get { return expressio } }`

Extension methods are defined outside the class, always static, and may be
placed directly in the namespace of the class they are for.

    pub func Example::MyExtension() => Prop1 + ":" + Func1(23)

TBD: Require `@` for member variables?  This would make it easier to add
new qualifiers in the future.  It would also be more consistent overall,
both for the parser and the person looking at variable declarations.

    // TBD: Require `@` for member variables?
    pub @MyMemberVariable1 int
    pub new_qualifier @MyMemberVariable2 int

## Structs

A struct is usually a stack object or embedded in a class, and can be used where
speed and efficiency are desired:

    pub struct MyPointXY
    {
        pub X int
        pub Y int
        pub new(x int, y int) { X = x; Y = y}
        pub override func ToString() => "(" + X + "," + Y + ")"
    }

The `List<MyPointXY>` indexer returns a reference (identical to `[]MyPointXY`)
so `myMutPoints[index].X = x` will set X the same as if this were an array.
The indexer for `IList` uses a traditional `get` and `set` function,
however `myIListOfPoint[index].X = x` works as expected since the
compiler generates code to `get` the struct, modify it, then `set` it.

## Enums

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
        override func ToString() => MyConvertToTranslatedName()
    }

The default `ToString` function shows the value as an integer rather
than the name of the field, but it is possible to override and make it
display anything you want.  This allows enumerations to be just as light
weight as an integer and need no metadata in the compiled executable.

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

Interfaces are a cross between C# and GoLang, but a little different from
both.  They are mostly C# 8.0 (including default implementations, etc.)
but they also allow *explicit* conversion from any class that defines
all the required functions.

[I didn't realize default implementations
were so contentious.](https://jeremybytes.blogspot.com/2019/09/interfaces-in-c-8-are-bit-of-mess.html)
Let me know how to do it better.  Also, since they can have default
implementations, should we change the name to something different?  **trait**?
Here is `IEquatable<T>` from the stadard library:

    // TBD: Is `interface` the right keyword since there can be implementations?
    pub interface IEquatable<T>
    {
        static func GetHashCode(a T) uint 
            => youdo
        static func Equals(a T, b T) bool 
            => youdo
    }

Note that unimplemented functions and properties are explicitly marked with
`youdo`.   Functions and properties must either have an implementation or
specify `youdo`, `default`, or `extern`.  


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
    pub static func MyFunc(yourStuff MyStuff)
    {
        // Modify your stuff.  ILLEGAL!
        yourStuff.SeeMyStuff#(List<Stuff>).Add(Stuff());
    }

#### Static Functions

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
    pub static func BoundValue<T>(value T, low T, high T) T
            where T : IAritmetic
    {
        if value <= low
          => return low;
        if value >= high
          => return high;
        return value;
    }

## Arrays, Slicing, and the Range Operator

The `..` operator takes two `int`s and make a `Range` which is
a `struct Range { Start int; End int}`.  The `::` operator also
makes a range, but the second parameter is a count instead of end
index.

    for @a in 2..32
        => Console.Log(a)

Prints the numbers from 2 to 31.  The end of the range is exclusive,
so `for @a in 0..myArray.Count` iterates over the entire array.
An `Array` and `List` can be sliced using these two operators:

    @a = myArray[2..32]     // Elements starting at 2 ending at 31 (excludes element 32)
    @b = myArray[2::5]      // Elements 2..7 (length 5, excludes element 7)

Describe more here...

## Pointers

The `*` operator is only for multiplication, and there is no `->` operator.
The `.` operator is used for accessing fields or members of a pointer to
struct.  The `^` operator dereferences a pointer.  The `~` operator is
both xor and complement.
 
    pub static func strcpy(dest ^byte, source ^byte)
    {
        while ^source != 0
            { ^dest = ^source;  dest += 1;  source += 1 }
        ^dest = 0
    }

TBD: I'm still debating going back to `*` for pointers, but changing
the unary dereference operator to `*.` so it's not the same as multiplication.
In which case, strcpy would look like this:

    pub static func strcpy(dest *byte, source *byte)
    {
        while *.source != 0
            { *.dest = *.source;  dest += 1;  source += 1 }
        *.dest = 0
    }


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

    afunc MySlowIoFunctionAsync(server str) str 
    {
        // In C# `await` would be needed before both function calls, but not in Zurfur
        @a = MakeXhrCallToServerAsync(server)  // Blocks without await keyword
        Task.Delay(100);                       // Also blocks without a keyword
        return a;
    }

Async code normally looks and acts as if it were sync.  When we want
to start or wait for multiple tasks, use the `astart` and `await` keywords.

    afunc GetStuffFromSeveralServers() str 
    {
        // Start the functions, but do not block
        @a = astart MySlowIoFunctionAsync("server1")
        @b = astart MySlowIoFunctionAsync("server2")
        @c = astart MySlowIoFunctionAsync("server3")

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

**TBD:** As you can see, much is still TBD.

#### Async Implementation 

Async will be implemented with an actual stack, not with heap objects. 
This should improve GC performance since each task call won't be
required to create a heap allocation.  Stacks themselves won't be
GC objects.  Instead there will be a reusable list of stack arrays.
    
## Open Questions

Should structs be immutable by default?  No.  Immutable won't be
immutable when they are in an array or a property with a setter.
If `MyPoint` is immutable, then `MyArray[i].X=1` is legal because
the compiler can transorm to `MyArray[i] = new MyPoint(1,MyArray[i].Y)`

Do we want lower case to be private by default and upper case to
be public similar to Golang?  My personal preference is to have 
`pub` be explicit even though it is a bit tedious.

Should we switch to i32, i64, f32, f64, etc., like WebAssembly?

Should `Array`, `Map`, and `List` be lower case since they are almost as
basic as `str` and `decimal`?  If so, should `Span` and `RoSpan` be lower
case?  My preference is leaning toward them lower case but leaving `Span` and
`RoSpan` upper case.  What about `Sin`, `Cos`, etc.?

Should NAN`s compare the way IEEE numbers are supposed to? Since this is a new
language, my preference would be to have them compare so NAN != NAN and they
sort so NAN > INF.  This way we don't have to worry about doing special
things for Map or sorted collections.  OTOH, it could be confusing when
porting code from other places.  OTOOH, I've had plenty of problems with
the IEEE comparison behavior as it is.




