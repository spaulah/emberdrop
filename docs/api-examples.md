# API Endpoints Usage Examples

This document demonstrates how to use the Emberdrop Server WorkUnitsController API endpoints.

## Base URL

```
https://localhost:5001/api/work-units
```

## Endpoints

### 1. Get All Work Units

**GET** `/api/work-units`

**Response:**

```json
{
  "success": true,
  "data": [
    {
      "workUnitId": 1,
      "identifier": "heartbeat-12345",
      "name": "Heartbeat Work Unit",
      "type": "HeartbeatWorkUnit",
      "schedule": "0 */30 * * * *",
      "input": null,
      "enabled": true,
      "createdAt": "2025-08-03T10:00:00Z",
      "updatedAt": "2025-08-03T10:00:00Z"
    }
  ],
  "message": "Work units retrieved successfully",
  "errors": []
}
```

### 2. Get Work Unit by ID

**GET** `/api/work-units/{id}`

**Example:** `GET /api/work-units/1`

**Response:**

```json
{
  "success": true,
  "data": {
    "workUnitId": 1,
    "identifier": "heartbeat-12345",
    "name": "Heartbeat Work Unit",
    "type": "HeartbeatWorkUnit",
    "schedule": "0 */30 * * * *",
    "input": null,
    "enabled": true,
    "createdAt": "2025-08-03T10:00:00Z",
    "updatedAt": "2025-08-03T10:00:00Z"
  },
  "message": "Work unit retrieved successfully",
  "errors": []
}
```

### 3. Create New Work Unit

**POST** `/api/work-units`

**Request Body:**

```json
{
  "identifier": "backup-work-unit",
  "name": "Database Backup Work Unit",
  "type": "DatabaseBackupWorkUnit",
  "schedule": "0 0 2 * * *",
  "input": "{\"backupPath\": \"/backups/daily\", \"retentionDays\": 30}",
  "enabled": true
}
```

**Response (201 Created):**

```json
{
  "success": true,
  "data": {
    "workUnitId": 2,
    "identifier": "backup-work-unit-67890",
    "name": "Database Backup Work Unit",
    "type": "DatabaseBackupWorkUnit",
    "schedule": "0 0 2 * * *",
    "input": "{\"backupPath\": \"/backups/daily\", \"retentionDays\": 30}",
    "enabled": true,
    "createdAt": "2025-08-03T11:00:00Z",
    "updatedAt": "2025-08-03T11:00:00Z"
  },
  "message": "Work unit created successfully",
  "errors": []
}
```

### 4. Update Existing Work Unit

**PUT** `/api/work-units/{id}`

**Example:** `PUT /api/work-units/2`

**Request Body:**

```json
{
  "identifier": "backup-work-unit",
  "name": "Enhanced Database Backup Work Unit",
  "type": "DatabaseBackupWorkUnit",
  "schedule": "0 0 1 * * *",
  "input": "{\"backupPath\": \"/backups/daily\", \"retentionDays\": 45, \"compressionEnabled\": true}",
  "enabled": true
}
```

**Response:**

```json
{
  "success": true,
  "data": {
    "workUnitId": 2,
    "identifier": "backup-work-unit-67890",
    "name": "Enhanced Database Backup Work Unit",
    "type": "DatabaseBackupWorkUnit",
    "schedule": "0 0 1 * * *",
    "input": "{\"backupPath\": \"/backups/daily\", \"retentionDays\": 45, \"compressionEnabled\": true}",
    "enabled": true,
    "createdAt": "2025-08-03T11:00:00Z",
    "updatedAt": "2025-08-03T11:15:00Z"
  },
  "message": "Work unit updated successfully",
  "errors": []
}
```

### 5. Delete Work Unit

**DELETE** `/api/work-units/{id}`

**Example:** `DELETE /api/work-units/2`

**Response:**

```json
{
  "success": true,
  "data": null,
  "message": "Work unit deleted successfully",
  "errors": []
}
```

### 6. Get Available Work Unit Types

**GET** `/api/work-units/types`

**Response:**

```json
{
  "success": true,
  "data": [
    {
      "name": "HeartbeatWorkUnit",
      "fullName": "SamplePlugin.HeartbeatWorkUnit",
      "assemblyName": "SamplePlugin",
      "isFromPlugin": true
    },
    {
      "name": "DatabaseBackupWorkUnit",
      "fullName": "MyCompany.Plugins.DatabaseBackupWorkUnit",
      "assemblyName": "MyCompany.Plugins",
      "isFromPlugin": true
    },
    {
      "name": "FileCleanupWorkUnit",
      "fullName": "MyCompany.Plugins.FileCleanupWorkUnit",
      "assemblyName": "MyCompany.Plugins",
      "isFromPlugin": true
    }
  ],
  "message": "Work unit types retrieved successfully",
  "errors": []
}
```

