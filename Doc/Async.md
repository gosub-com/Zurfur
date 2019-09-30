# Async

I like Golang's concept of async.  Everything should be async by
default, but look as if it were sync. 

The problem with this approach is that WebAssembly doesn't support
the same kind of stack switching used by Golang. It would be difficult
to optimize function calls through a delegate that may or may not be
async.  One of the goals of Zurfur is that it be as fast and efficient
as C, so this is too high a price to pay.

For the time being, async is built into the type system but it looks and
acts as if it were sync.  Calling an async function from async code blocks
without using the `await` keyword:

    afunc MySlowIoFunctionAsync(server string) string 
    {
        // In C# `await` would be needed before both function calls, but not in Zurfur
        #a = MakeXhrCallToServerAsync(server); // Blocks without await keyword
        Task.Delay(100);  // Also blocks without a keyword
        return a;
    }

Async code normally looks and acts as if it were sync.  When we want
to start or wait for multiple tasks, we use the `astart` and `await` keywords.

    afunc GetStuffFromSeveralServers() string 
    {
        // Start the functions, but do not block
        #a = astart MySlowIoFunctionAsync("server1");
        #b = astart MySlowIoFunctionAsync("server2");
        #c = astart MySlowIoFunctionAsync("server3");

        // The timeout cancels the task after 10 seconds, but we'll hand
        // the task to the user who may push a button to cancel early
        #timeout = astart Task.Delay(10000); 
        GiveThisTaskToAUserWhoCancelTheOperationEarly(timeout)

        // Collect the results in the order they complete order
        #sum = new list<string>()
        await a, b, c, timeout
        {
            case a.HasResult: sum += a.Result;
            case b.HasResult: sum += b.Result;
            case c.HasResult: sum += c.Result;
            case a.HasException: sum += "a failed" // It threw an exception but swallow it and continue
            case b.HasException: sum += "b failed"; break;  // Cancel remaining tasks and exit immediately
            case timeout.HasResult: break;  // 10 seconds has passed, cancel automatically
            case timeout.HasException: break;  // The user has cancelled the operation early
            // TBD: break canceles all remaining tasks
            // TBD: If `c` throws, all remaining tasks are canceled.
        }
        // TBD: The only way to get out of an `await` is when all of the awaited
        // tasks have completed completed (possibly with an exception)

        // Not strictly necessary, but TBD good practice? 
        // TBD: Make sure Task functions can use `FinalizeNotify` to clean up
        timeout.Cancel();  
    }

A sync function cannot implicitly call an async function, but it can call it
using the `astart` keyword, like this: `func MySyncFunction() { astart MyAsyncFunction() }`

## Cancellation

TBD: A task started with `astart`, can be caneled via `RequestCancel()`.  The
cancel request is propagated up the task stack and all awaiters are notified
via `TaskCancelledException`.

## Task Stack Frames

`Task<T>` does not live by itself on the heap.  Instead, the first one (allocated
with the `astart` keyword) is created on its own new very light weight stack frame.
Each additional async call uses the callers stack frame for parameters and local
variables.  This reduces the amount of garbage and eliminates the need for ValueTask.

