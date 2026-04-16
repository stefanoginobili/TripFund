# Task 09: Microsoft OneDrive Remote Storage Implementation

Implement the `OneDriveRemoteStorageService` to enable offline-first synchronization with Microsoft OneDrive, following the architecture defined in `ARCHITECTURE.md`.

## Context & Objectives
- **Target Service**: `src/TripFund.App/Services/OneDriveRemoteStorageService.cs`
- **Objective**: Implement `IRemoteStorageService` for Microsoft OneDrive.
- **Protocol**: Use Microsoft Graph REST API (v1.0).
- **Credentials**: Use `MicrosoftClientId` and `MicrosoftTenantId` from `src/TripFund.App/Services/MicrosoftAuthConfiguration.cs`.

## Functional Requirements

### 1. Authentication & Permissions
- Implement OAuth 2.0 PKCE flow using `WebAuthenticator`.
- **Permission Scopes**: Request `Files.ReadWrite` and `offline_access`.
- **Token Persistence**: Persist the `refresh_token` in `known_trips.json` to enable background sync without repeated login prompts.
- **Workflow-Specific Checks**:
  - **Create New Trip**: Verify **Read/Write** permissions on the target folder.
  - **Add Existing Trip**: Verify at least **Read** permissions. If the folder is shared as read-only, set the `readonly` flag in the `RemoteStorageConfig` to `true`.

### 2. Folder Selection & Drive Context
- Implement a custom Blazor-based folder picker (`OneDrivePickerModal.razor`).
- Support both **"I miei file"** (Personal Drive) and **"Condivisi con me"** (Shared Drives).
- Capture and persist the `driveId` for shared items to ensure correct API endpoint usage (`/drives/{driveId}/items/{id}`).

### 3. Synchronization Logic (`SynchronizeAsync`)
Follow the versioned storage engine logic:
- **Config Sync**: Ensure local and remote `config_versioned/` are consistent using the folder-based sync logic.
- **Transactions Sync**:
  - Traverse `transactions/` folder recursively.
  - Identify version folders: `transactions/{transactionId}/details_versioned/{NNN_kind_deviceId}/`.
  - **Download**: Fetch remote versions missing locally.
  - **Upload**: Push local versions missing remotely (unless `readonly` is true).
- **Conflict Detection**: Use `VersionedStorageEngine.GetLatestVersionFolder` to identify diverging states. Update `hasConflicts` in `known_trips.json`.

### 4. Media & Attachments
- Sync binary files (photos, receipts) in each transaction version.

## Technical Constraints
- **Offline-First**: Never block the UI. Sync must happen in the background or when triggered via the UI.
- **Error Handling**: Gracefully handle network timeouts and HTTP 400/401 errors. Ensure path-based requests use trailing colons (`:/`) per Graph API standards.
- **Mocking for Tests**: Use `WireMock.Net` to simulate Microsoft Graph API responses.

## Validation Criteria
1. Successfully authenticates and retrieves folder content from both personal and shared drives.
2. Correctly handles folder browsing and selection within the app UI.
3. Automatically sets `readonly: true` in `known_trips.json` if the user lacks write permissions.
4. Synchronizes JSON files and binary attachments between local storage and OneDrive.
5. Persists authentication state across application restarts.
