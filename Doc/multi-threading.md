# Multi-Threading

Zurfur is currently single-threaded. This is an intentional starting point, not a permanent
limitation. True multi-threading is planned for a future version of the language.

NOTE: Zurfur is targeting WebAssembly, where single-threaded execution is still the dominant model.

## Why Start Single-Threaded?

* JavaScript and Node.js have proven that async/await can handle the vast majority of real-world
  workloads without threads.
* Web Workers can fill the gap until full multi-threading is supported.
* Ownership model stays focused on aliasing and iterator invalidation, not thread safety.
* Simple non-atomic reference counting — no atomic overhead, no lock-free complexity.

The plan is to add true multi-threading in the future without changing the surface language or
forcing users to rewrite their code. The type system is already designed to make this possible.

## How Multi-Threading Will Be Added

Zurfur's type system already draws the exact line that makes multi-threading safe without locks:

* **Mutable data** lives on a thread-local heap and is never shared across threads. Passing mutable
  data to a new thread transfers it exclusively. The compiler verifies this through simple local
  analysis — the same kind used for move semantics. If the data was created locally and no borrows
  have been passed to other async tasks at the spawn site, `spawn(myData)` is allowed with no
  annotation and no runtime cost. If the compiler cannot prove it statically, the programmer uses
  `spawn(unique myData)`, which performs a runtime deep check and panics if any outstanding borrows
  exist anywhere in the owned tree. Because only one thread ever holds mutable data at a time,
  simple non-atomic reference counting works for all mutable data even in a multi-threaded runtime.

* **Immutable data (`ro T`)** is deeply and transitively read-only. It is safe to share across
  threads without any locking because no thread can modify it. When multi-threading is added, `ro`
  values will be promoted to a shared heap using atomic or biased reference counting, which is a
  pure runtime change with no language surface impact.

* **References (`&T`, `mut &T`)** are already stack-confined by design and cannot escape to the
  heap or to static memory. They naturally cannot cross thread boundaries.

There will be no `lock` keyword and no shared mutable state across threads.

## No Shared Mutable State

This model means two threads can never access the same mutable data at the same time, which is
what makes the model safe without locks. But, what if multiple threads need to update the same
data structure, like a shared cache or a large map?

The answer is the **actor model**, which Erlang has used at massive scale for decades (WhatsApp,
RabbitMQ, and major telecom systems all run on it). One async task owns the mutable data and
services requests from other threads. Because only one task ever touches the data, no lock is
needed. Access is serialized naturally by message passing.

For the common case of simple shared counters or flags, Zurfur will provide a small set of
built-in atomic types (`Atomic<Int>`, `Atomic<Bool>`) that can be shared freely across threads
without an owner task.

| Need | Solution |
| :--- | :---
| Shared read-only data | `ro T` — always safe, no overhead
| Shared mutable data structure | One async task owns it, others send messages
| Shared counter or flag | `Atomic<Int>` / `Atomic<Bool>` built-ins
| Data pipeline | Transfer `own T` between threads

## The Upgrade Path

The phased approach keeps the language design clean:

1. **Now:** Single-threaded, simple reference counting, no atomic overhead, easy to implement and
   debug. This is the version being shipped.

2. **Later:** Add `spawn` and channels that accept only `own` and `ro` arguments. The type system
   already enforces the right rules; this is adding syntax and a scheduler.

3. **Runtime upgrade:** Promote the `ro` heap to use biased reference counting so that `ro` objects
   that stay on one thread pay no atomic cost, and only pay a small synchronization cost when first
   shared across threads. This is a runtime-only change with no language breakage.

The result is a language where multi-threading is safe by construction, costs nothing for code that
does not use it, and requires no locks, mutexes, or atomic primitives in user code.
