# Func and Func Pointers

Whenever you see `func` without an `*`, think c# style delegates.  The
delegate lives on the heap and can optionally carry the `this` of any
object. Whenever you see `*func`, think C style function pointer.  The 
function pointer carries no information other than the entry point to a
function with its type signature.

In general, `func` and delegates are the way to go.  Lambdas are implemented
using delegates.  They provide the most flexibility and are quite light weight, 
fast, and efficient.  In fact, invoking a delegate is faster than invoking
a virtual on an unsealed class.  The one downside is that delegates live on
the heap and require a dynamic allocation.

When the need for absolute raw speed arises, the function pointer wins.
It carries no extra information beyond the entry point to a function with
its signature type.  It is a value type and doesn't require the use of
a heap memory allocation.  Function pointers are used to implement
delegates, virtual functions, and interfaces.


