# ![Logo](Zurfur.jpg) Zurfur

Zurfur is is a programming language I'm designing for fun and enlightenment.
The language is named after our cat, Zurfur, who was named by my son.  It's
spelled **_ZurFUR_** because our cat has fur.

I love C#.  It's my favorite language to program in.  But, I'd like to fix
some [warts](http://www.informit.com/articles/article.aspx?p=2425867) and have
some features from other languages built in from the ground up.  I'm thinking
about mutability, nullability, ownership, and functional programming.

**Status Update**

Header file generation is working, and I am starting code generation.
Hit F4 to see the header file in JSON format.  The syntax is still being
developed, nothing is set in stone.  Feel free to send me comments letting
me know what you think should be changed.

![](Doc/IDE.png)

#### Design Goals


Zurfur takes its main inspiration from C#, but borrows syntax and design
concepts from Golang, Rust, Zig, Lobster, and many other languages.
Here are some key features:

* **Prime directives:**
    * Fun and easy to use
    * Faster than C# and unsafe code just as fast as C
    * Target WebAssembly with ahead of time static compilation
    * Typesafe replacement for JavaScript
    * Stretch goal: Rewrite compiler and IDE in Zurfur on Node.js
* **Mutability, ownership, and nullabilty are part of the type system:**
    * Function parameters must be explicitly marked `mut` if they mutate anything
    * All objects are value types, except for pointers (e.g. `^MyType`)
    * References are non-nullable, but may use `?MyType` or `?^MyType` for nullable
    * `ro` means read only *all the way down* (not like C#, where `readonly` only protects the top level)
    * Get/set of mutable properties works (e.g. `myList[0].VectorProperty.X = 3` mutates `X`)
    * Deterministic destructors (e.g. `FileStream` closes itself automatically)
* **Fast and efficient:**
    * Return references and span used everywhere. `[]int` is `Span<int>`, and OK to pass to async functions
    * Functions pass parameters by reference, but will pass a copy when it is more efficient
    * Explicit `clone` required when copying an object that requires dynamic allocation
    * Most objects are deleted without needing GC.  Heap objects are reference counted.
    * Safe multi-threading via web workers (`deepClone` and `bag` defined as fast deep copy for message passing)
    * Async acts like a Golang blocking call without the `await` keyword (no garbage created for the task)

#### Inspirations

* [Lobster](http://strlen.com/lobster/) - A really cool language that uses reference counting GC
* [Zig](https://ziglang.org/) - A better and safer C
* [Pinecone](https://github.com/wmww/Pinecone/blob/master/readme.md) - Inspiration to keep plugging away

## Variables

Variables are declared and initialized with the `@` operator
(i.e. the `var` keyword from C#):

    @a = 3                          // a is an int
    @b = "Hello World"              // b is a str
    @c = myFunction()               // c is whatever type is returned by myFunction
    @d = [1,2,3]                    // d is List<int>, initialized with [1,2,3]
    @e = ["A":1.0, "B":2.0]         // e is Map<str,f64>
    @f = [[1.0,2.0],[3.0,4.0]]      // f is List<List<f64>>

The above form `@variable = expression` creates a variable with the same type as
the expression.  A second form `@variable type [=expression]` creates an explicitly
typed variable with optional assignment from an expression. 

    @a int = myIntFunc()   // Error if myIntFunc is f32, ok if int has constructor to convert
    @b str                              // b is a string, initialized to ""
    @c List<int>                        // c is an empty List<int>
    @d List<f64> = [1, 2, 3]            // Create List<f64>, elements are converted
    @e Map<str,f32> = ["A":1, "B:1.2]   // Create Map<str,f32>
    @f Json = ["A":1,"B":[1,2,3.5]]     // Create a Json

This form is required for field definitions.

A list of expressions `[e1, e2, e3...]` is used to initialize a `List`
and a list of pairs `[K1:V1, K2:V2, K3:V3...]` is used to initialize a `Map`.
Brackets `[]` are used for both lists and maps. Curly braces are reserved
for statement level constructs.  Constructors can be called with `()`.
For `type MyPointXy(x int, y int)`, the following are identical:

    @c Map<str, MyPointXy> = ["A": (1,2), "B": (3,4)]           // MyPointXy Constructor
    @d Map<str, MyPointXy> = ["A": (x:1,y:2), "B": (x:3,y:4)]   // MyPointXy field initializer
    @a = ["A": MyPointXy(1,2), "B": MyPointXy(3,4)]

## Functions and Properties

Functions are declared with the `fun` keyword. The type name comes after the
argument, and the return type comes after the parameters:

    // This comment is public documentation.
    // Use `name` to refer to variables in the code. 
    fun main(args Array<str>)
        Log.info("Hello World, 2+2=${2+2}")

Methods and extension methods are declared with the same syntax and
use the `my` keyword to refer to fields or other methods in the type:

    // Declare an extension method for strings
    fun str.rest() str
        return if(my.count == 0, "" : my.subRange(1))  // `subRange` is defined by `List`

    // TBD: Still considering golang method syntax
    fun (str) rest() str
        return if(my.count == 0, "" : my.subRange(1)) 

Properties are functions declared with `get` and `set` keywords:

    fun get MyType.myString() str
        return my.cachedStr

    fun set MyType.myString(v str)
        my.cachedString = v
        my.stringChangedEvent()

    // TBD: Still considering golang syntax
    fun (MyType) get myString() str
        return my.cachedStr

This is identical to declaring a public field:

    // The return value is known to be a borrow from an internal field
    fun MyPointXy.x2() ref int
        return ref my.x
  
By default, function parameters are passed as read-only reference.  The
exception is that small types (e.g. `int`, and `Span<T>`) are passed by
copy because it is more efficient to do so.  Other qualifiers, `mut`, `ref`,
and `own` can be used to change the passing behavior:

    fun test(
        a               int,  // Pass a copy because it is efficient (i.e. `type copy`)
        b       mut ref int,  // Pass by ref, allow assignment in function
        c         List<int>,  // Pass by ref, read-only
        d     mut List<int>,  // Pass by ref, allow mutation, but not assignment
        e mut ref List<int>,  // Pass by ref, allow mutation and assignment
        f     own List<int>)  // Take ownership of the list

Parameter qualifiers:

| Qualifier | Passing style | Notes
| :--- | :--- | :---
|  | Read-only reference | Copy small `type copy` types (e.g. `int`, `Span`, etc.)
| `mut` | Allow mutation but not assignment | Not valid for `ro` types (e.g. `str`, `ro List`, etc.)
| `mut ref` | Allow mutation and assignment | Requires annotation (i.e. `ref`) at the call site
| `own` | Take ownership | Not valid for `ro` types or non-allocating types

Arguments passed by `mut` do not need to be annotated at the call site.  This
is because it is obvious that `f(myList)` could mutate `myList` and it's easy
enough to see if `f` does that just by hovering over the definition. 

Arguments passed by `mut ref` must be annotated at the call site
(e.g. `f(ref myInt)`).  This is because it is not quickly obvious that the
entire object could be replaced.  Furthermore, it would not be obvious that
`f(myInt)` or `f(myString)` would change the value.  

If the type is mutable *and* requires dynamic allocation, the function can
take ownership of the object by using the `own` keyword.  The caller must
then never use the object again, or must explicitly `clone` the object.

    fun storeList(list own List<int>)
        // Take ownership of the list

Functions can return multiple values:

    // Multiple returns
    fun circle(a f64, r f64) -> (x f64, y f64)
        return cos(a)*r, sin(a)*r

The return parameters are named, and can be used by the calling function:

    @location = circle(a, r)
    Log.info("X: ${location.x}, Y: ${location.y}")

Normally the return value becomes owned by the caller, but this behavior
can be changed with the `ref` keyword:

    fun getRoList() ref List<int>           // Read-only ref of internal data structure
        return ref myListField
    fun getMutList() mut List<int>          // Mutable (mutation allowed, assignment not allowed)
        return mut myListField
    fun getMutRefList() mut ref List<int>   // Mutable ref (mutation or assignment is allowed)
        return mut ref myListField

Return qualifiers:

| Qualifier | Passing style | Notes
| :--- | :--- | :---
|  | Caller takes ownership | A move or copy operation is performed
| `mut` | Caller may mutate, but not assign | Callee retains ownership.  Not valid for `ro` types
| `ref mut` | Caller may mutate or assign | Requires annotation (i.e. `ref`) at the call site

**TBD:** `ref` can cross async boundaries.  Or `ref` cannot, but `aref` can.
Probably this is better to be a compiler optimization, but mutating a `List`
count while holding a `ref` to element will cause a runtime error, so it
would be good to have compile time checks on that.

## Types

The ones we all know and love:

    nil, bool, i8, byte, i16, u16, i32, u32, int, u64, f32, f64
    
| Type | Description
| :--- | :---
| List\<T\> | Dynamically sized mutable list with mutable elements.  This is the one and only dynamically sized object in Zurfur.
| Array<\T\>| An alias for `ro List<T>`.  An immutable list of immutable elements. Even if the array contains mutable elements, they become immutable when copied into the list.  Array's can be copied very quickly, just by copying a reference.
| str, str16 | An `Array<byte>` or `Array<u16>` with support for UTF-8 and UTF-16.  `Array` (an ailias for`ro List`) is immutable, therefore `str` is also immutable.  `str16` is a Javascript or C# style unicode string
| Span\<T\> | Span into a `List` or `Array`.  It has a constant `count`.  Mutability of elements depends on usage (e.g Span from `Array` is immutable, Span from `List` is mutable)
| Map<K,V> | Unordered mutable map.  `ro Map<K,V` is the immutable counterpart. 
| Dynamic, DynamicMap | The type used to interface with dynamically typed languages.  Easy conversion to/from JSON and string representations of built-in types.  TBD: `dyn` keyword in the future (e.g. `myDyn["hello"]` is same as `myDyn.hello`)

All types have a compiler generated `ro` counterpart which can be copied
very quickly since cloning them is just a memory copy without dynamic
allocation.  In the case of `ro List`, `Array`, `str`, it's just one pointer.

This is still TBD:

| Qualifier | Notes
| :---    | :--- 
|  (none) | Normal types (`List`, `Map`, etc.) pass by reference and require explicit clone
| `ro`    | Read only types (`str`, `ro List`, `Array`, etc.) pass by reference and copy implicitly (copies are always fast and never allocate)
| `passcopy` | Small types (`int`, `Span`, possibly `Point`, 'Rect`, etc.) pass by copy since that is faster
| `copy`  | Types that never allocate can be implicitly copied but pass by reference since that is faster. 
| `ref`   | Stack only types containing a reference
| `boxed` | **TBD:** Heap only type, but is still owned
| `heap`  | **TBD:** Heap only type that can get a pointer to itself
| `async` | A type that has an async `drop`, can only be created in an async context or on the heap
| `noclone` | Cannot be cloned

**TBD:** `pass` and `passcopy` could be decided by the compiler based on the
size of the type, but different behavior could result because of references,
especially when combined with async. 

**TBD:** Are copy/clone semantics worth the trouble?  Why not clone by default?
A new programmer might ask why some types allow `a=b` and others require
`a=b.clone()` and then point out that `a=f(b)` might be hiding a clone.
For now, mutable dynamically allocated data requires an explicit clone because
we care about efficiency.  We can remove this later by allowing an implicit
clone.  `List` and `Map` are actually plain old data structures even though
they do dynamic allocation. More **TBD**: `myList.pushAll` hides a clone, or
require `myList.pushClones`?

**Pro explicit clone:** Prevent accidental cloning of large data structurs
with a simple assignment.  Encourage programmers to get a reference, rather
than re-index (e.g. `@a = ref myList[0].myType` rather than `@a = myList[0].myType`
which would be obvious if the compiler forces `@a = myList[0].myType.clone()`).

**Con explicit clone:** Different types have different semantics, some require
`clone` while others don't.  Largely an optimization problem.  Clones can
easily be hidden in a function call. 

### Privacy

At the module level, all fields (global static data), functions, and types 
are private to that module and it's children unless the `[pub]` qualifier is
specified.  Methods are declared at the module level, therefore they are
private by default.

Inside of a type, all fields are public by default.  Any field declared as
private is visible only within that file.  

### Simple Data Types

Simple data-only types can declare their fields in parentheses.  They are
mutable by default, but can also be immutable by adding the `ro` qualifier.
All fields are public.

    // Simple types - all fields are public
    type Point(x int, y int)
    type Line(p1 Point, p2 Point)
    type WithInitialization(x int = 1, y int = 2)
    type ro Person(Id int, firstName str, lastName str, birthYear int)

The default constructor can take all the fields in positional order, or any
of the fields as named parameters. 

    @a = Point()                        // Default constructor (0,0)
    @b = Point(1,2)                     // Constructor with all parameters
    @c = Point(x: 3, y: 4)              // Initialized via named parameters
    @d = Line((1,2),(3,4))              // Same as Line(Point(1,2),Point(3,4))
    @z = WithInitialization(x: 5)       // `y` is 2
    @p1 = Person(1, "John", "Doe", 32)  // Read-only data


### Complex Types

A complex type defines all of its fields in the body. Fields are declared
with `@` and are private, but may have public properties with `pub get`,
`pub get set`, etc.

    type Example
        // Mutable fields
        @text1 str = "hello"                // Private
        @text2 str pub get = "hello"        // Public get (copy, not a reference)
        @text3 str pub get set = "hello"    // Public get/set (copy, not a reference)
        @list1 List<int>                    // Private
        @list2 List<int> pub ref;           // Public read-only reference
        @list3 List<int> pub mut;           // Public mutable reference, not assignable
        @list4 List<int> pub mut ref;       // Public mutable reference, assignable

        // Read-only fields
        @roText1 ro str                     // Constructor can override
        @roText2 ro str = "Hello"           // Constructor cannot override
        @roText3 ro str pub init = "Hello"  // Constructor or client can override
        
    // Getter and setter functions (passing copies)
    fun get Example.text() str
        return my.text1

    fun set Example.text(value str)
        if value == my.text1
            return
        my.text1 = value
        my.sendTextChangedEvent()

    // Getter returning references
    fun get Example.list1a() ref List<int>      // Immutable reference
        return ref my.list1
    fun get Example.list1b() mut List<int>      // Mutable reference, not assignable
        return ref my.list1
    fun get Example.list1c() mut ref List<int>  // Mutable reference, assignable
        return ref my.list1


### Immutability

`ro` means read-only, not just at the top level, but at all levels.  When
a field is `ro`, there is no way to modify or mutate any part of it.

TBD: Document more

### References

References are short lived pointers that may never leave the stack.
They can be created explicitly, or implicitly when a function is called.


### List and ro List

`List` is the default data structure for working with mutable data.  Once the
list has been created, it can be converted to a `ro List` which is immutable.
Assigning a `ro List` is very fast since it is just copying a reference,
whereas assigning a `List` will create a copy unless it can be optimized
to a move operation.

    @x = [1,2,3]            // x is List<int>
    @y = ["A", "B", "C"]    // y is List<str>
    @z = [[1,2,3],[4,5,6]]  // z is List<List<int>>
    x.Push(4)               // x contains [1,2,3,4]
    x.Push([5,6])           // x contains [1,2,3,4,5,6]
    @a = x.toRo()           // a is `ro List<int>`
    fieldx = x              // fieldx is a copy of x (with optimization, x may have been moved)
    fielda = a              // fielda is always a reference to the ro list `a`

`List` can be used to quickly build and manipulate mutable data, while `ro List`
can be used to store immutable data.

### Strings

Strings (i.e. `str`) are immutable byte lists (i.e. `ro List<byte>`), generally
assumed to hold UTF8 encoded characters.  However, there is no rule enforcing
the UTF8 encoding so they may hold any binary data.

String literals start with a quote (single line) or backtick (multi-line), and
can be translated at runtime using `tr"string"` syntax.  They are interpolated
with curly braces (e.g `"${expression}"`). Control characters may be put inside
an interpolation (e.g. `"${\t}"` is a tab).  Inside the quoted string, the
backslash `\` is not treated differently than any other character.

![](Doc/Strings.png)

**TBD:** Remove quotes, since it's redundant?  Or are we so used to quotes, we
keep them?  Coding standard requires quotes unless the string is truly multi-line?

There is no `StringBuilder` type, use `List<byte>` instead:

    @sb = List<byte>()
    sb.push("Count from 1 to 10: ")
    for @count in 1..+10
        sb.push(` ${count}`)
    return sb.toStr()

### Span

Span is a view into a `str`, `List`, or `ro List`.  They are `type ref` and
may never be stored on the heap.  Unlike in C#, a span can be used to pass
data to an async function.  

The array declaration syntax `[]Type` translates directly to Span (not to
`Array` like C#).  The following definitions are identical:

    // The following definitions are identical:
    afun mut write(data Span<byte>) int throws
    afun mut write(data []byte) int throws

Spans are as fast, simple, and efficient as it gets.  They are just a pointer
and count.  They are passed down the *execution stack* or stored on the async
task frame when necessary.  More on this in the GC section below.

Given a range, the index operator can be used to slice a List.  A change to
the list is a change to the span and a change to the span is a change to the list.

    @a = ["a","b","c","d","e"]  // a is List<str>
    @b = a[1..4]                // b is a span, with b[0] == "b" (b aliases ["b","c","d"])
    @c = a[2..+2]               // c is a span, with c[0] == "c" (c aliases ["c","d"])
    c[0] = "hello"              // now a = ["a","b","hello","d","e"], and b=["b","hello","d"]

Mutating the `count` or `capacity` of a `List` (not the elements of it) while
there is a `Span` or reference  pointing into it is a programming error, and
fails the same as indexing outside of array bounds.

    @list = List<byte>()
    list.push("Hello Pat")  // list is "Hello Pat"
    @slice = a[6..+3]       // slice is "Pat"
    slice[0] = "M"[0]       // slice is "Mat", list is "Hello Mat"
    list.Push("!")          // Runtime failure with stack trace in log file


### Map

`Map` is a hash table and is similar to `Dictionary` in C#.  The type can be
inferred from the expression, or it can be explicit:

    @a = ["Hello":1, "World":2]     // a is Map<str,int>
    @b Map<int,f64> = [0:1, 1:2.3]  // b is Map<int,f64>

When indexing a map, the return type is `?ref T`, so it must either be checked
before being used or have a default:

    // Check for its existence
    if a["hello"]@item
        // Use item here
    else
        // item is invalid, insert new pobject

    // Get the value, or get a default if it doesn't exist
    @x = a["hello"]??3      // Get the value, or the default if it doesn't exist


If the map contains objects that can't be trivially copied (e.g. `List<int>`),
they must be cloned or reference captured:

    // TBD: explicit `ref` might not be needed here since
    //      @ capture is a new operator, different than assignment
    if ref myLists["hello"]@item
        // Use item here.  You don't own it and may not modify it

    if clone myLists["hello"]@item
        // Use item here.  You own they copy and may modify it

When assigning, the item is always created.  If it is also used at the same
time, it gets the default value:

    a["hello"] = 23         // Created if it doesn't exist
    a["new"] += 1           // Created with 0, or uses existing value

    
### Enum

Enumerations are similar to C# enumerations, in that they are just
a wrapped `int`.  But they are implemented internally as a `type`
and do not use `,` to separate values.

    enum MyEnum
        A           // A is 0
        B; C        // B is 1, C is 2
        D = 32      // D is 32
        E           // E is 33
    
        // Enumerations can define ToStr
        fun ToStr() str
            return MyConvertToTranslatedName()

The default `ToStr` function shows the value as an integer rather
than the name of the field, but it is possible to override and make it
display anything you want.  This allows enumerations to be just as light
weight as an integer and need no metadata in the compiled executable.

**TBD:** Differentiate an enum having only scalar values vs one with flags?
The one with flags allows `|` and `&`, etc but the other doesn't.

## Interfaces

Zurfur uses Golang style interfaces, which fit nicely with the dynamic nature
of Javascript.

## Async

Async is built into the type system but it looks and acts as if it were sync.
Calling an async function from async code blocks without using the `await` keyword:

    // The keyword `afun` denotes an async function
    afun MySlowIoFunctionAsync(server str) str
        @a = fetchHttp(server)      // Blocks without `await` keyword
        Task.delay(100);            // Also blocks without `await` keyword
        return a;

Async code normally looks and acts as if it were sync.  But, when we want
to start or wait for multiple tasks, we can also use the `astart` keyword.

    afun GetStuffFromSeveralServers() str
        @a = astart fetchHttp(request1)
        @b = astart fetchHttp(request1)
        @c = astart fetchHttp(request1)
        await(a,b,c)
        // We are guaranteed that a, b, and c have completed succesfully.
        // The first failure will return an error and cancel the other tasks.

The result of an `afun` function is actually a `Future` that can be used in a
similar way to `Task` in C#.

#### Async Implementation 

Async will be implemented with an actual stack, not with heap objects. 
This should improve GC performance since each task call won't be
required to create a heap allocation.  

## Errors and Exceptions

Joe Duffy has some great thoughts on error handling here [Midori](http://joeduffyblog.com/2016/02/07/the-error-model/).

But does the distinction between throwing and non-throwing make sense for
async? Almost all async functions can throw, so that will be the default:

    fun myFun1() int            // Sync, can not throw but can panic
    fun myFun2() int throws     // Sync, can throw, can panic
    afun myFun3() int           // Async, can throw, can panic
    afun myFun4() int nothrow   // Async, must handle errors or panic

The purpose of `nothrow` is to force top level functions to handle errors.
For instance, the click event for a GUI might require a `nothrow` function.

|Error Type | Action | Examples
| :--- | :--- | :---
|Normal | No stack trace, debugger not stopped | `throw` - File not found, task cancelled
|Panic | Stack trace logged, debugger stopped | `require` - List bounds check
|Critical | End process, stack trace, maybe core dump | Memory corruption, type safety violation

Panics always stop the debugger (if attached) and log a stack trace. They are
unrecoverable from within sync code.  In async code they continue unwinding
the async stack up to some special top level exception handler.  The program
can recover, but a web server might report "500 Internal Server Error" or a
GUI might save the document and send a bug report back to the developer.

Normal error handling uses `throw` or `throwIf` to send the error up to the
caller.  There is no need to use a keyword to unwrap the result.  By default,
the caller will either get the result or automatically re-throw the error up
to its caller if it may do so.  If it may not re-throw, the caller must
manually check the result for an error.  The normal non-error path looks
like C# exception handling.


    // File.open either opens the file, or automatically passes the error up
    @stream = File.open("config.json")

    // If readAllLines fails, the file is closed and the error is passed up
    @textLines = stream.readAllLines()

An error can be detected when the function is called:

    if try File.open("config.json")@stream
        // Do something with `stream`, which is a FileStream
        // The file is closed at the end of the scope, even if it is a panic
    else
        // Do something with `stream`, which is an Error

Resource cleanup can be performed by the `drop` function, such as with
`FileStream` above.  Another method is to use `defer`:

    // Use `defer` to cleanup at the end of the scope
    @databaseHandle = c_function_to_open_a_database_handle("database")
    defer c_function_to_close_a_database_handle(databaseHandle)
    // The above function is called at the end of the scope, even if panic

The third method is to use `scope`..`finally` which is similar to C#
`try`..`finally`.  


### New, Equality, Clone, and Drop

The `new` function is the type constructor.  It does not have access to
`my` and may not call member functions except for another `new` function
(e.g. `new(a int)` may call `new()`).  

`_opEq`, `getHash`, `clone`, and `deepClone` are generated automatically
for types where all of the elements also implement these functions.  The 
`_opEq` function compares values, not object references.  Types that don't
implement an `_opEq` function may not be compared with `==` or `!=`.

Like Rust, an explicit `clone` is required to copy any mutable type that
requires dynamic allocation.  If the type contains only `int`, `str`, or
`ro List`, it will implicitly copy itself.  If the type contains `List`, `Map`,
or any other dynamically allocated mutable data, it must be explicitly cloned.
Some types, such as `FileStream` can't be cloned at all.

`clone` clones the entire object, but does a shallow copy of pointers.
For instance, `List<MyType>>` is cloned fully provided that `MyType`
doesn't contain a pointer.  Even if `MyType` contained a `List<str>`,
everything is cloned.  For `List<*MyType>`, the pointer is copied
and `MyType` is not cloned.  `deepClone` can be used to clone the
entire object graph regardless of pointers or circular references.

A type may define `drop`, which is called deterministically when a
local stack object goes out of scope.  If the object is not
local, the `drop` function may be called some time later.

The `drop` function does not have access to any references or `boxed`
types.  There is no zombie resurrection. 

### Lambda and Function Variables

The `@` symbol is used to make it easy to recognize new local variables:

    // Find max value and sort the list using lambdas
    @a = 0
    myList.For(@item => { a.max = Math.Max(item, a.max) })
    myList.Sort(@(a,b) => a > b)
    log.Info("Sorted list is {myList}, maximum value is: {a.max}")

Inside the lambda function, `return` is forbidden since it doesn't return
from thom the nearest `fun` scope.  Instead, `exit` is used.

**TBD:** Consider how to `break` out of the lambda.  Use a return type of `Breakable`?

## Operators

Operator precedence is mostly from Golang, but more compatible
with C and gives an error where not compatible:

|Operators | Notes
| :--- | :---
|x.y  f<type>(x)  a[i] | Primary
|- ! & ~ sizeof unsafe | Unary
|@|Capture new variable
|as is | Type conversion and comparison
|<< >>| Bitwise shift (not associative, can't mix with arithmetic operators)
|* / % & | Multiply, bitwise *AND* (can't mix arithmetic and bit operators)
|~| Bitwise *XOR* (can't mix with arithmetic operators)
|+ - &#124; | Add, bitwise *OR* (can't mix arithmetic and bit operators)
|.. ..+|Range (Low..High) and range count (Low..+Count).  Inclusive of low, exclusive of high. 
|== != < <= > >= === !== in|Not associative, === and !== is only for pointers
|and|Conditional, short circuit
|or|Conditional, short circuit
|=>|Lambda
|key:Value|Key value pair (only inside `()`, `[]` or where expected)
|,|Comma Separator (not an expression)
|= += -= *= /= %= &= |= ~= <<= >>=|Assignment Statements (not an expression)


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

The range operator `..` takes two `int`s and make a `Range` which is a
`type Range(High int, Low int)`.  The `..+` operator also makes a
range, but the second parameter is a count (`High = Low + Count`).  

Operator `==` does not default to object comparison, and only works when it
is defined for the given type.  Use `===` and `!==` for object comparison. 
Comparisons are not associative, so `a == b == c` is illegal.

There is no ternary operator, but an `if` expression can be
used with the same effect (e.g. `@a = if(a>b, "pass":"fail")`).
The syntax is still TBD.

The pair operator `:` makes a key/value pair which can be used
in a list to initialize a map.

Assignment is a statement, not an expression.  Therefore, expressions like
`a = b = 1` and `while (a = count) < 20` are not allowed. In the latter
case, use `while count@a < 20`.  Comma is also not an expression and may
only be used where they are expected, such as a function call or lambda.


#### Operator Overloading

`+`, `-`, `*`, `/`, `%`, and `in` are the only operators that may be individually
defined.  The `==` and `!=` operator may be defined together by implementing
`fun _opEq(a myType, b myType) bool`.  All six comparison operators,
`==`, `!=`, `<`, `<=`, `==`, `!=`, `>=`, and `>` can be implemented with just
one function: `fun _opCmp(a myType, b myType) int`.  If both functions
are defined, `_opEq` is used for equality comparisons, and `_opCmp` is used
for the others.

## Statements

Like Golang, semicolons are required between statements but they are
inserted automatically at the end of lines based on the last non-comment
token and the first token of the next line. Like C#, curly braces can be
placed anywhere.  Both end-of-line and beginning-of-next-line are acceptable.
The Zurfur IDE shrinks curly brace only lines so they take the same vertical
space as the brace-at-end style as when using a regular editor.

Unlike Golang and C#, compound statements (`if`, `else`, `while`, `for`, etc.)
can accept multiple lines without needing braces, but the indentation is
checked to make sure it matches the expected behavior.

1. Indentation is four spaces per scope level. No tabs anywhere in the source code.
    **TBD:** Might allow tabs, but the entire file must use the same method (no mixing of tabs/spaces)
2. One statement per line, unless it's a continuation line.  It's a continuation line if:
   1. The end of the previous line is `[`, `(`, or `,`.
   2. The line begins with `]`, `)`, `,` or any operator including `"`, `and`, `or`, `in`, `+`, `.`, `=`, etc.
   3. A few exceptions where continuations are expected (e.g. `where`, `require`, and a few other places)
3. Compound statements (e.g. `if`, `while`, `for`, etc.) may use or omit curly braces.  Curly braces may be at the end of the line or on their own line.
4. A brace, `{`, cannot start a scope unless it is in an expected place such as after
`if`, `while`, `scope`, etc., or a lambda expression.

#### While and Do Statements

The `while` loop is the same as C#.  The `do` loop is similar to C#
except that `dowhile` is used at the end of the loop, and the condition
executes inside the scope of the loop:

    do
        @accepted = SomeBooleanFunc()
        DoSomethingElse()
    dowhile accepted

#### Scope Statement

The `scope` statement creates a new scope:

    scope
        @file = use File.Open("My File")
        DoStuff(file)
    // File variable is out of scope here


The `scope` statement can be turned into a loop using the `continue` statement:

    scope
        DoSomething()
        if WeWantToRepeat()
            continue

Likewise, `break` can be used to exit early.

#### For Loop

For the time being, `for` loops only allow one format: `for @newVariable in expression`. 
The new variable is read-only and its scope is limited to within the `for` loop block.
The simplest form of the for loop is when the expression evaluates to an integer:

    // Print the numbers 0 to 9
    for @i in 10
        Console.writeLine(i)   // `i` is an integer

    // Increment all the numbers in an list
    for @i in list.Count
        list[i] += 1

The range operators can be used as follows:

    // Print all the numbers in the list
    for @i in 0..list.Count
        Console.writeLine(list[i])

    // Collect elements 5,6, and 7 into myList
    for @i in 5..+3
        myList.push(myList[i])

Maps can be iterated over:

    // Log key value pairs of all elements in a map
    for @kv in map
        Log.info(`Key: ${kv.key} is {kv.value}`)

The expression after `in` is evaluated at the start of the loop and never
changes once calculated:

    // Print the numbers from 1 to 9
    @x = 10
    for @i in 1..x
        x = x + 5               // This does not affect the loop bounds 
        Console.WriteLine(i)

When iterating over a collection, just like in C#, it is illegal to add
or remove elements from the collection.  An exception is thrown if
it is attempted.  Here are two examples of things to avoid:

    for @i in myIntList
        myIntList.Add(1)   // Exception thrown on next iteration

    // This does not remove 0's and increment the remaining elements
    // The count is evaluated only at the beginning of the loop.
    for @i in myIntList.Count
        if myIntList[i] == 0
            RemoveAt(i)        // There are at least two problems with this
        else:
            myIntList[i] += 1  // This will throw an exception if any element was removed
 
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



## Garbage Collection

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

JavaScript has done pretty well with the single threaded model.
IO is async and doesn't block.  Long CPU bound tasks can be
offloaded to a web worker.  Even Windows uses a single threaded
model for user interface objects.

## Raw Pointers

The `^` type is a raw C style pointer.  The `.` operator is used to access
fields or members of the referenced data.  The `.*` operator can dereference
the data.
 
    fun strcpy(dest ^byte, source ^byte)
        while source.* != 0
            { dest.* = source.*;  dest += 1;  source += 1 }
        dest.* = 0

#### Pointer Safety

For now...

Raw pointers are not safe.  They act axactly as they do in C.  You can corrupt
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

Raw pointers are never tracked by the GC.  Any object that may have a
pointer pointing into it must be pinned.

In an unsafe context, pointers can be cast from one type to another
using the `cast` operator.  The format is `cast(type)expression`.
For those coming directly from C, it will look almost the same
as an old C style cast, except the keyword `cast` must be used.

    @b = myFloatPtr.cast(^int)   // Cast myFloatPtr to *int

## Packages and Modules

A package is like a C# assembly.  It is the basic unit for distributing
a library or application and is a .`zip` file with a `.zil` extension.
It will be defined here [ZIL Specification](Doc/Zil.md).

Modules are like a C# static class and namespace combined.  They can contain
static functions, fields, and extension methods.  From within a package,
module names act like namespaces and stitch together just as they do in C#.
From outside the package, they look and act like a C# static class.

The `module` keyword does not nest, or require curly braces.  The module name
must be declared at the top of each file, after `use` statements, and before
type, function, or field definitions.  A file may contain other modules, but
all of them must be nested inside the top level module:


    module MyCompany.MyProject               // Top level module
    module MyCompany.MyProject.Utils         // Ok since it is nested in the top level
    module MyCompany.MyProject.OtherUtils    // Ok since it is also nested
    module MyCompany.MyOtherProject          // ILLEGAL since it is not nested

Package names should be unique across the world, such as a domain name
followed by a project (e.g. `com.gosub.zurfur`).  For now, top level module
names must be unique across an entire project.  If there are any top level
module name clashes, the project will fail to build.  In the future, there
may be syntax or project settings to resolve that.



## Open Questions

Should NAN`s compare the way IEEE numbers are supposed to? Since this is a new
language, my preference would be to have them compare so NAN != NAN and they
sort so NAN > INF.  This way we don't have to worry about doing special
things for Map or sorted collections.  OTOH, it could be confusing when
porting code from other places.  OTOOH, I've had plenty of problems with
the IEEE comparison behavior as it is.




