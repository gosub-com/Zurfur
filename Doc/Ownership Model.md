# Ownership and Memory Model

This is a work-in-progress, and is still just brainstorming. I'm trying to work out an ownership 
model that is simpler than Rust's borrow checker but is more useful than C#'s non-ownership model. 

Zurfur is garbage collected and also single threaded (like node.js), so we don't need an ownership
model to help with memory or thread safety. The primary purpose of the ownership model is to help
programmers and AI reason about aliasing and iterator invalidation.

NOTE: Zurfur is targeting WebAssembly.

## The Core Ownership Model

Ownership is the exclusive authority of a container to manage the existence, structural lifecycle,
and mutability boundaries of its data within a cycle-free hierarchy. Because ownership must form
a strict tree or directed acyclic graph, an object can never own its ancestors, which prevents object
graphs with reference cycles.

In a `List<Box<MyObject>>`, the outer `List` fully controls the lifecycle of everything inside it. 
If the list is cleared, its structural ownership of those objects ends. However, because memory 
safety is guaranteed by a garbage collector, the owner can hand out temporary views (borrows) 
of its data to async tasks without tracking who holds them. 

Once handed out, the container no longer has control over how long those references persist or how they
are used, except that non-`mut` references are strictly read-only. Any outstanding borrows held by async
tasks will safely delay final destruction until those tasks drop their references.

## Deep Immutability Model (`ro T`)

The type system separates data into mutable and deeply immutable variants. Every mutable data type
`T` possesses an immutable counterpart, `ro T`. Immutability is transitive and strictly follows the
ownership tree. For example, `ro List<Box<MyObject>>` structurally expands to `ro List<ro Box<ro MyObject>>`,
freezing the entire hierarchy. Conversion from `T` to `ro T` is achieved via `t.ro`, while reverting 
to a mutable instance requires a deep allocation via `ro_t.copy`. For convenience, `ro T` can be 
declared directly at the type definition, though it is not required.

While mutable types enforce strict exclusive ownership, immutable types bypass ownership entirely
in favor of unrestricted reference-sharing. Because `ro` types do not track ownership, assignments always
perform fast, non-allocating reference copies. For instance, a `List<ro Box<MyObject>>` with 1,000 elements
can safely have every single element point to the same immutable box instance in memory. Note that
a `List<ro MyObject>` has all of the elements in-line (not boxed) so even though they can be copied
quickly, they cannot be combined to point to the same instance in memory.  This means the former may be
more efficient even though `Box<MyObject>` is heap allocated.

Despite allowing widespread aliasing, this model strictly preserves the underlying acyclic constraint: 
immutable data can never form reference cycles. Developers are free to pass and share read-only data 
across async tasks without restriction. While recursively traversing an immutable tree might yield the 
same reference multiple times, it is guaranteed never to get trapped in an infinite loop.


## Move Semantics, Copying, and Equality

To preserve this strict structural hierarchy, assigning an allocating mutable data structure moves 
ownership by default. An assignment like `var a = myList` is illegal unless the compiler can 
prove that `myList` is not referenced after use within that local scope. To duplicate data instead
of moving it, an explicit `var a = myList.copy` can be used, which performs a deep copy across
the entire owned object tree, stopping only at non-owned boundaries like pointers. Conversely, 
non-allocating data structures (such as `struct`s and `ro` types) are implicitly copied.

Equality checks (`==`) follow the same structural boundaries and operate on the entire owned object
tree as strict logical equality. They don't use reference identity except when explicitly comparing
pointers or for optimizing read-only data type comparisons. For example, comparison of two variables
of the type `List<Box<MyObject>>` compiles only if `MyObject` implements equality. The comparison
evaluates the logical values deep within the structure. Consequently, comparing a list to its freshly
deserialized counterpart, `myList1 == Deserialize<List<Box<MyObject>>>`, will correctly evaluate to
`true` or `false` despite them occupying entirely different locations in physical memory. When reference
identity checks are required, a built-in function like `REF_EQ(myList1, myList2)` can be used.

## References and Borrows (&T)

References are second-class citizens. They are always owned by the stack (including async stacks)
and can never be stored inside any type owned by the heap. While a stack-owned collection can hold
references (e.g., a local `List<&T>`), the data structure itself cannot escape to the heap. This
restriction allows programmers to reason about references locally, keeps them strictly temporary,
and prevents them from forming complex object graphs.

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

