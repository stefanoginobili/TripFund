# System Architecture

## 1. System Overview & Tech Stack
- **Application Type:** Offline-first Android application.
- **Framework:** .NET MAUI Blazor Hybrid (HTML/CSS/C#).
- **Data Format:** Human-readable JSON. Collections MUST utilize `Dictionary<string, object>` rather than arrays to facilitate key-based $O(1)$ lookups and simplified merge operations.
- **Local Storage:** Append-only, folder-based versioning system.
- **Remote Sync:** Google Drive API (functions purely as a synchronization layer; Drive does not process logic).

## 2. File System Architecture
All application data resides locally. The absolute source of truth for the offline-first UI is the local file system.

### 2.1. Global App Data Level
- **Location:** Application's root data directory.
- **`known_trips.json`:** A lightweight master registry powering the Home Page. It contains references to every joined/created trip, mapping the local `[TripSlug]` to the remote Google Drive Folder ID.

### 2.2. Trip Level
- **Structure:** `[AppData]/trips/[TripSlug]/`
- **Versioning:** This root folder is **NOT** versioned.
- **Google Drive Mapping:** Maps 1:1 with a shared folder in Google Drive. Folder permissions rely entirely on Drive's native permission model. Unhandled API errors (e.g., 403 Forbidden) MUST gracefully display a standard error without preemptively blocking UI interactions.

### 2.3. Trip Data Directories
Inside each `[TripSlug]` folder, data is partitioned into two distinct domains, both governed by the Versioned Storage Engine:

1. **Metadata:** `[AppData]/trips/[TripSlug]/metadata/`
    - Contains high-level trip settings.
    - **Is Versioned:** YES.
2. **Transactions:** `[AppData]/trips/[TripSlug]/transactions/[TransactionID]/`
    - **TransactionID Format:** `yyyyMMddTHHmmssZ-[guidprefix]` (e.g., `20260325T143000Z-a1b2c3d4`). The prefix is the first 8 characters of a standard GUID.
    - **Is Versioned:** YES.
    - **Attachment Rule:** All non-JSON files (images, PDFs) MUST be renamed upon import to `attachment_[guid].[extension]`. Original filenames MUST be discarded to prevent collisions.

## 3. The Versioned Storage Engine
The application utilizes an append-only, soft-deletion storage engine. When a folder is designated as "Versioned" (like Metadata or specific Transactions), its active state is determined by its sub-folders.

### 3.1. Version Sub-Folder Naming Convention
Data is never stored in the root of a versioned folder. It is stored in sub-folders adhering strictly to this regex-compatible format:
`^(?<nnn>\d{3})_(?<kind>new|upd|res|del)_(?<deviceId>[a-z0-9\-]+)$`

- `[nnn]`: A 3-digit progressive integer (e.g., `001`, `002`).
- `[kind]`: The commit type (see 3.2).
- `[deviceId]`: The globally configured deviceId initiating the commit.

### 3.2. Commit Kinds & Multi-File Rules
Commits are **atomic**. A single version bump MUST be able to process a batch of multiple file changes (creations, modifications, and deletions) simultaneously.

- **`new` (Creation):** Always paired with `001`. Contains the initial dataset.
- **`upd` (Update):** Contains a batch of modifications.
    - **Rule:** When creating an `upd` folder, the system MUST:
        1. Include all newly created files.
        2. Include all modified files (e.g., an updated `data.json`).
        3. Copy all **untouched** files from the immediate previous version.
        4. Explicitly **exclude/drop** any files the user intended to delete (e.g., removing a specific attachment).
- **`del` (Soft Deletion):** Deletes the *entire entity*.
    - **Rule:** The folder MUST ONLY contain an empty file named `.deleted`. No `data.json` or attachments are copied forward.
- **`res` (Resolution):** Closes a conflict state. Contains the exact file payload (or `.deleted` marker) of the chosen winning thread.

### 3.3. Standard Commit Operation Algorithm
When a user modifies data (changing one or multiple files) and saves:
1. Scan the versioned folder for all sub-folders to find the latest state.
2. Calculate the next sequence number: `NextSeq = MAX([nnn]) + 1`.
3. Create the new folder: `[NextSeq]_[kind]_[deviceId]`.
4. Populate the folder resolving the batch of changes against the previous state (copying untouched files, writing new/changed files, omitting deleted files).
5. Commit the operation atomically to the file system.

### 3.4. Conflict Detection & Resolution Matrix
Conflicts occur natively due to the offline-first architecture when multiple users commit against the same base state before syncing.

**Detection Rule:**
A conflict is actively occurring IF AND ONLY IF there are two or more version sub-folders sharing the exact same `[nnn]` value, AND there is no valid `res` folder that supersedes them.

**Conflict State Behavior:**
1. The engine MUST group the diverging paths into "Threads" based on the `[deviceId]`.
2. The engine MUST surface the latest valid data payload from *all* active threads to the UI.
3. The UI remains in a locked/conflict state until a resolution is committed.

**Resolution Algorithm:**
1. The user selects a winning thread via the UI.
2. The engine calculates `NextSeq = MAX([nnn_all_threads]) + 1`.
3. The engine creates a `[NextSeq]_res_[deviceId]` folder.
4. The engine copies the exact state (files, or the `.deleted` marker) of the chosen winning thread into the `res` folder.
5. The conflict is marked resolved. Standard linear commits can resume at `NextSeq + 1`.

**Conflict Override Edge Case:**
If an out-of-sync client uploads a new version folder (e.g., an `upd` or `del`) that sits alongside an existing `res` folder, the resolution is invalidated. The engine MUST revert the entity to an active Conflict State, requiring a new `res` commit to reconcile the newly introduced thread.