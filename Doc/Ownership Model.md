# Zurfur Ownership and Memory Model

This is a work-in-progress, and is still just brainstorming. I'm trying to work out an ownership 
model that is simpler than Rust's borrow checker but is more useful than C#'s non-ownership model. 

Zurfur is garbage collected and also single threaded (like node.js), so we don't need an ownership
model to help with memory or thread safety.  The primary purpose of the ownership model is to help
programmers and AI reason about aliasing and iterator invalidation. 

NOTE: Zurfur is targeting WebAssembly.

## The Core Ownership Model

Ownership is the exclusive authority of a container to manage the existence, structural lifecycle,
and mutability boundaries of its data within a strict, cycle-free hierarchy. Because ownership must
form a strict tree or directed acyclic graph, an object can never own its ancestors, which prevents 
object graphs with reference cycles.  

In a `List<Box<MyObject>>`, the outer list fully controls the lifecycle of everything inside it. 
If the list is cleared, its structural ownership of those objects ends. However, because memory 
safety is guaranteed by a garbage collector, the owner can hand out temporary views (borrows) 
of its data to async tasks without tracking who holds them.  Once handed out, the container no 
longer has control over how long those references persist or how they are used, except that 
non-`mut` references are strictly read-only.

When ownership ends, it triggers immediate, deterministic destruction of the data, provided
no active borrows exist. However, because memory safety is guaranteed by a garbage collector, 
outstanding borrows held by async tasks will safely delay final destruction until those tasks 
drop their references.  In this case, destruction becomes non-deterministic.  Most data
types will never declare a destructor because raw memory allocation and de-allocation are
managed by the runtime.

## Move Semantics, Copying, and Equality

To preserve this strict structural hierarchy, assigning an allocating data structure moves 
ownership by default. An assignment like `var a = myList` is illegal unless the compiler can 
prove that `myList` is not referenced after use within that local scope. To duplicate data instead
of moving it, an explicit `var a = myList.copy` must be used, which performs a deep copy across
the entire owned object tree, stopping only at non-owned boundaries like pointers. Conversely, 
non-allocating data structures (such as standard `struct`s) are implicitly copied, while temporary
access can always be achieved via an explicit borrow using `var a = &myList`.

Following the same structural boundaries, equality checks (`==`) operate on the entire owned object
tree as strict logical equality, never as reference identity (except when explicitly comparing pointers).
For example, comparison of two variables of the type `List<Box<MyObject>>` compiles only if 
`MyObject` implements equality.  The comparison evaluates the logical values deep within the structure. 
Consequently, comparing a list to its freshly deserialized counterpart, 
`myList1 == Deserialize<List<Box<MyObject>>>`, will correctly evaluate to `true` or `false`
despite them occupying entirely different locations in physical memory. When reference identity
checks are required, a built-in function like `REF_EQ(myList1, myList2)` can be used. 

## Deep Immutability Model (`ro T`)

The type system separates data into mutable and deeply immutable variants. Every mutable data type
`T` possesses an immutable counterpart, `ro T`. Immutability is transitive and strictly follows the 
ownership tree.  For example, `ro List<Box<MyObject>>` structurally expands to `ro List<ro Box<ro MyObject>>`,
freezing the entire hierarchy. Conversion from `T` to `ro T` is achieved via `t.ro`, while reverting 
to a mutable instance requires a deep allocation via `ro_t.copy`. For convenience, `ro T` can be 
declared directly at the type definition, though it is not required.

While mutable types are governed strictly by ownership and move semantics, immutable types bypass 
ownership entirely in favor of unrestricted reference-sharing. Because `ro` types do not track ownership,
assignments always perform fast, non-allocating reference copies. For instance, a `List<ro Box<MyObject>>`
with 1,000 elements can safely have every single element point to the same immutable box instance in memory.

Despite allowing widespread aliasing, this model strictly preserves the underlying acyclic constraint: 
immutable data can never form reference cycles. Developers are free to pass and share read-only data 
across async tasks without restriction. While recursively traversing an immutable tree might yield the 
same reference multiple times, it is guaranteed never to get trapped in an infinite loop.

## The Nature of References (Borrows)

References are second-class citizens. They are always owned by the stack (including async stacks) 
and can never be stored inside any type owned by the heap. While a stack-owned collection can hold
references (e.g., a local `List<&T>`), the data structure itself cannot escape to the heap. This 
restriction allows programmers to reason about references locally, keeps them strictly temporary,
and prevents them from forming complex object graphs.

References can be freely passed across async function boundaries, such as holding a sliced `Span`
across an async function, which unifies the synchronous and asynchronous programming models and 
eliminates the mental friction of switching between stack-bound and heap-bound view types
(e.g. There is no need for `Memory<T>`, we can always use `Span<T>`).

Multiple mutable references to the same data are permitted. Because references are explicit and
explicitly mutable, it is clear to the programmer when aliasing could become a problem. Ownership
in Zurfur is designed to help programmers reason about data structures, not to enforce strict memory
or thread safety. 

