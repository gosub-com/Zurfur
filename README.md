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
* Type declaration syntax and operator precedence is from Golang
* Built from the ground up using `ref` returns, so `List` acts exactly like `Array`
* Interfaces connect to any object with matching signature (just like Golang)
* Lots of other differences, but if you're familiar with C# it'll all make sense

## Overview

Thoughts about where to go and how to do it: [Internals](Doc/Internals.md).

## Functions

    /// This is a public documentation comment
    pub static func main()
    {
        // This is a regular comment
	    Console.Log("Hello World, 2+2=" + add(2,2))
    }

    static func add(a int, b int) int
    {
	    return a + b
    }


Functions are declared with the `func` keyword. The type names come
after each argument, and the return type comes after the parameters.
Functions, classes, structs, enums, variables and constants are
private unless they have the 'pub' qualifier.

Functions are allowed at the namespace level, but must be static or
extension methods.

    /// This is an extension method
    pub func MyClass::MyExtensionFunc(a str) str
    {
        return a + ": " + memberVariable
    }

TBD: Still considering using `fn` to declare a function.

## Variables

Within a function, variables are declared and initialized with the `@` symbol,
which is the same as `var` in C#. 

    // Local variables declared in a function
    @myString = "Hello World"
    @myInt = 3
    @myList = List<int>({1,2,3});
    @myMap = Map<str,int>({{"A",1},{"B",2}})

TBD: Make them immutable by default, and add `mut@` or `@@` for mutable?  
I've never had a problem with mutable by default, so don't see the point
but am open to the argument.

## Basic types

    int8, uint8, byte, int16, uint16, int32, int, uint32, uint, int64, uint64
    float32, float64, xint, xuint, decimal, str, Array, List, Map

`byte`, `int`, and `uint` are aliases for `uint8`, `int32`, and `uint32`.
`str` is an immutable array of UTF8 encoded bytes.  `xint` and `xuint` are
extended integer types, which could be 32 or 64 bits depending on run-time
architecture.

`[]type` is shorthand for `Array<type>`, but cannot be initialized with
any value other than `null`.  `List<type>` works just like an array, but
has a capacity and dynamic size.  It's similar to C#'s `List`, except that
it indexes using a `ref` return.  It acts just like an array including the
ability to modify a field of a mutable struct or slice it to create a `Span`

TBD: Lower case `array`, `list`, and `map`?  `span`, `roSpan`?

## Classes

A class is a heap only object, same as in C#.  Field definitions are `field`
followed by `type`, just like in Golang:

    pub class Example
    {
	    F1 str                                   // Private string initialized to ""
	    pub F2 Array<int>                        // Array of int, initialized to null
        pub F3 Array<int>(23)                    // Array, length 23 integers (all 0)
        pub F4 Array<int>({1,2,3})               // Array initialized with 1,2,3
	    pub F5 List<str>({"Hello", "World"})     // Initialized list
	    pub F6 Map<str,int>({{"A",1},{"B",2}})   // Initialized map
        pub ro F7 str("Hello world")             // Initialized read only string
	    pub func Func1(a int) float => F1 + " hi"   // Member function
        pub prop Prop1 str => F1                 // Property returning F1
    }

The `ro` keyword makes a field read only.  The `prop` keyword is used to
define a property.  Extension methods are defined outside the class and may
be placed directly in the namespace:

    pub func Example::MyExtension() => Prop1 + ":" + Func1(23)

TBD: Require `@` for member variables?  This would make it easier to add
new qualifiers in the future.  It would also be more consistent overall,
both for the parser and the person looking at variable declarations.

    // TBD: Require `@` for member variables?
    pub @MyMemberVariable1 int
    pub new_qualifier @MyMemberVariable2 int

