# Memory Model

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

This tracking penalty does not pose a hidden "overhead trap" because heap allocations are
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
might technically require many small boxed segments, allocating them independently can be slow and
fragment the heap.

To optimize this, Zurfur could calculate the collective size of the entire object tree
upfront—including the necessary `dlmalloc` sizing headers and type metadata for each node. The
runtime could then request a single, contiguous memory chunk from `dlmalloc`.

Because we have full access to the `dlmalloc` source code, the runtime could directly manipulate the
internal chunk headers and layout within this master block. It could slice and dice the large allocation,
stamping out standard `dlmalloc` bounds and field pointer offsets inline. This approach would ensure maximum
memory locality upon creation, while still physically allowing individual sub-chunks to be split off
or safely passed to `dlmalloc.free()` independently at any time.

## Mass Deallocation Fast-Path

When dropping a large compound object graph, the runtime could check for a highly optimized fast-path
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
the moment they become unreachable, preventing memory fragmentation.

While reference counting handles the vast majority of lifecycle tasks, a cycle garbage collector is still
needed to handle escape-hatch pointers (`^T`). This cycle detector can be exceptionally fast and efficient
because it only needs to scan types that explicitly contain pointers (`^T`), which are the unique source of
cycles in Zurfur. Programs that do not instantiate cyclic references, or that break them intentionally, incur
zero overhead, as the cycle detector never needs to run.

## Fat and Thin References

To track data structures without sacrificing speed, Zurfur could differentiate between forward references
and return references:

* **Forward References (Thin):** References passed into a function (like a `Span`) are thin pointers. The
  caller guarantees that the root owner is locked securely in memory on the stack for the duration of
  the call, requiring no reference-counting overhead.

* **Return References (Fat):** References returned out of a function must track their root owner dynamically
  so that the caller can safely hold onto the data and decrement the appropriate reference counter when
  finished. These are fat pointers.

To maximize WebAssembly execution efficiency, a fat pointer can be packed into a single 64-bit integer. 
The lower 32 bits point directly to the target data payload, and the upper 32 bits point to the structural
owner, allowing the entire reference to be passed inside a single native wasm32 stack register.

