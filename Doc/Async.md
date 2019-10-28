# Async

Overview moved to main section.  In the future, this will describe implementation details.

## Cancellation

TBD: A task started with `astart`, can be caneled via `RequestCancel()`.  The
cancel request is propagated up the task stack and all awaiters are notified
via `TaskCancelledException`.

## Task Stack Frames

`Task<T>` does not live by itself on the heap.  Instead, the first one (allocated
with the `astart` keyword) is created on its own new very light weight stack frame.
Each additional async call uses the callers stack frame for parameters and local
variables.  This reduces the amount of garbage and eliminates the need for ValueTask.

