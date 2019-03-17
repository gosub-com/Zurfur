# Simple Intermediate Language (SIL)

SIL is going to be the simplest intermediate language I can come up with.

TBD: Document SIL here.

## Header SIL

The first part of the SIL file is the header type information.
This should be quickly generated for each file, and then
the type information can be used while compiling the other files.

## High Level SIL

High level SIL preserves all type information and code.  It has
no optimizations.

## Low Level SIL

Low level SIL has all type information stripped out, is optimized, and is
ready to be transformed into web assembly code.

LL SIL has only the following types: `i8`, `i16`, `i32`, `i64`, and 
`intptr` which is either 32 or 64 bits depending on the machine architecture.  
Every other type, including `float` and `uint` is supplied by libraries.

TBD: Describe more here