### 7. Work unit fields that come from the plugin

Work units that belong to a plugin also return what the plugin's manifest and types describe (`GET /api/work-units`, `GET /api/work-units/{id}`):

```json
{
  "workUnitId": 4,
  "name": "greet",
  "pluginId": "sample-plugin",
  "kind": "work-unit",
  "description": "Greets someone by name.",
  "capabilities": [],
  "inputSchema": { "type": "object", "properties": { "name": { "type": "string" } }, "required": [ "name" ], "additionalProperties": false },
  "outputSchema": { "type": "object", "properties": { "message": { "type": "string" } }, "required": [ "message" ], "additionalProperties": false },
  "declaredTriggers": [ "manual" ],
  "triggers": [ { "triggerId": 1, "kind": "recurring", "definition": "recurring:0 9 * * *", "enabled": true, "createdAt": "2026-11-08T16:14:41Z" } ]
}
```

`declaredTriggers` are the defaults from the plugin's manifest; `triggers` are the ones an operator added. `capabilities` are informational in v0.1 (see [Capabilities](concepts/capabilities.md)).

### 8. Triggers

- **GET** `/api/work-units/{id}/triggers`: the triggers an operator added.
- **POST** `/api/work-units/{id}/triggers`: add one. Body `{ "kind": "recurring", "cron": "0 9 * * *" }`. v0.1 stores recurring triggers only (`400` with an explanation otherwise).
- **DELETE** `/api/work-units/{id}/triggers/{triggerId}`.

### 9. Start an execution

**POST** `/api/work-units/{id}/executions`

```json
{ "input": { "name": "ada lovelace" }, "runAt": "2030-01-01T09:00:00Z" }
```

Both properties are optional. With no `runAt` the execution starts now with a manual trigger; with `runAt` it is a one-off trigger. `input` replaces the stored input for this execution and is read strictly as the work unit's input type.

**Response (202):** `{ "success": true, "data": { "executionId": "…", "trigger": "manual" } }`.

`400` if the input does not fit (the message says why), `409` if the work unit cannot run right now (its plugin is not loaded, a setting is missing), `501` if the scheduler cannot do delayed runs.

### 10. List executions

**GET** `/api/scheduler/executions?skip=0&take=50` lists finished executions, newest first: `executionId`, `state`, `workUnitType`, `workUnitId`, `trigger` (the token of the trigger that caused it), `finishedAt`, `error`.

## Error Responses

### 400 Bad Request

```json
{
  "success": false,
  "data": null,
  "message": "Invalid request data",
  "errors": [
    "The Identifier field is required.",
    "The Name field is required.",
    "The Type field is required."
  ]
}
```

### 404 Not Found

```json
{
  "success": false,
  "data": null,
  "message": "Work unit with ID 999 not found",
  "errors": []
}
```

### 500 Internal Server Error

```json
{
  "success": false,
  "data": null,
  "message": "Internal server error",
  "errors": ["Database connection failed"]
}
```

## cURL Examples

### Get all work units

```bash
curl -X GET "https://localhost:5001/api/work-units" \
  -H "Accept: application/json"
```

### Create a new work unit

```bash
curl -X POST "https://localhost:5001/api/work-units" \
  -H "Content-Type: application/json" \
  -d '{
    "identifier": "test-work-unit",
    "name": "Test Work Unit",
    "type": "TestWorkUnitType",
    "schedule": "0 */5 * * * *",
    "enabled": true,
    "input": "{\"description\": \"Test work unit\"}"
  }'
```

### Update a work unit

```bash
curl -X PUT "https://localhost:5001/api/work-units/1" \
  -H "Content-Type: application/json" \
  -d '{
    "identifier": "updated-test-work-unit",
    "name": "Updated Test Work Unit",
    "type": "TestWorkUnitType",
    "schedule": "0 */10 * * * *",
    "enabled": false,
    "input": "{\"description\": \"Updated test work unit\"}"
  }'
```

### Delete a work unit

```bash
curl -X DELETE "https://localhost:5001/api/work-units/1"
```

### Get available work unit types

```bash
curl -X GET "https://localhost:5001/api/work-units/types" \
  -H "Accept: application/json"
```

## Notes

- All timestamps are in UTC format
- The `identifier` field is automatically suffixed with a GUID when creating work units
- The `schedule` field uses cron expression format (6 fields: seconds, minutes, hours, day, month, day-of-week)
- The `input` field accepts a JSON string that is passed to the work unit as its input
- Work unit types are discovered automatically from assemblies implementing `IWorkUnit`
- Plugin-based work unit types are loaded from the `PluginAssemblyCache` for performance
