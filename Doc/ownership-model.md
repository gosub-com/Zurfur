# Ownership Model

This is a work-in-progress, and is still just brainstorming. I'm trying to work out an ownership 
model that is simpler than Rust's borrow checker but is more useful than C#'s non-ownership model. 

Zurfur is garbage collected and also single threaded (for now), so we don't need an ownership
model to help with memory or thread safety. The primary purpose of the ownership model is to help
programmers and AI reason about aliasing and iterator invalidation.

NOTE: Zurfur is targeting WebAssembly.

## The Core Ownership Model

Ownership is the exclusive authority of a container to manage the existence, structural lifecycle,
and mutability boundaries of its data within a cycle-free hierarchy. Because ownership must form
a strict tree or directed acyclic graph, an object can never own its ancestors, which prevents object
graphs with reference cycles.

In a `List<box MyObject>`, the outer `List` fully controls the lifecycle of everything inside it. 
If the list is cleared, its structural ownership of those objects ends. However, because memory 
safety is guaranteed by a garbage collector, the owner can hand out temporary views (borrows) 
of its data to async tasks without tracking who holds them. 

Once handed out, the container no longer has control over how long those references persist or how they
are used, except that non-`mut` references are strictly read-only. Any outstanding borrows held by async
tasks will safely delay final destruction until those tasks drop their references.

## Deep Immutability Model (`ro T`)

The type system separates data into mutable and deeply immutable variants. Every mutable data type
`T` possesses an immutable counterpart, `ro T`. Immutability is transitive and strictly follows the
ownership tree. For example, `ro List<box MyObject>` structurally expands to `ro List<ro box ro MyObject>`,
freezing the entire hierarchy. Conversion from `T` to `ro T` is achieved via `t.toRo`, while reverting 
to a mutable instance requires a deep allocation via `ro_t.copy`.

While mutable types enforce strict exclusive ownership, immutable types bypass ownership entirely
in favor of unrestricted reference-sharing. Because `ro` types do not track ownership, assignments always
perform fast, non-allocating reference copies. For instance, a `List<ro box MyObject>` with 1,000 elements
can safely have every single element point to the same immutable box instance in memory. Note that
a `List<ro MyObject>` has all of the elements in-line (not boxed) so even though they can be copied
quickly, they cannot be combined to point to the same instance in memory.  This means the former may be
more efficient even though `box MyObject` is heap allocated.

Despite allowing widespread aliasing, this model strictly preserves the underlying acyclic constraint: 
immutable data can never form reference cycles. Developers are free to pass and share read-only data 
across async tasks without restriction. While recursively traversing an immutable tree might yield the 
same reference multiple times, it is guaranteed never to get trapped in an infinite loop.


## Move Semantics, Copying, and Equality

To preserve this strict structural hierarchy, assigning an allocating mutable data structure moves
ownership by default. An assignment like `var a = myList` transfers ownership of `myList` to `a`.
This is only permitted if the compiler can prove, through simple local analysis, that the original
variable (`myList`) is not used again within the current function. This check is intentionally simple
and obvious.

To duplicate data instead of moving it, an explicit `var a = myList.copy` can be used. This performs
a deep copy across the entire owned object tree, stopping only at non-owned boundaries like `ro` types
or pointers. Conversely, non-allocating data structures (such as `struct`s and `ro` types) are always
implicitly copied on assignment.

Equality checks (`==`) follow the same structural boundaries and operate on the entire owned object
tree as strict logical equality. They don't use reference identity except when explicitly comparing
pointers or for optimizing read-only data type comparisons. For example, comparison of two variables
of the type `List<box MyObject>` compiles only if `MyObject` implements equality. The comparison
evaluates the logical values deep within the structure. Consequently, comparing a list to its freshly
deserialized counterpart, `myList1 == Deserialize<List<box MyObject>>`, will correctly evaluate to
`true` or `false` despite them occupying different locations in physical memory. When reference
identity checks are required, a built-in function like `REF_EQ(myList1, myList2)` can be used.

## References and Borrows (&T)

References are second-class citizens, and their lifetimes are strictly managed to prevent them from
being owned by the heap or by static memory. This model is similar to C#'s `ref struct` system.