Zurfur is strictly const-correct. Function parameters that mutate data must be explicitly marked 
with `mut`. Note that const-correctness enforces immutability, whereas ownership enforces uniqueness.
Even if const-correctness were added to a language like C#, it would still lack ownership since
mutable objects could link together to form arbitrary graphs.

To protect collection integrity during iteration, iterator invalidation is prevented. The compiler 
distinguishes between structurally benign mutations like `myList[i] = someValue` and structural
modifications like `myList.append(someValue)`, enforcing safety at runtime or compile time where
invalidation could occur.

Open Questions:

    * Should we use a compacting collector or reference counting?  I am leaning towards reference
      counting because single-threading with ownership will make it extremely efficient by removing
      many reference count updates at compile time. 
    * Should we allow true multi-threading?  I am leaning towards no because we can supply async libraries
      that call into other web workers to do CPU intensive work.
    * Should fields of types be in-line (like C) or on the heap (like C#)? 
      Efficiency argues for in-line by default, but ease of use argues for keeping them heap allocated by default.

## Pointers

Pointers represent mutably shared ownership.  They are owned by the heap, and they are reference counted. 
In C# all classes are pointers.  But in Zurfur, we would have to explicitly declare them as a pointer. 

	// In C#, this function...
	void myFunction(List<MyClass> myList)

	// ...translates to
	fun myFunction(myList mut ^List<^MyClass>)


## Closures and Interfaces

Both closures and interfaces represent some data and they both present ownership problems.

	// Non-escaping lambda and interface.
	// The lambda `f` and interface `i` are owned by the caller and cannot escape.
	fun myLambdaFun(f fun(p parameters)) {}
	fun myInterfaceFun(i ISomeInterface) {}

In this type:

	type MyType
		d List<Int>
		f fun()
		i MyInterface

All fields `d`, `f`, and `i` are owned by `MyType`.  So, we have the same problems with data `d` as
with lambdas and interfaces, and we should have the same model.


## Async

It's important to understand that the async model is fundamentally different from C#.
Async does not imply heap allocation.  Async means stack, but there can be many async stacks
and they can be suspended.

Sync and async functions are identical, except that a sync function cannot call an async function
and an async function can be suspended.  There is no need for the `await` keyword because an async 
function executes and suspends by default.  If we want to launch multiple async functions in parallel, 
we need to use a keyword like `astart`, which could return a future.

	afun myAsyncFunction(p)
		callSync(p)		// This function cannot be suspended
		callSync(p)		// Guaranteed to run immediately after the above, without interruption
		callAsync(p)	// This function may or may not be suspended (other functions may run in between)
		callSync(p)		// This function will run immediately after callAsync completes

Async is built into the type system because there is benefit in knowing if a function is quick or slow.
It allows reference counting to be fast and local reasoning about state within a sync function.

## Garbage Collection (Compacting or Reference Counting?)

Zurfur is single threaded and has an ownership model.  This opens the door to very fast reference 
counting. Many (maybe even most) reference count updates can be eliminated at compile time.

Using DL MALLOC as our base, there are 8 bytes of overhead per allocation.  Additionally
there are 8 bytes of metadata (type ID and reference count) for a total of 16 bytes per allocation.
And then we also need the pointer to the allocated data, for a total of 20 bytes.  

Should fields declared in a type be in-line (like C) or on the heap (like C#)? 
The extra overhead per allocation argues strongly for in-line by default.
Ease of use argues for keeping them all heap allocated. 

Some of this overhead can be mitigated by using `struct` and also allowing the programmer
to use `inline` (at the expense of programming convenience) for space critical data types. 

But, we can optimize and get memory locality at the same time.
When allocating, we add the sizes of all the types including the DL MALLOC and metadata
overhead.  Then we allocate just one big chunk of memory and very quickly fill in the
DL MALLOC, metadata, and field pointer offsets all at once in such a way as each chunk can 
be freed individually at any time.  Yes, we use more memory and retain the reference
indirections, but allocation is very fast.

For deallocation, we can very quickly scan and collect the entire object all at once if none of 
the pieces have been captured (ref count) or field references changed (simple offset comparison).
In most cases, many objects would be deallocated all at once, but there is a fallback
in place to allow captures and modifications.



## Memory Model

Starting with C#'s `ref struct` system, we will restrict references so they can only be contained
in `ref` types, and `ref` types must be owned by the stack.  This doesn't mean `ref` types can't
be on the heap, so, `List<&T>` is allowed to be created as a local variable in an async function.
It can be stored as a field in a `ref` struct. It cannot be declared as a field of a non-`ref` type.  


Zurfur defines distinct categories for function input parameters and return values:

### Function Inputs

- **IN:** `fun f(p Type)` Read-only reference or a logical copy for struct.
  `p` could have come from a mutable or immutable type, so it's possible `p` could mutate at 
  any async function call unless it is a struct, in which case it can't because it is a copy.
- **MUT:** `fun f(p mut Type)` Read/write reference.  The caller provides a reference to a type
  that the callee may modify.  This is different than `inout` because `mut` does not mean ownership.
  It is possible that there are other mutable references to `p`.  If `p` is a `List<T>` and we have
  runtime iterator invalidation checks, then `p.append` is allowed but would throw if there are any
  outstanding references.  If we have compile-time iterator invalidation checks, `p.append` is not
  allowed.  TBD: Not legal for a struct because structs are copy only.
- **OWN:** `fun f(p own Type)` Ownership transfer. The callee takes responsibility for the value.
  Problem: Calling `f(myList[3])` takes ownership and creates a hole in `myList`. 
  How does Rust handle this?  Perhaps `f(myList[3].replace(someValue))`.
  I'm worried we might enter Rust-like complexity here.
- **INOUT:**  `fun f(p inout Type)`.  Take temporary ownership, then give it back.
  Appending to a list would always be allowed because it's expected you might get back a different `p`.
  I'm worried we might enter Rust-like complexity here.
- **OUT:** `fun f(p out Type)` Output parameter is a reference to a value to be written by the callee, 
  and then owned by the caller.  An optimizing compiler might re-write functions from
  `fun f() Type` to `fun f(p out Type)` depending on the goals.
  

### Function Returns

- **OUT (return):** `fun f() Type` The callee produces the value, and the caller takes ownership.
- **REF:** `fun f() ref Type` A reference owned by another object.  The caller may not
  modify the return, but it might get modified by the owner during any async function call.
- **MUT:** `fun () mut Type` A mutable reference owned by another object.


### The Ownership Problem

We don't care about preventing multiple mutable references as much as in Rust because we can be memory safe
because of GC. But we do care about local reasoning and preventing stupid aliasing mistakes like iterator
invalidation.  We also care about ensuring that when we say something is owned, we know it is only owned
by just one thing, even though there might be outstanding references somewhere else.

The big problem is this: We have a `List<Type>`.  And we have two kinds of mutability, one that causes iterator
invalidation and one that doesn't:

	fun modifyList(p mut List<Type>)
		p[2].field = X			// Doesn't cause iterator invalidation
		p[2] = Type()			// Doesn't cause a problem
		p.append(Type())		// Causes iterator invalidation if someone holds a Span into p
		p[2].list[0].append(1)	// Causes iterator invalidation if someone holds a span into p[2].list[0]

The problem with `p.append` is that it can create a completely new item, and then the iterator is iterating
through an old copy (or, if we call `p.remove`, the iterator might be iterating through an empty spot).

There are two ways to solve this.  One way is we could check for uniqueness when calling `append` or 
`remove` and generate a runtime error.  This is similar to C#, except the exception is generated by the
owner when they use `append` instead of by the user when they iterate to the next item.

A second way we could try and solve this is in the type system at compile time.  If we restrict a mutable
reference to non-ownership, we get this:

	// We have a mutable reference, but it's not necessarily a unique reference
	fun modifyListNeverAppend(p mut List<Type>)
		p[0] = Type()		// Legal because it can't affect outstanding iterators or spans 
		p.append(Type())	// Illegal because there might be outstanding iterators or spans
		p[0].append(...)	// Illegal for the same reason

But sometimes we need ownership to do things like append:

	// We have temporary ownership which guarantees p is unique at the time of the call
	fun modifyListMaybeAppend(p inout List<Type>)
		p.append(Type())	// Legal because p is unique and the caller expects a possibly new p



### Static Variables

We want to allow as much flexibility as possible, so it should be possible to have mutable
static variables if that's what the programmer wants to do.  But there probably needs to be 
some restrictions to keep ownership working properly.  

Without static variables, we know that a function's `fun f(a Type1, b Type2) ref Type3` return
value must have come either from one of the parameters passed in, or that it came from a new
object created inside `f`.  (AI, check that this is a true statement, did I miss anything?).

With static variables, the above function might get a reference to something owned by one of the
parameters or from any static variable.  This means that you can't have true ownership of a static
variable because there can be outstanding borrows against it at any time.  You could still do
a `myStatic[i] = someValue` because the thing being replaced still exists until the last
reference is released.   But you couldn't take ownership with something like 
`var myOwnedVariable = myStatic[i].replace(someValue)` because there could be outstanding
references


### True ownership, or GC trick?

Having a GC (whether it's ref counted or compacting) can keep us memory safe, but it doesn't
answer all the questions of whether or not the object we have has outstanding references or not.

If we have `fun f(a mut List<T>)` we know that that there could be other outstanding mutable
references. `a.append` is either illegal (compile-time check) or it will fail at runtime if 
there are outstanding references.

But, if we do have ownership `fun f(a own List<T>)`, does that mean we've guaranteed there are
no other references at all, anywhere, even if we are in an async context.  Can a simple borrow 
checker (without lifetimes or much complexity) verify that 
`var myOwnedVariable = anotherMyOwnedVariable[i].replace(someOtherOwnedVariable)`
will work properly.

If it is impossible, then we can use reference counting and accept that `myList.append` could fail.



