# Zurfur Intermediate Language (ZIL)


A `.zil` file is a `.zip` file with a defined structure containing ZIL and
other resources needed to compile a Zurfur package for distribution.  This
format allows quickly loading a subset of the package (i.e the header file)
so other packages can compile without reading the entire source code.

Most of the rest of this document will ignore that the files are zipped, and
assume they are files and subdirectories.

## Directory Layout

The top level directory contains:

* Info.json - Information about the author, project, compiler, etc.
* Manifest.json - Optional list of all files (used for signing)
* Namespace directories - One for each top level namespace (`com.MyDomain.MyProject`, etc.)

Top level namespace directories contain:

* Header.json - Header file, stripped of all private symbols.
* Code.json - All symbols (both public and private) and code
* Resources - Directory holding project resources (images, text files, translations, data, etc.)

**TBD:** Describe Info.json and Manifest.json

## Header.json

The `Header.json` file contains only public symbols needed by other packages
so they can be compiled independently.  For speed, it should not be compressed.
For verification, every symbol in the header must have an exact match in `Code.json`

**TBD**: Describe json layout

## Code.json

The `Code.json` file contains all symbols (public and private) and all code
needed to compile the package.

**TBD:** Describe json layout and Zil code format

