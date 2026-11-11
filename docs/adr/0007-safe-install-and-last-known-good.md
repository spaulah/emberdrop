# ADR 0007: Install atomically, and never let a bad package disturb a good one

- Status: accepted
- Date: 2026-11-07

## Context

Packages arrive by being copied into a folder while the server runs. They can be half written, corrupt, hostile, or simply broken, and a replacement for a running plugin must not be able to take the running one down. The first installer processed a file after a fixed one-second pause, extracted straight to its final location, handled the same file from several concurrent events, and unloaded the old version before it knew the new one worked.

## Decision

**A file is processed only when it is complete.** Size and modification time must be unchanged across consecutive checks, and the file must open exclusively (a writer that still holds it makes that fail). The several events one copy raises are merged into one piece of work by a pending-path set, and one installer handles one package at a time.

**Nothing is extracted into its final place.** A package is extracted into a staging directory on the same volume, validated, and promoted with a single directory rename. A crash leaves only staging debris, removed at startup.

**Extraction does not trust the archive.** Every entry name is checked before any byte is written (absolute paths, `..`, backslashes, symbolic links, duplicates, anything that resolves outside the target). Entry count, declared size and the actual bytes written (against a maximum expansion ratio relative to the file's real size, which cannot be forged) are limited.

**Integrity is opt-in.** An empty SHA-256 allowlist means no check. With entries, anything else is quarantined, and the reason names the hash. Signing is deliberately out of scope.

**Last known good.** Whatever is active when a package arrives stays active unless the package is proven to activate:

- A *different* version is activated beside the old one; a failure cannot affect it.
- A replacement for the *same* version is first loaded into a throwaway context (entry assemblies loaded, module initializers run, work units resolved, services built) and only then does the installed copy get retired.
- "Activate" includes everything that can fail on first use: loading the entry assemblies *and running their module initializers*, resolving every declared work unit, and building the service provider. Failures there would otherwise surface as failed work unit runs.

**Rejection is uniform.** A rejected package is moved to quarantine with a reason file listing every problem, and the version is visible in the API as `Quarantined`. Reasons carry the root cause (inner exceptions are rendered into the text), because the outermost message of a reflection or type-initializer failure rarely says what went wrong.

**Old versions are archived with a retention limit**, so a rollback is possible and the disk does not fill.

## Alternatives rejected

**Keep the fixed delay.** Wrong for large files and slow shares; right only by luck.

**Extract in place and delete on failure.** A crash or a reader in between sees a half-installed plugin.

**Retire the old version, then try the new one, and roll back if it fails.** The window between the two is an outage, and a rollback of a version whose work units just stopped is not the same as never stopping them. Proving first costs one extra load of the package, only for same-version replacements.

**Trust headers for sizes.** Archive headers are attacker-controlled; counting the bytes written is not.

**Quarantine only invalid packages and leave runtime failures to the engine.** A package that cannot activate is invalid by any definition an operator cares about.

**Make the allowlist mandatory.** Most uses are a trusted folder on a trusted machine; the cost of mandatory hashing is paid by every user to guard against a threat only some have.

## Consequences

- A same-version replacement runs the plugin's startup code twice (once in the discarded context); documented in the known limits.
- The pipeline is a handful of small, separately tested pieces rather than one large class: stable-file detector, extractor, hasher, archive, pending-path set, installer, watcher.
- Quarantine is write-only until someone cleans it. That is intentional: it is evidence.
