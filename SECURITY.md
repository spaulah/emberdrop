# Security policy

## Supported versions

Emberdrop is pre-release (v0.1). Only the latest commit on `main` is supported; fixes are not backported.

## Reporting a vulnerability

Please do not open a public issue for a security problem. Use GitHub's private vulnerability reporting instead: on the repository page, open the **Security** tab and choose **Report a vulnerability**.

Include what you found, how to reproduce it (a package or request that triggers it is ideal), and what you think the impact is. This is a one-person project, so replies are best effort, but every report will be read and you will be told what was decided.

## What counts as a vulnerability

Emberdrop loads plugins as ordinary .NET code in the host process, and says so plainly ([known limits](docs/engineering/known-limits.md#it-is-not-a-security-sandbox)). That sets the boundary:

**In scope** (the code that handles a package *before* it is trusted):

- Escaping the extraction directory with a crafted archive (zip-slip, symbolic links, absolute or odd paths).
- Getting past the extraction limits (entry count, size, expansion ratio) so that an archive exhausts memory or disk.
- Getting a package past the optional SHA-256 allowlist, or having it loaded after it was rejected.
- A crafted manifest or package that crashes the host or corrupts what is installed.
- Injection or path traversal in the REST API.

**Out of scope** (documented limits, not vulnerabilities):

- A plugin doing something harmful once it is loaded. Plugins run with the host's permissions; load only packages you trust and restrict who can write to the watched folders.
- Declared `capabilities` not being enforced. They are informational in v0.1.
- The reference host's REST API having no authentication. It is documented as unsuitable for untrusted networks.
- A plugin that prevents its own unload, or code that ignores cancellation. These are reported by the host, not prevented.

If you are unsure which side of the line something falls on, report it privately and it will be sorted out there.