To protect collection integrity during iteration, iterator invalidation is prevented. The language
distinguishes between structurally benign mutations like `myList[i] = someValue` and structural
modifications like `myList.append(someValue)`. The compile-time versus run-time rules are defined
later in *Iterator Invalidation, Uniqueness, and the Meaning of Ownership*.

## Pointers (^T)

Pointers act as an escape hatch to the ownership model, allowing shared mutable heap-allocated data.
Ownership ends at the pointer boundary and a pointer assignment copies the pointer verbatim without
allocating a new heap object, meaning multiple pointers can reference the same instance in memory.

Immutability also ends at the pointer boundary. When a pointer is contained within an immutable type
`ro ^R`, the pointer itself is immutable (it cannot be changed to point elsewhere) but the data it points
to remains fully mutable. Pointers to immutable data (i.e. `^ ro T`) are usually not needed because
`ro box<T>` is the preferred way to implement a pointer to immutable data. Note that `List<ro box T>`
is nearly (but not exactly) identical to `List<^ ro T>`.  

Because pointers break the strict tree hierarchy, they introduce risks:

 * **Reference Cycles:** Pointers are the only way to create reference cycles in Zurfur. Because
   safety is guaranteed by the garbage collector, cycles will not leak memory, but they can still
   cause infinite recursion if the programmer is not careful.

 * **Copy:** Data types that contain a pointer do not have a `.copy` operation. Instead, they have
   a `.copyShallow` which does not traverse into the pointer and a `.copyDeep` operation that safely
   copies the entire object graph, accounting for cycles. TBD: `shallowCopy` might be slightly
   misleading because it is really a deep copy of all owned data, but shallow at the pointer boundary.

 * **Equality**: TBD: Decide if `==` should either perform a shallow comparison or should be illegal
   (e.g. a function call to either `equalsShallow` or `equalsDeep` is required).

