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
- **Join Trip:** User selects an existing shared folder in Drive.
- **Permissions:** Rely entirely on Google Drive folder permissions. Unhandled API permission errors (e.g., 403 Forbidden) should gracefully show a standard error to the user without altering the UI beforehand.

### Conflict Resolution & File System
Transactions are **append-only** to minimize conflicts and preserve a history of changes.
- **Structure:** `[TripFolder]/Transactions/[yyyyMMddTHHmmssZ-guidprefix]/[Version]_[authorSlug]/`
- **Example:** `patagonia-2026/Transactions/20260325T143000Z-a1b2c3d4/001_mario-mobile/`
- **Content:** Inside the version folder, there is a `data.json` and optional attachments.
- **Attachments Rule:** To prevent file name collisions, all uploaded files (JPEG, PDF, etc.) MUST be renamed using a GUID format: `attachment_[guid].[extension]`. The original file name is discarded.
- **Soft Deletion Rule:** If a user deletes a transaction, the app must NOT delete the previous folders. Instead, it creates a new version folder (e.g., `003_luigi`) containing only an empty file named `.deleted`.
- **Conflict Handling:** If the app detects multiple folders for the same version number (e.g., `003_mario` and `003_luigi`), a conflict is flagged. The UI prompts the user to pick the right version showing the latest version from each user. The app then rename al the conflicting folders prepending an underscore (`_`) and creates a new folder (e.g., `004_mario`) with the next version for the user resolving the conflict and the resolved data. Conflict can happen between 2 or more threads. User in the folder's name defines the thread. Here is a more complex example; in case the app finds the folders `003_mario`, `003_luigi`, `003_carlo`, `004_mario`, `005_mario` and `004_luigi` the versions to show to resolve the conflict are the latest for each user (`005_mario`, `004_luigi` and `003_carlo`). In case the user resolving the conflict is `mario` then the 6 folders listed in the example will be renamed prepending an underscore (`_`) and creating the resolution folder named `006_mario`. Extracting the core rules:
  - **Conflict Trigger:** A conflict exists the moment two or more folders share the exact same version number (the prefix) but have different usernames (e.g., a "fork" in the tree).
  - **Resolution Candidates:** The UI filters the view to show only the highest/latest version number for each user thread involved in the conflict.
  - **Archiving (The `_` Prefix):** Upon resolution, all folders involved in the divergent threads (from the point of conflict up to their latest versions) are archived by prepending an underscore (_).
  - **Next Version Calculation:** The new resolved folder takes the global maximum version number across all threads, adds 1, and appends the name of the user who clicked "resolve".

### Local Trip Registry
To power the Home Page offline, the app maintains a master registry file (`known_trips.json`) in the root of the local application data folder. This file contains a lightweight reference to every trip the user has created or joined, including the trip's ID and the linked Google Drive Folder ID.