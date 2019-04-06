# Stack Frames

The execution stack is not accessible by WebAssembly, so two shadow stacks 
are maintained in linear memory.  

The first stack, `SCATCH_STACK`, is scratch memory for anything that can't
live on the execution stack, such as `stackalloc` objects, interior references,
or any addressable object requiring temporary linear memory.  This stack is
not scanned for GC references.

The second stack, `GC_STACK`, is for GC object references, try-finally cleanup
functions, stack trace information, and optional debug information.  This stack
is scanned for GC references and may never hold an interior reference.  Once
an object is on this stack, it is GC pinned and no longer needs to be tracked.

Let's consider some examples.

This function uses no linear stack memory because no exceptions need to be caught,
there are no garbage collected references, and all temprary variables can live on
the execution stack.

    func a(b float64, c float64, d float64) float64 {
        // There could be a lot of other temporary variables here.
        // MyFunc1, MyFunc2, or MyFunc3 could throw an exception
        #myTemp1 = MyFunc1(b,c);
        #myTemp2 = MyFunc2(c,d);
        return MyFunc3(myTemp1, myTemp2, b, c, d);
    }

Likewise, this function uses no linear memory stack.  `b` must have already
been placed on `GC_STACK` so it doesn't need to be stored again. Furthermore, 
the `ref b[10]` is a reference to an object that was pinned by the calling
function, so it is also does not need to live on `GC_STACK`.

    func a(b []int) float64 {
        return MyFunc1(b, ref b[10]);
    }

This function requires `SCRATCH_STACK` because `d` must be copied
into memory and its address passed as a parameter.  

    func a(b float64) float64 {
        return MyFunc1(ref b);
    }

This function requires `GC_STACK` because the reference needs to be
stored so the GC doesn't collect it.  An optimizer coudl remove the
need for `GC_STACK` by proving that no GC could happen in 
`DoSomethingThatCouldTriggerGC`

    func a() string {
        #myTemp = MyStringFunction();
        DoSomethingThatCouldTriggerGC();
        return myTemp;
    }

This function uses `GC_STACK` to allow the `finally` clause to cleanup
when the stack is unwound.  Note that this type of code is implicitly
generated when `lock`, `use`, or `defer` is used.

    func b() string {
        try { return DoSomethingThatCouldThrow(a);  }
        finally  { DoSomething(); }
    }

This function requires execution to continue after an exception is caught.
In this case, the execution stack must not be unwound. Instead, the `try` clause
invokes JavaScript code that can handle the exception and return control back to the 
calling function.  Using `try` and `finally` are not expensive, but catching an
exception and swallowing it is.

    func b() string {
        try { return DoSomethingThatCouldThrow(a);  }
        catch  { LogItAndThenIgnore() } 
    }


## Task Stack Frame Layout

Task stack frames are used to limit the amount of garbage created during async
calls.  The also speed up async function execution.

TBD: describe them.