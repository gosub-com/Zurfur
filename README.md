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
Golang.  Here are some differences between Zurfur and C#:

* Strings are UTF8 byte arrays initialized to "" by default
* Type declaration syntax and operator precedence is from Golang
* Built from the ground up using `ref` returns, so `List` acts exactly like `Array`
* Interfaces connect to any object with matching signature (just like Golang)
* Lots of other differences, but if you're familiar with C# it'll all make sense

## Overview

Thoughts about where to go and how to do it: [Internals](Doc/Internals.md).

#### Status Update

The syntax is still being developed, nothing is set in stone yet.  Feel
free to send me comments letting me know what you think should be changed.
I am currently working on [ZSIL](Doc/Zsil.md) header file.

## Functions

    /// This is a public documentation comment, which should not be marked
    /// up with XML.  Use '`' to refer to symbols in the source.  For example,
    /// call `Main` to run this example.  Parameters should have a ':' after them.
    /// `args`: A list of arguments.
    pub static func Main(args []str)
    {
        // This is a regular private comment
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

## Operator Precedence

Operator precedence comes from Golang.

    Primary: . () [] @ # ^ {}
    Unary: + - ! & ^
    Multiplication and bits: * / % << >> & 
    Add and bits: + - | ^
    Range: .. ::
    Comparison: == != < <= > >= === !== in
    Conditional: &&
    Conditional: ||
    Ternary: a ? b : c
    Lambda: =>
    Comma: ,
    Assignment Statements: = += -= *= /= %= &= |= ~= <<= >>= 

The `@` operator is the same as using `var` in front of a variable in C#.

The `*` operator is only for multiplication, and there is no `->` operator.
See pointers section below for discussion.

Like Golang, there is no unary `~` operator.  The unary version of `^`
can be used instead.

Operator `==` does not default to object comparison, and only works when it
is defined for the given type.  Use `===` for object comparison. 

#### Operator Overloading

`+`, `-`, `*`, `/`, `%`, and `in` are the only operators that may be individually
overloaded.  The `==` and `!=` operator may be overloaded together by implementing
`static func Equals(a myType, b myType) bool`.  All six operators, `==`, `!=`,
`<`, `<=`, `==`, `!=`, `>=`, and `>` by implementing just one function:
`static func Compare(a myType, b myType) int`.  If both functions are overloaded,
`Equals` is used for equality comparisons, and `Compare` is used for the others.

Overloads using the `operator` keyword are static.  Only static
versions of `Equals` and `Compare` are used for the comparison operators.
Zurfur inherits this from C#, and Eric Lippert
[gives a great explanation of why](https://blogs.msdn.microsoft.com/ericlippert/2007/05/14/why-are-overloaded-operators-always-static-in-c).

#### Initializers

An initializer is a parameter enclosed within `{}` and may be used any place
a function parameter takes either an `ICollection` or an object with a matching
constructor.  The `ICollection` is always chosen over a constructor if both exist. 

    @a = Map<str, int>({{"A",1}, {"B", 2}})

The `Map` constructor takes an `ICollection<KeyValuePair<str,int>>` parameter.
The constructor of the key value pair will take `str` and `int` parameters, so
everything matches up and is accepted.  If the `int` were replaced with `MyPointXY`,
an extra set of `{}` would be required:

    @a = Map<str, MyPointXY>({{"A",{1,2}}, {"B", {3,4}}})


## Casting

The cast as we know it from C and C# has several problems.  First, the parser
doesn't know a type name is expected until after it has been parsed, meaning
the IDE can't show a list filtered by type name while you are typing.  Second,
the syntax for cast looks strange for simple types `@myInt = (int)(a+b*myFloat)`
where you'd much rather type it like this `@myInt = int(a+b*myFloat)`.
Third, sometimes a postfix cast is easier to read.

Zurfur accepts two forms of cast syntax, a prefix and a postfix version.
The prefix version looks like this `#type(expression)`, and the postfix
version looks like this `primary#(type)`.  The `()` in both versions are
mandatory, same as in C#.  Two examples:

    @a = (int)(a+myFloat)       // C# (not allowed in Zurfur)
    @a = #int(a+myFloat)        // Prefix
    @a = (a+myFloat)#(int)      // Postfix

	((List<Stuff>)yourStuff.SeeMyStuff).Add(Stuff())    // C# (not allowed in Zurfur)
	yourStuff.SeeMyStuff#(List<Stuff>).Add(Stuff())     // Postfix
	#List<Stuff>(yourStuff.SeeMyStuff).Add(Stuff())     // Prefix

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
all the required functions.  Unlike C# and Golang, interfaces do not
allow casting back to the original object.  

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
functions.  The explicit cast is to remind us that the class
does not necessarily support the interface, and it's on the
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

    // Return value if it low..high otherwise return low or high.  
    pub static func BoundValue<T>(value T, low T, high T) T
		    where T : IAritmetic
    {
	    if value <= low
		    { return low; }
	    if value >= high
		    { return high; }
	    return value;
    }

## Arrays, Slicing, and the Range Operator

Describe here...

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




