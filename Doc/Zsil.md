# Zurfur Simple Intermediate Language (ZSIL)

ZSIL is modeled after a combination of CIL and WebAssembly, but aims
to be more basic than CIL and richer than WebAssembly.  From CIL
we will retain naming conventions and type metadata, but drop most
of the IL instructions in favor of function calls.  From WebAssembly
we will retain flow control structure.

ZSIL is designed to be platform independent, whether it be 32 bit,
64 bit, or even x86.  It is not meant to be efficient or run directly
on any platform, but should be easy to convert to WebAssembly.
ZSIL has an Application Binary Interface (ABI), but it also
is not meant to match the ABI of the target platform.

ZSIL will be transformed into ZSILOBJ, which is just a very much
stripped down version of ZSIL using only machine native types.
ZSILOBJ is target specific, and must only be linked with modules
compiled on the same platform, the same optimizations, the same build
options, and even the same compiler. 

#### ABI Notes

ZSIL pushes the parameters of the function in the order they are encountered
and passes `struct`s on the stack by value. 

The first version of ZSILOBJ is expected to pass `struct`'s by reference, unless
the struct is a single field that can fit in a machine register in which case
it is passed by copy.  Future versions may pass small `struct`s by value
and bigger ones by reference, whichever is more efficient.  

#### ZSIL Instruction Set

Instruction | Description
--- | ---
ldl/ldla|Load local (address)
ldp/ldpa|Load parameter (address)
ld.i32/ld.i64|Load constant int 32/64 bits
ld.f32/ld.f64|Load constant float 32/64 bits
ld.str|Load string
block|Block (br branches down, like break)
loop|Loop (br branches up, like continue)
end|End of Block/Loop/If
br|Branch
br_if|Branch conditional
return|Return
throw|Throw
calls|Call static function
callv|Call virtual function
calli|Call indirect
drop|Drop # of stack objects

#### Examples

Example: `if a() { DoIf() } else { DoElse() }`

    block 0
        block 1
            call a
            call not
            br_if 1
            call DoIf
            br 0
        end 1
        call DoElse
    end 0        

Example: `if a() || b() { DoIf() } else { DoElse() }`

    block 0
        block 1
            block 2
                call a
                br_if 2
                call b
                call not
                br_if 1
            end 2
            call DoIf
            br 0
        end 1
        call DoElse
    end 0


Example of setting a member of a struct `shape.l1.p1.y = 0`:

    struct Point { x int; y int; z int }
    struct Line { p1 Point; p2 Point }
    class Shape { l1 Line }

    // Zurfur Code
    @shape = Shape()
    shape.l1.p1.y = 0;

    // ZSIL
    ldl shape
    dup
    call get_l1
    dup
    call get_p1
    ld 0
    call set_y
    call set_p1
    call set_l1

    // Getter definitions
    func get_l1(Shape) Line
    func get_p1(Line) Point
    func get_y(Point) int

    // Setter definitions
    func set_y(Point, int) Point
    func set_p1(Line, Point) Line
    func set_l1(Shape, Line) Shape


### Header SIL

The first part of the SIL file is the header type information.
This should be quickly generated for each file, and then
the type information can be used while compiling the other files.

### High Level SIL

High level SIL preserves all type information and code.  It has
no optimizations.

### Low Level SIL

Low level SIL has all type information stripped out, is optimized, and is
ready to be transformed into web assembly code.

LL SIL has only the following types: `i8`, `i16`, `i32`, `i64`, and 
`intptr` which is either 32 or 64 bits depending on the machine architecture.  
Every other type, including `float` and `uint` is supplied by libraries.

TBD: Describe more here

