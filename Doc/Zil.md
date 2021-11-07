# Zurfur Intermediate Language (ZIL)


A `.zil` file is a `.zip` file with a defined structure containing ZIL and
other resources needed to compile a Zurfur package for distribution.  This
format allows quickly loading a subset of the package (i.e the header file)
so other packages can compile without reading the entire source code.

Most of the rest of this document will ignore that the files are zipped, and
assume they are files and subdirectories.

## Directory Layout

The top level directory contains:

* Info.json: Information about the author, project, compiler, etc.
* Manifest.json: Optional list of all files (used for signing)
* Package directories: One for each top level package (`com.MyDomain.MyProject`, etc.)

## Package Directory Layout

* Header.json: Header file, stripped of all private symbols.
* HeaderAll.json: Superset of Header.json, containing all symbols.
* Code.txt: Source code in text format (Zurfur intermediate language)
* Resources: Directory holding project resources (images, text files, translations, data, etc.)

## Header and HeaderAll

The `Header.json` file contains only public symbols needed by other packages
so they can be compiled independently.  For speed, it might not be compressed.
For verification, every symbol in the header must have an exact match in
`HeaderAll.json`

The `HeaderAll.json` file contains all public and private symbols in the
package.  It is an exact superset of `Header.json`.

## Code

The `Code.txt` file contains Zurfur intermediate language.  TBD


