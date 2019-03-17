# Multithreading

The first version of Zurfur is aimed at WebAssembly and will
not support any kind of multithreading within an instance
of the WebAssembly module.  The `lock` keyword is reserved 
for future use.  One thing that won't ever be supported is
locking on any random object.  There will be a `Mutex` object 
for that purpose.

## Multithreading via Web Workers

Multithreading can be achieved using Web Workers.  To support that
option, there will be a quick easy way to serialize object graphs and
transport them to other Web Assembly instances.  

This is the method we will use to multi-thread the compiler in WebAssembly.
One benefit of doing it this way is that each module has its own memory
space, so the garbage collector will have a smaller working set
of objects to reclaim for each instance.



