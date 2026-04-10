# Task 09: Google Drive Remote Storage Implementation

Implement the `GoogleDriveRemoteStorageService` to enable offline-first synchronization with Google Drive, following the architecture defined in `ARCHITECTURE.md`.

## Context & Objectives
- **Target Service**: `src/TripFund.App/Services/GoogleDriveRemoteStorageService.cs`
- **Objective**: Implement `IRemoteStorageService` for Google Drive.
- **Protocol**: Use Google Drive REST API (v3).
- **Credentials**: Use `ClientId` and `ClientSecret` (if applicable) from `src/TripFund.App/Config.cs`.

## Functional Requirements

### 1. Authentication & Permissions
- Implement OAuth2 flow using the system browser or native Google Sign-In.
- **Permission Scopes**: Request `https://www.googleapis.com/auth/drive.file` or specific folder access.
- **Workflow-Specific Checks**:
  - **Create New Trip**: Verify **Read/Write** permissions on the target folder.
  - **Add Existing Trip**: Verify at least **Read** permissions. If the folder is read-only (e.g., shared with the user without edit rights), set the `readonly` flag in the `RemoteStorageConfig` to `true`.

### 2. Folder Resolution
- Users provide a URL like `https://drive.google.com/drive/folders/{folderId}`.
- Parse the `folderId` from the URL to use as the root for sync operations.

### 3. Synchronization Logic (`SynchronizeAsync`)
Follow the versioned storage engine logic:
- **Metadata Sync**: Compare local and remote `metadata/`.
- **Transactions Sync**:
  - Traverse `transactions/` folder recursively.
  - Identify version folders: `transactions/{transactionId}/`.
  - **Download**: Fetch remote versions missing locally.
  - **Upload**: Push local versions missing remotely (unless `readonly` is true).
- **Conflict Detection**: After sync, identify if multiple device versions exist for the same transaction/metadata. Update `hasConflicts` in `known_trips.json`.

### 4. Media & Attachments
- Sync files in each transaction version.

## Technical Constraints
- **Offline-First**: Never block the UI. Sync must happen in the background or when triggered via `RemoteStorageStatusBar`.
- **Error Handling**: Gracefully handle network timeouts and expired auth tokens (implement refresh token logic).
- **Mocking for Tests**: Use `WireMock.Net` to simulate Google Drive API responses in `tests/TripFund.Tests/Services/GoogleDriveRemoteStorageTests.cs`.

## Validation Criteria
1. Successfully authenticates and retrieves folder content.
2. Correctly parses `folderId` from Drive URLs.
3. Automatically sets `readonly: true` in `known_trips.json` if the user lacks write permissions to the folder.
4. Synchronizes JSON files and binary attachments between local storage and Google Drive.
5. Passes all integration tests with simulated API failures.