C# treats all classes as pointers, whereas they must be explicitly marked as such in Zurfur.
For example, the C# type `List<MyClass>` translates to `^List<^MyClass>`. Pointers are
dereferenced implicitly, so `myList[0].field` (like C#) is used to access fields of `MyClass`.


## Sync and Async Functions (`fun` and `afun`)

Sync and async functions act identically, except that a sync function cannot call an async function
and an async function can be suspended. Async functions suspend and block by default, so the `await`
keyword is not needed for normal async function calls.

Async does not imply heap allocation. Async means stack, but there can be many async stacks and they
can be suspended. References can be captured on an async stack, and even stored in a `List<&T>`, but
they can never escape from the stack they were originally created on.

	// References captured by this function cannot escape
	afun myAsyncFunction(list mut List<MyObject>)
		let slice = list.slice(1, 5) // This slice can persist across async calls
		callSync(list)      // This function cannot be suspended because it is a sync function
		callAsync(list)     // This function may or may not be suspended (other functions may run in between)
		callSync(list)      // This function will run immediately after callAsync completes
		slice[0].field = X	// This is valid, but the slice is gone when the function ends

Async is built into the type system because there is a benefit in knowing if a function is quick and atomic or
slow and non-atomic.

## Closures and Interfaces

Closures and interfaces hold references to data, and they (and their owned data) follow the same ownership
rules as other data types. This allows us to know if an interface contains a reference (e.g. a `ref` interface)
and also know if it escapes a stack, etc.


## Memory Model

Starting with C#'s `ref struct` system, we will restrict references so they can only be contained
in `ref` types, and `ref` types must be owned by the stack. This doesn't mean `ref` types can't
be used inside dynamically sized structures, so `List<&T>` is allowed to be created as a local
variable on an async stack frame, and it can be stored as a field in a `ref struct`. However, it
cannot escape the lifetime of the stack frame where the underlying references were originally captured.

Zurfur defines distinct categories for function input parameters and return values:

### Function Inputs

- **IN:** `fun f(p Type)` Passes the parameter as a read-only reference and does not transfer ownership.
  If `p` references mutable data, that data may still change during async suspension. If `p` is a `struct`,
  it is passed as a copy so the callee cannot retain a reference into another object, nor will the callee
  see changes to mutable data during async suspension. The compiler may optimize representation (reference
  or copy) as long as these semantics are preserved.

- **MUT:** `fun f(p mut Type)` Passes the parameter as a read/write reference and does not transfer ownership.
  Other mutable references may still exist. For `struct`, the caller must pass a reference to a copy (not
  a reference to the original owned data), and the caller must copy it back into the location where it is
  stored after the call. This is done to prevent the callee from retaining a reference into another object
  (same reason as **IN**). The call site must be explicit for struct mutation (e.g. `myStruct.mut.clear()` or
  `myFunction(mut myStruct)`). The compiler may optimize implementation details as long as semantics are
  preserved.

- **OWN:** `fun f(p own Type)` Passes the parameter and transfers its ownership to the callee which may
  modify, store, or drop the value.

- **UNIQUE:** `fun f(p unique Type)`. Take temporary full ownership and then give it back, possibly
  returning a new object. The compiler enforces that either there are no outstanding references into
  the object or that the caller uses the `unique` keyword which will cause a panic if the object is not
  unique. This alerts the programmer to a possible error, but also allows maximum flexibility.

- **OUT:** `fun f(p out Type)` The parameter is an output-only reference to a value to be written by the
  callee and then owned by the caller. An optimizing compiler might re-write functions from
  `fun f() Type` to `fun f(p out Type)` to increase efficiency of passing large types, etc.

### Function Returns

- **OUT (return):** `fun f() Type` The callee produces the value, and the caller takes ownership.

- **REF:** `fun f() ref Type` Returns a reference owned by another object. The caller may not modify
  the return, but it might get modified by the owner during any async function call.

- **MUT:** `fun f() mut Type` A mutable reference owned by another object. 

### Iterator Invalidation, Uniqueness, and the Meaning of Ownership

Ownership tells us structurally who owns the object, but not about the references that point into
it. When structural ownership is transferred, the outstanding references are left pointing into
the same object but with a new owner. This can lead to programming errors. Because of ownership
rules, stack reference tracking, and const-correctness, these errors should be easier to spot and reason
about than in most other languages.

However, there is one kind of error that must be prevented at all costs: iterator invalidation. We
cannot allow iteration over "ghost objects" created (maybe) when `append` is called on a list, nor
can we allow iterating over "empty spots" created when `pop` is called on the list.

This is accomplished with the `unique` keyword, and a combination of compile-time and run-time checks.
While iterating, Zurfur allows safe iterator non-invalidating mutations (e.g. `myList[0] = someValue`)
but prevents unsafe iterator invalidating mutations (e.g. `myList.append(someValue)`) either at compile-time
(when possible) or at run-time with an explicit run-time check. This means that holding a `Span` into
a `List` and calling `append` is either prevented by the compiler (when possible) or forces an explicit
`unique` run-time check that would panic because of the outstanding `Span`.

For example, the function `fun append<T>(list unique List<T>, item own T) Todo` requires the list to be
unique with no outstanding references. The programmer is free to call `myObject[0].myList.append(item)`
as long as the compiler can prove that there are no outstanding references into `myList`. If the compiler
cannot prove this to be true, the programmer must use an explicit run-time check
`myObject[0].myList.unique.append(item)`, which will panic if there are outstanding references into the list.

Uniqueness is shallow, meaning that if `myList` is unique, `myList[0].myListField` is not necessarily unique.
Furthermore, unique applies only to `List`, which is the only dynamically sized object in Zurfur. In the future,
other types may be allowed to opt in, but for now it applies only to `List` because other collections are expected
to either use `List` as an underlying container or to use `ro T` to avoid iterator invalidation issues.
These restrictions on uniqueness allow us to use a compacting GC without adding a lot of run-time reference
tracking overhead.

**Enforcement boundary (single source of truth):**

- **Compile-time:** when the compiler can prove there are no outstanding references into a collection,
  iterator-invalidating operations are allowed.

- **Run-time:** when compile-time proof is not possible, the caller must use an explicit `unique` check,
  which panics if outstanding references exist.


## Garbage Collection (Compacting or Reference Counting?)

Zurfur is single-threaded, stores nested data fields directly in-line by default (similar to C),
and operates within a strict ownership model. This deterministic landscape opens the door to
high-performance reference counting (RC) rather than relying on a heavy compacting runtime. By
utilizing static lifetime analysis, many (if not most) reference count updates can be entirely
eliminated at compile time.

To ensure a concrete, reliable initial implementation, the first version of the language will
use reference counting paired with a customized port of `dlmalloc` as its underlying heap allocator.

## Allocation Overhead and Intentional Boxing

Zurfur will store an additional 8 bytes of internal metadata (a 32-bit type ID and a 32-bit 
reference count) alongside `dlmalloc`’s standard 8-byte allocation header, totaling 16 bytes of
overhead per heap allocation.

This tracking penalty does not pose a hidden "overhead trap" because heap allocations must be
completely intentional:

* Types are stored in-line by default, meaning structures nest directly inside their parent
  frames without pointer indirection.
* To move an item to the heap, the programmer must explicitly declare a `Box<T>` or `^T`.
* Just as an experienced C++ developer avoids wrapping primitive integers in discrete heap
  allocations via `new int`, a Zurfur developer can visually audit and control memory overhead
  directly via `Box` usage in the source code.

## Compound Allocation Optimization for Deeply Immutable Types

For read-only types (`ro T`), boxing data is highly beneficial because it allows immutable structures
to be safely shared, re-used, or consolidated across the program. While deep read-only structures
might technically require many small boxed segments, allocating them independently might be slow and
fragment the heap.

To optimize this, Zurfur calculates the collective size of the entire immutable object tree
upfront—including the necessary `dlmalloc` sizing headers and type metadata for each node. The
runtime then requests a single, contiguous memory chunk from `dlmalloc`.

Because we have full access to the `dlmalloc` source code, the runtime can directly manipulate the
internal chunk headers and layout within this master block. It slices and dices the large allocation,
stamping out standard `dlmalloc` bounds and field pointer offsets inline. This approach ensures maximum
memory locality upon creation, while still physically allowing individual sub-chunks to be split off
or safely passed to `dlmalloc.free()` independently at any time.

## Mass Deallocation Fast-Path

When dropping a large compound object graph, the runtime checks for a highly optimized fast-path
before performing a standard recursive teardown. Because the ownership model defines a strict,
predictable hierarchy, the compiler can track whether any child node within a compound structure
has been captured by an external reference or pointer. If the runtime verifies that the reference
count of every internal heap allocation is exactly 1 (meaning they are exclusively held by their
parent containers) and that their physical structural offsets have not been mutated, it can bypass
individual element scans entirely. The runtime simply validates the metadata and deallocates the
entire collection of objects all at once in a single, lightning-fast memory sweep, reducing deep
graph reclamation to near-zero cost.

## Immediate Deallocation and Cyclic Data Handling

When a data structure's reference count falls to zero, it can be dropped immediately. While descending
a large, deeply nested object graph can introduce a slight pause, the teardown duration is strictly
bounded by the size of the graph itself. Dropping memory immediately ensures that chunks are reclaimed
the exact moment they become unreachable, preventing memory fragmentation.

While reference counting handles the vast majority of lifecycle tasks, a cycle garbage collector is still
needed to handle escape-hatch pointers (`^T`). This cycle detector can be exceptionally fast and efficient
because it only needs to scan types that explicitly contain pointers (`^T`), which are the unique source of
cycles in Zurfur. Programs that do not instantiate cyclic references, or that break them intentionally, incur
zero overhead, as the cycle detector never needs to run.

## Fat and Thin References

To track data structures without sacrificing speed, Zurfur differentiates between forward references
and return references:

* **Forward References (Thin):** References passed into a function (like a `Span`) are thin pointers. The
  caller guarantees that the root owner is locked securely in memory on the stack for the duration of
  the call, requiring no reference-counting overhead.

* **Return References (Fat):** References returned out of a function must track their root owner dynamically
  so that the caller can safely hold onto the data and decrement the appropriate reference counter when
  finished. These are fat pointers.

To maximize WebAssembly execution efficiency, a fat pointer is packed into a single 64-bit integer. The
lower 32 bits point directly to the target data payload, and the upper 32 bits point to the structural
owner, allowing the entire reference to be passed inside a single native wasm32 stack register.



