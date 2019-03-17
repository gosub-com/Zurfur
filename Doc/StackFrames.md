# Stack Frames

The execution stack is not accessible by WebAssembly, so a shadow stack is
maintained in linear memory.  All addressable objects, garbage collected
references, and "try-finally" cleanup functions are placed on the stack.

There are three ways stack memory can be laid out:  1) No stack,
2) Just scratch memory, 3) STable that contains the stack
length, optional GC reference info, and optional cleanup
function.

For #1, consider:

    func a(b float64, c float64, d float64) float64 {
        #myTemp = MyFunction(b,c);
        return myTemp*d;
    }

No linear memory stack is used, no exceptions need to be caught,
and there are no grabage collected references.

For #2, consider:

    func a(b float64, c float64, d float64) float64 {
        #myTemp = MyFunction(b,c, **ref** d);
        return myTemp*d;
    }

Addressable linear memory must be used, so a stack frame is setup that
contains just scratch memory.  The stack setup looks like this:

    STACKTOP -= size of scratch memory;
    *STACKTOP = size of scratch memory (and a bit to say this is scenario #2)

Now the stack can be traced by the garbage collector or un-wound by
an exception handler.  MyFunction can throw an exception and everything
works as expected.

For #3, consider either of the following:

    func a() string {
        #myTemp = MyStringFunction();
        DoSomethingThatCouldTriggerGC();
        return myTemp + ": Done;
    }
    func b() string {
        try { return DoSomethingThatCouldThrow(a);  }
        finally  { DoSomething(); }
    }

The first function needs to store a reference, and the second function needs
to DoSometing when there is an exception.  Note that this type of code is
implicitly generated when `lock`, `use`, or `defer` is used.
The stack setup code looks like this:

    STACKTOP -= size of scratch memory
    *STACKTOP = STable (and a bit to say this is scenario #3)

The STable is a compiler generated varible length struct that looks like this:

    struct STable {
        lengthOfStackFrame int // Plus several bits for optional fields
        objectRefLayout *RefLayout  // Null if stack frame doesn't hold references
        cleanup func(); // Null if the function doesn't need cleanup (i.e. `finally`)
        ... Other compiler generated info
    }

Scenario #3a: There is one last scenario, which is that the exception is caught, 
dealt with, and execution continues. 

    func b() string {
        try { return DoSomethingThatCouldThrow(a);  }
        catch  { LogItAndThenIgnore() } 
    }


In that case, the execution stack must not be unwound. Instead, the `try` clause
invokes JavaScript code that can handle the exception and return control back to the 
calling function.  Using `try` and `finally` are not expensive, but catching an
exception and swallowing it is.

## Stack Frame Layout

TBD: Complete description of how stack frames are laid out, how the GC walks it
to track references, and how exception finally clauses are stored

## Task Stack Frame Layout

Task stack frames are used to limit the amount of garbage created during async
calls.  TBD: describe them here.