An exception would be made for const (since that's a keyword) and enumerations
since they are

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

## Initializers

An initializer is a parameter enclosed within `{}` and may be used any place
a function parameter takes either an `ICollection` or an object with a matching
constructor.  The `ICollection` is always chosen over a constructor if both exist. 

    @a = Map<str, int>({{"A",1}, {"B", 2}})

The `Map` constructor takes an `ICollection<KeyValuePair<str,int>>` parameter.
The constructor of the key value pair will take `str` and `int` parameters, so
everything matches up and is accepted.  If the `int` were replaced with `MyPointXY`,
an extra set of `{}` would be required:

    @a = Map<str, MyPointXY>({{"A",{1,2}}, {"B", {3,4}}})

## Operator Precedence

Operator precedence comes from Golang.

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
    Comma: ,
    Assignment Statements: = += -= *= /= %= &= |= ~= <<= >>= 

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

## Interfaces

Interfaces are a cross between C# and GoLang, but a little different from
both.  They are mostly C# 8.0 (including default implementations, etc.)
but they also allow *explicit* conversion from any class that defines
all the required functions.  Unlike C# and Golang, interfaces do not
allow conversion back to the original object.  

#### Structural Typing

In C#, a class must explicitly support an interface.  This is
good because it forces the class designers to consider
the supported interfaces when making API changes.  Golang
will convert any class to an interface as long as the class
implements all the matching functions.  This is convenient, but
there is no contract forcing the class designer to think about
each supported interface.

Zurfur takes the middle ground.  Classes should list the
interfaces they support.  But, an *explicit* cast can be used
to build any interface provided the class implements all the
functions.  The explicit cast is to remind us us that the class
does not necessarily support the interface.  **TBD:** Could
require the `cast` keyword to make it even more explicit
that structural typing is being used.

#### No Conversion Back to the Concrete Class

Once you have an interface, it's impossible to cast it back
to the original class.  It can be implicitly converted
to a base interface or explicitly converted to any interface
that implements a subset of the methods.

This prevents people from using a cast to bypass the intended
use of the interface.  For example:

    pub class MyStuff
    {
	    // Nobody should modify our stuff, except for us!
	    mystuff List<Stuff>()
	
	    // Don't mind if they look at our stuff
    	pub ro SeeMyStuff IRoArray<Stuff> = mystuff;
    }

    pub static func MyFunc(yourStuff MyStuff)
    {
	    // Gee, wouldn't it be nice to modify your stuff here?
	    // ILLEGAL!
	    ((List<Stuff>)yourStuff.SeeMyStuff).Add(Stuff());

        // Alternate experimental cast (still ILLEGAL)
	    yourStuff.SeeMyStuff.to(List<Stuff>).Add(Stuff());
    }

## Casting

In the code, cast looks like `(Type)identifier` or `(Type)(expression)`
and is used when a type conversion should be explicit, including
conversion from a base class to a derived class, conversion between
pointer types, and converting integer types that may lose precision.

**TBD:** Allow conversion from `float` to `int` via constructor 
(e.g `myInt = int(a+myFoat)`)?  This syntax looks  nicer than 
`myInt = (int)(a+myFloat)`.  The problem comes with a field
definition like this `a int(MyFunc())`.  Since the type name is
required, it's not clear that the conversion should be allowed.
If the return type of `MyFunc` changes from `int` to `float` there
would be an undetected loss of precision.  One solution is to
to allow `int(MyFloat)` in expressions (since it's clear we
want the conversion), but require the cast for field definitions
that lose precision:

    // Field definitions
    a int(MyByteFunc())         // Ok, no loss of precision
    a int(MyIntFunc())          // Ok
    a int(MyFloatFunc())        // Fail, not truly explicit since `int` is required
    a int((int)MyFloatFunc())   // Ok because of explicit cast

    // Expressions in code
    @a = int(MyFloatFunc())                 // Ok, `int` is explicit 
    MyFuncTakingInt(int(MyFloatFunc()))     // Ok, `int` is explicit


The cast construct is determined at the parse level.  Whenever a closing
parenthesis `)` is found, if the next symbol is an identifier or an open
parenthesis `(`, it's a cast.  Otherwise, it is not.  For example,
`(a)b`, `(a)(b)` are always casts regardless of what `a` or `b` is.
`(a+b)c` is an invalid cast.  `(a)^b` is not a cast.  If you
need to cast a dereferenced pointer, an extra parenthesis is required
as in `(a)(^b)`.

#### TBD: Experimental Cast

The parser currently accepts a postfix cast in the form of `Expression.(type)`, 
`Expression.to(type)`, and `Expression.cast(type)`.  So, 
`((MyInterfaceType)MyObject).InterfaceFunc()` can be written as 
`MyObject.(MyInterfaceType).InterfaceFunc` or `MyObject.to(MyInterfaceType).InterfaceFunc`.
And a conversion from `float` to `int` like this `MyFloat.(int)` or `MyFloat.to(int).  
This looks a little funky, especially for pointer conversions, but maybe it just takes
some getting used to.

    // See above for definition of MyStuff
    pub static func MyFunc(yourStuff MyStuff)
    {
        // Standard method
	    ((List<Stuff>)yourStuff.SeeMyStuff).Add(Stuff());

        // Experminetal methods (leaning towards using to)
	    yourStuff.SeeMyStuff.(List<Stuff>).Add(Stuff());
	    yourStuff.SeeMyStuff.to(List<Stuff>).Add(Stuff());
	    yourStuff.SeeMyStuff.cast(List<Stuff>).Add(Stuff());
    }

**TBD:** Tell me which you like better

## Arrays, Slicing, and the Range Operator

Describe here...


## Namespace/using

Similar to C# namespaces, but a little more like modules since
static functions, variables, constants, and extension methods may be declared
at the namespace level.  `using Zurur.Math` imports the intrinsic math functions,
`Cos`, `Sin`, etc. into the global symbol table.  If you want to froce them to be
prefixed with `Math.`, it can be done with `using Math=Zurfur.Math`.

TBD: Do we want to use the keyword `module` and `import` instead?  We'll
keep `namespace` for now since a module may contain different namespaces.

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
        @a = astart MySlowIoFunctionAsync("server1");
        @b = astart MySlowIoFunctionAsync("server2");
        @c = astart MySlowIoFunctionAsync("server3");

        // The timeout cancels the task after 10 seconds, but we'll hand
        // the task to the user who may push a button to cancel early
        // TBD: Timeouts and cancellation are still TBD
        @timeout = astart Task.Delay(10000); 
        GiveThisTaskToAUserWhoCancelTheOperationEarly(timeout)

        // Collect the results in the order they complete order
        @sum = new list<str>()
        await a, b, c, timeout
        {
            case a.HasResult: sum += a.Result;
            case b.HasResult: sum += b.Result;
            case c.HasResult: sum += c.Result;
            case a.HasException: sum += "a failed" // It threw an exception but swallow it and continue
            case b.HasException: sum += "b failed"; break;  // Cancel remaining tasks and exit immediately
            case timeout.HasResult: break;  // 10 seconds has passed, cancel automatically
            case timeout.HasException: break;  // The user has canceled the operation early
            // TBD: break cancels all remaining tasks
            // TBD: If `c` throws, all remaining tasks are canceled.
        }
        // TBD: The only way to get out of an `await` is when all of the awaited
        // tasks have completed completed (possibly with an exception)

        // Not strictly necessary, but TBD good practice? 
        // TBD: Make sure Task functions can use `FinalizeNotify` to clean up
        timeout.Cancel();  
    }

A sync function cannot implicitly call an async function, but it can start it
using the `astart` keyword, like this: `func MySyncFunction() { astart MyAsyncFunction() }`

**TBD:** As you can see, much is still TBD.

## Pointers

`^` is used to dereference pointers and `.` is used to access a field
or method of a struct through a pointer.  `->` is not needed because
`.` works fine.  `*` was changed to `^` since the former is also
a binary operator.  

Describe more here...

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




