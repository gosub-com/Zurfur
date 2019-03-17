# Class Objects

Like in C#, all `class` objects live on the heap.

An object has a VTable and optionally a length of elements:

    struct Object {
        VT *VTable;   // All heap objects
        Length int;   // Only when inheriting from VariableSizedObject
    }

The VTable contains the object size and reference layout:

    struct VTable {
        SizeOfObject int             // Size of object, not including array elements
        SizeOfElement int            // Size of element, or zero when not variable sized
        ObjectRefLayout *RefLayout   // Null if object doesn't contain references
        ElementRefLayout *RefLayot   // Null if elements don't contain references
        // Other information (type, virtual functions, etc.)
    }

All objects are a multiple of 64 bits, and the size of A can be calculated with
`(A.VT.SizeOfObject + ((A.VT.SizeOfElement*A.Length+7)&~7)`.
