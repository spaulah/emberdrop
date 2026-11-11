# ADR 0002: Load plugin assemblies from memory, not from their files

- Status: accepted
- Date: 2026-11-07

## Context

Emberdrop must be able to replace, archive and delete a plugin's files while the process runs. On Windows, an assembly loaded with `LoadFromAssemblyPath` keeps its file open (it is memory-mapped), so the file cannot be deleted or overwritten until the context unloads, and even then only if the unload succeeds. On Linux and macOS the file can be unlinked but the mapping keeps the old contents alive. A loader that holds file locks turns every update into a potential failure with a half-installed plugin.

## Decision

Managed assemblies (the entry assemblies and every private dependency) are read into memory and loaded with `AssemblyLoadContext.LoadFromStream`. The file is read once and closed. If a `.pdb` sits next to the assembly it is loaded the same way so stack traces keep line numbers.

Packages are still extracted into a versioned directory first. The directory is the source of truth for the bytes, for `deps.json` resolution and for native libraries (see below). It can be deleted after unload, and, because nothing is held open, deleting it earlier does not fail either.

## Alternatives rejected

**`LoadFromAssemblyPath` from an extracted versioned directory.** Simpler and keeps `Assembly.Location` meaningful. Rejected because of the Windows locking behavior above; "unload first, then delete" would turn every replacement into an ordering problem, and a failed unload would block the delete.

**Shadow-copying the whole package to a temporary directory per load.** Avoids the lock on the *installed* files but moves the problem: the temporary copy is locked instead and must be cleaned up later, possibly after a crash. It also doubles disk usage.

**Memory-mapped views we control.** More code for no benefit over a byte array.

## Consequences

- No file of a loaded plugin is locked by Emberdrop's managed loading. Tests check that `Assembly.Location` is empty (proof the assembly did not come from a path) and that the directory can be deleted while the plugin is loaded.
- Memory use is roughly the size of the plugin's assemblies, held for as long as the version is loaded, plus the loaded image itself.
- `Assembly.Location` is an empty string for plugin assemblies. A plugin that reads files next to its own assembly through `Location` will not find them; it must use a path it is given. This is documented in the plugin guide.
- **Native libraries are the exception.** Native code can only be loaded from a path, so a plugin that ships a native library keeps that file locked on Windows until the context is collected, and native libraries cannot always be unloaded. See [known limits](../engineering/known-limits.md).
- Lazily loaded private dependencies are read when first needed, so the extracted directory must exist for as long as the version is loaded. Deleting it early is safe for already-loaded code but breaks a dependency that has not been touched yet.
- The test that proves "no locks" can only fully prove it on Windows, where the locks exist. On other platforms it proves the mechanism (no path-based load) rather than the symptom.