- References (`&T`) can only be held by `ref` types (which are analogous to C#'s `ref struct`s).

- `ref` types are always owned by the stack (including async stacks) and can never be stored in
  static global memory or any type owned by the heap.

This restriction allows programmers to reason about references locally, keeps them strictly temporary,
and prevents them from forming complex object graphs that outlive the data they point to.
A `ref` type can contain heap allocated types, such as a `List<&T>`, as long as the entire structure
remains owned by the stack.

References can be freely passed across async function boundaries, such as holding a sliced `Span`
across an async function, which unifies the synchronous and asynchronous programming models and
eliminates the mental friction of switching between stack-bound and heap-bound view types
(e.g., There is no need for `Memory<T>`, we can always use `Span<T>`).  

Multiple mutable references to the same data are permitted. Because references are explicit and
explicitly mutable, it is clear to the programmer when aliasing could become a problem. Ownership
in Zurfur is designed to help programmers reason about data structures, not to enforce strict memory
or thread safety.

Zurfur is strictly const-correct. Function parameters that mutate data must be explicitly marked
with `mut`. Note that const-correctness enforces immutability, whereas ownership enforces uniqueness.
Even if const-correctness were added to a language like C#, it would still lack ownership since
mutable objects could link together to form arbitrary graphs.

### Enforcement

Starting with C#'s `ref struct` system, we will restrict references so they can only be contained
in `ref` types, and `ref` types must be owned by the stack. This doesn't mean `ref` types can't
be used inside dynamically sized structures, so `List<&T>` is allowed to be created as a local
variable on an async stack frame, and it can be stored as a field in a `ref struct`. However, it
cannot escape the lifetime of the stack frame where the underlying references were originally captured.

## Function References

Zurfur defines distinct categories for function input parameters and return values:

### Function Inputs

- **IN:** `fun f(p T)` Passes the parameter as a read-only reference (`&T`) and does not transfer
  ownership. If `p` references mutable data, that data may still change during async suspension. If `p`
  is a `struct`, it is passed as a copy so the callee cannot retain a reference into another object, nor
  will the callee see changes to mutable data during async suspension. The compiler may optimize 
  representation (reference or copy) as long as these semantics are preserved.

- **MUT:** `fun f(p mut T)` Passes the parameter as a read/write reference (`mut &T`) and does not
  transfer ownership. Other mutable references to `p` may still exist.

- **OWN:** `fun f(p own T)` Passes the parameter and transfers its ownership to the callee which may
  modify, store, or drop the value.  Ownership is structural, and this does not guarantee there are
  no outstanding references into the object. 

- **UNIQUE:** `fun f(p unique T)`. Take temporary full ownership and then give it back, possibly
  returning a new object. The compiler enforces that either there are no outstanding references into
  the object or that the caller uses the `unique` keyword which will cause a panic if the object is not
  unique. See *uniqueness* below for more details.

- **OUT:** `fun f(p out T)` The parameter is an output-only reference to a value to be written by the
  callee and then owned by the caller. An optimizing compiler might re-write functions from
  `fun f() T` to `fun f(p out T)` to increase efficiency of passing large types, etc.

Passing a `struct` or `ro` parameter with `mut`, `own`, or `unique` is not allowed. Some exceptions
may be allowed for generic types (e.g. `fun f<T>(p own T)` would allow passing in a `ro` or `struct` type).

### Function Returns

- **OUT (return):** `fun f() T` The callee produces the value, and the caller takes ownership.

- **REF:** `fun f() &T` Returns a reference owned by another object. The caller may not modify
  the return, but it might get modified by the owner during any async function call.

- **MUT:** `fun f() mut &T` A mutable reference owned by another object. 

### Uniqueness, Iterator Invalidation, and the Meaning of Ownership

Ownership tells us structurally who owns the object, but not about the references that point into
it. When structural ownership is transferred, the outstanding references are left pointing into
the same object but with a new owner. This can lead to programming errors. Because of ownership
rules, stack reference tracking, and const-correctness, these errors should be easier to spot and
reason about than in most other languages.

However, there is one kind of error that must be prevented at all costs: iterator invalidation. We
cannot allow iteration over "ghost objects" created (maybe) when `append` is called on a list, nor
can we allow iterating over "empty spots" created when `pop` is called on the list.

This is accomplished with the `unique` keyword and a combination of compile-time and run-time checks.
Zurfur distinguishes between ordinary access to elements and exclusive structural access to a container's
backing storage. Safe non-invalidating mutations (e.g. `myList[0] = someValue`) are allowed even while
references or iterators into the list exist. By contrast, structurally invalidating operations such as
`append`, `push`, `pop`, `remove`, `clear`, or anything else that may resize, reallocate, or reshuffle
the underlying storage require the list to be unique.

If the compiler can prove there are no outstanding references, the call is allowed. If it cannot, the
programmer must use an explicit run-time check: `myList.unique.append(item)` which will panic if there
are any outstanding borrows.  The `unique` keyword alerts the developer that there could be a run-time
error if they are not careful about handing out references to the collection. The run-time mechanism is
intended to be lightweight: the list's backing array tracks outstanding borrows with a reference count,
and a structural operation will panic if that count is non-zero. For defensive programming, `List`
supports `isUnique` which returns true if there are no outstanding references into it.

Uniqueness is shallow and applies to structurally invalidating operations on resizable storage. In the
current design, `List` is the only built-in dynamically sized heap object, so it is the main place where
`unique` appears explicitly. This restriction is intentional, as it minimizes the reference tracking
overhead required, should Zurfur switch to a compacting garbage collector in the future. Other collections
such as `Map` are expected to use `List` as an underlying container and therefore inherit the same rule
for any operation that could invalidate outstanding views into that storage. This means `unique` is not a
general borrow-checking mode for all mutation; it is a targeted rule for structural invalidation.

## Pointers (^T)

Pointers act as an escape hatch to the ownership model, allowing shared mutable heap owned data
and object graph cycles.  Ownership ends at the pointer boundary and a pointer assignment copies
the pointer verbatim without allocating a new heap object, meaning multiple pointers can reference
the same instance in memory.

When a pointer is contained within an immutable type `ro ^T`, neither the pointer nor the data
behind it can be modified. However, there can be other holders of `^T` that may modify the data 
behind the pointer.

Pointers to immutable data (i.e. `^ ro T`) are usually not needed because `ro box<T>` is the preferred
way to implement a pointer to immutable data. Note that `List<ro box T>` is nearly (but not exactly) 
identical to `List<^ ro T>`.  

Because pointers break the strict tree hierarchy, they introduce risks:

 * **Reference Cycles:** Pointers are the only way to create reference cycles in Zurfur. Because
   safety is guaranteed by the garbage collector, cycles will not leak memory, but they can still
   cause infinite recursion if the programmer is not careful.

 * **Copy:** Data types that contain a pointer do not have a `.copy` operation. Instead, they have
   a `.copyShallow` which performs a deep copy of all owned data but only copies the pointer value 
   (shallow copy). For a full structural clone, a `.copyDeep` operation can be used, which safely
   copies the entire object graph, accounting for cycles.

 * **Equality**: The `==` operator is a compile-time error for any data structure that contains a
   pointer. This is because the intent is ambiguous. Instead, the programmer must explicitly call
   either `equalsShallow(a, b)` to compare owned data and pointer identity, or `equalsDeep(a, b)`
   to perform a full structural comparison that traverses pointers.

C# treats all classes as pointers, whereas they must be explicitly marked as such in Zurfur.
For example, the C# type `List<MyClass>` translates to `^List<^MyClass>`. Pointers are
dereferenced implicitly, so `myList[0].field` (like C#) is used to access fields of `MyClass`.


## Sync and Async Functions (`fun` and `afun`)

Sync and async functions act identically, except that a sync function cannot call an async function
and an async function can be suspended. Async functions suspend and block by default, so the `await`
keyword is not needed for normal async function calls.

Async does not imply heap allocation. Async means stack, but there can be many async stacks and they
can be suspended. References can be captured on an async stack, survive across async suspension, and even
be stored in a `List<&T>`, but they can never escape from the stack they were originally created on.

Allowing references to survive suspension is important so iterators, spans, and other borrowed views can
flow naturally through async code without forcing copies or special async-only view types. The cost of
this flexibility is not a full Rust-style borrow checker. Instead, any outstanding borrow continues to
block structurally invalidating operations on the underlying `List` storage for as long as that borrow
remains live, even across async calls.

	// References captured by this function cannot escape
	afun myAsyncFunction(list mut List<MyObject>)
		let slice = list.slice(1, 5) // This slice can persist across async calls
		callSync(list)      // This function cannot be suspended because it is a sync function
		callAsync(slice)    // This function may suspend while the borrowed slice remains live
		callSync(list)      // Structural mutation still requires uniqueness while slice is live
		slice[0].field = X  // This is valid, but the slice is gone when the function ends

Async is built into the type system because there is a benefit in knowing if a function is quick and atomic or
slow and non-atomic.

## Closures and Interfaces

Closures and interfaces hold references to data, and they (and their owned data) follow the same ownership
rules as other data types. This allows us to know if an interface contains a reference (e.g. a `ref`
interface) and also know if it escapes a stack, etc.




