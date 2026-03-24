# Architecture & Data Flow

## Tech Stack
- **Framework:** .NET MAUI Blazor Hybrid (HTML/CSS/C#).
- **Storage:** Local File System (Offline-first).
- **Sync Backend:** Google Drive API.

## Data Structure Philosophy
- Use standard, human-readable JSON.
- Prefer `Dictionary<string, object>` over arrays for collections to make lookups and merge operations easier via keys.

## Storage & Sync Logic
All data is stored locally. When online, the app syncs with Google Drive.

### Google Drive Integration
- **New Trip:** App creates a new folder in a user-selected Google Drive directory. Generates a slug-based name (editable).
- **Join Trip:** User selects an existing shared folder in Drive. The user selects their profile from the existing trip members to link local actions to their user ID.
- **Permissions:** Rely entirely on Google Drive folder permissions. Unhandled API permission errors (e.g., 403 Forbidden) should gracefully show a standard error to the user without altering the UI beforehand.

### Conflict Resolution & File System
Transactions are **append-only** to minimize conflicts and preserve a history of changes.
- **Structure:** `[TripFolder]/Transactions/[yyyyMMddTHHmmssZ-guidprefix]/[Version]_[UserSlug]/`
- **Example:** `Patagonia2026/Transactions/20260325T143000Z-a1b2c3d4/001_luigi/`
- **Content:** Inside the version folder, there is a `data.json` and optional attachments.
- **Attachments Rule:** To prevent file name collisions, all uploaded files (JPEG, PDF, etc.) MUST be renamed using a GUID format: `attachment_[guid].[extension]`. The original file name is discarded.
- **Soft Deletion Rule:** If a user deletes a transaction, the app must NOT delete the previous folders. Instead, it creates a new version folder (e.g., `003_luigi`) containing only an empty file named `.deleted`.
- **Conflict Handling:** If the app detects multiple folders for the same version number (e.g., `003_mario` and `003_luigi`), a conflict is flagged. The UI prompts the user to pick the winning version. The app then creates a new folder (e.g., `004_mario`) with the resolved data.

### Local Trip Registry
To power the Home Page offline, the app maintains a master registry file (`known_trips.json`) in the root of the local application data folder. This file contains a lightweight reference to every trip the user has created or joined, including the trip's ID, name, whether the current user is the owner, and the linked Google Drive Folder ID.