# Capabilities

A work unit can declare `capabilities` in its manifest: a list of strings that say what it needs, for example `network:smtp`, `fs:read` or `service:Acme.Shared.IClock`.

## What v0.1 does with them, honestly

| Done | Not done |
| --- | --- |
| **Validated.** Each string must be 1 to 200 letters, digits or `: . - _ /`, starting with a letter or digit. A package with a malformed capability is rejected. | **Not enforced.** Plugins run in the host's process with its full trust. A work unit that declares `network:smtp` can open any socket; one that declares nothing can too. |
| **Stored.** The list is saved with the work unit (`Capabilities`, a JSON array) and refreshed from the newest plugin version, like the other manifest-described fields. | **Not a sandbox, not a permission prompt, not an audit.** Nothing compares what the code does with what it declared. |
| **Shown.** The API returns them as `capabilities` on the work unit. | **Not interpreted**, apart from the one prefix below. |
| **Checked against a host allow-list, for services only.** A capability that starts with `service:` names a host service (`service:Full.Type.Name`). The type must be on `PluginHostOptions.HostServices` (the services the host offers plugins); otherwise the package is rejected with a message that names the work unit and the service. | A `service:` capability does not *grant* the service: a plugin gets exactly the services on the allow-list, whether it declares them or not. The check only catches a plugin that expects a service this host does not offer. |

The declarations are useful as documentation for operators, as a place for tooling to build on, and as the shape a future enforcement would take. Do not treat them as a security boundary. For hostile plugins you need process isolation, which Emberdrop does not provide ([known limits](../engineering/known-limits.md)).
