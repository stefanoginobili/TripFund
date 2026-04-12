# System Architecture

## 1. System Overview & Tech Stack
- **Application Type:** Offline-first Android application.
- **Framework:** .NET MAUI Blazor Hybrid (HTML/CSS/C#).
- **Data Format:** Human-readable JSON. Collections MUST utilize `Dictionary<string, object>` rather than arrays to facilitate key-based $O(1)$ lookups and simplified merge operations.
- **Local Storage:** Append-only, folder-based versioning system.
- **Remote Storage:** Multi-provider support (Microsoft OneDrive, Dropbox, Git, etc.). Acts purely as a synchronization layer; providers do not process logic.

## 2. File System Architecture
All application data resides locally. The absolute source of truth for the offline-first UI is the local file system.

### 2.1. Global App Data Level
- **Location:** Application's root data directory.
- **`known_trips.json`:** A lightweight master registry powering the Home Page. It contains references to every joined/created trip, mapping the local `[TripSlug]` to its specific remote storage provider and configuration parameters.

### 2.2. Trip Level
- **Structure:** `[AppData]/trips/[TripSlug]/`
- **Versioning:** This root folder is **NOT** versioned.
- **Remote Storage Mapping:** Each trip maps to a remote storage location managed by the selected remote storage provider (e.g., a shared folder in Microsoft OneDrive, a Git repository, etc.). Permissions and access control rely on the provider's native security model. Unhandled API errors MUST gracefully display a standard error without preemptively blocking UI interactions.

### 2.3. Trip Data Directories
Inside each `[TripSlug]` folder, data is partitioned into two distinct domains, both governed by the Versioned Storage Engine:

1. **Metadata:** `[AppData]/trips/[TripSlug]/metadata/`
    - Contains high-level trip settings.
    - **Is Versioned:** YES.
2. **Transactions:** `[AppData]/trips/[TripSlug]/transactions/[TransactionID]/`
    - **TransactionID Format:** `yyyyMMddTHHmmssZ-[guidprefix]` (e.g., `20260325T143000Z-a1b2c3d4`). The prefix is the first 8 characters of a standard GUID.
    - **Is Versioned:** YES.
    - **Attachment Rule:** All non-JSON files (images, PDFs) MUST be renamed upon import to `ATT_[timestamp].[extension]` to avoid collisions. `[timestamp]` format must be `yyyyMMddTHH:mm:ssZ` (UTC). Original filenames and attachment timestamp MUST be stored in the `data.json` file.

## 3. The Versioned Storage Engine
The application utilizes an append-only, soft-deletion storage engine. When a folder is designated as "Versioned" (like Metadata or specific Transactions), its active state is determined by its sub-folders.

### 3.1. Version Sub-Folder Naming Convention
Data is never stored in the root of a versioned folder. It is stored in sub-folders adhering strictly to this regex-compatible format:
`^(?<nnn>\d{3})_(?<kind>NEW|UPD|RES|DEL)_(?<deviceId>[a-z0-9\-]+)$`

- `[nnn]`: A 3-digit progressive integer (e.g., `001`, `002`).
- `[kind]`: The commit type (see 3.2).
- `[deviceId]`: The globally configured deviceId initiating the commit.

### 3.2. Commit Kinds & Multi-File Rules
Commits are **atomic**. A single version bump MUST be able to process a batch of multiple file changes (creations, modifications, and deletions) simultaneously.

- **`NEW` (Creation):** Always paired with `001`. Contains the initial dataset.
- **`UPD` (Update):** Contains a batch of modifications.
    - **Rule:** When creating an `UPD` folder, the system MUST:
        1. Include all newly created files.
        2. Include all modified files (e.g., an updated `data.json`).
        3. Copy all **untouched** files from the immediate previous version.
        4. Explicitly **exclude/drop** any files the user intended to delete (e.g., removing a specific attachment).
- **`DEL` (Soft Deletion):** Deletes the *entire entity*.
    - **Rule:** The folder MUST ONLY contain a file named `.deleted`. No `data.json` or attachments are copied forward. The `.deleted` MUST contain 2 line:
      - `author=Mario Rossi`: where "Mario Rossi" in this example is the author from the Global Settings.
      - `deletedAt=20260332T212354Z`: where the timestamp is the timestamp of the deletion time.
- **`RES` (Resolution):** Closes a conflict state. Contains the exact file payload (or `.deleted` marker) of the chosen winning thread.

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
A conflict is actively occurring IF AND ONLY IF there are two or more version sub-folders sharing the exact same `[nnn]` value, AND there is no valid `RES` folder that supersedes them.

**Conflict State Behavior:**
1. The engine MUST group the diverging paths into "Threads" based on the `[deviceId]`.
2. The engine MUST surface the latest valid data payload from *all* active threads to the UI.
3. The UI remains in a locked/conflict state until a resolution is committed.

**Resolution Algorithm:**
1. The user selects a winning thread via the UI.
2. The engine calculates `NextSeq = MAX([nnn_all_threads]) + 1`.
3. The engine creates a `[NextSeq]_RES_[deviceId]` folder.
4. The engine copies the exact state (files, or the `.deleted` marker) of the chosen winning thread into the `RES` folder.
5. The conflict is marked resolved. Standard linear commits can resume at `NextSeq + 1`.

**Conflict Override Edge Case:**
If an out-of-sync client uploads a new version folder (e.g., an `UPD` or `DEL`) that sits alongside an existing `RES` folder, the resolution is invalidated. The engine MUST revert the entity to an active Conflict State, requiring a new `RES` commit to reconcile the newly introduced thread.

## 5. Remote Storage Synchronization
The synchronization process ensures the local offline-first storage and the remote provider (Microsoft OneDrive, Git, etc.) are eventually consistent. This process operates at the folder level, navigating recursively through the `trips/[TripSlug]` structure.

### 5.1. Synchronization Flow
The process compares the local trip root (`trips/[TripSlug]`) against the remote root defined by the provider-specific parameters. It follows these macro-steps:

1.  **Remote-to-Local Scan (Download Phase):**
    *   Navigate recursively through all remote folders.
    *   For each folder existing on remote but missing locally:
        *   Initiate the **Folder Copying Flow** (see 5.2) to pull it locally.
    *   If a folder exists locally with a `.synching` suffix:
        *   Empty the local `.synching` folder and restart the copy from remote.

2.  **Integrity & Conflict Check:**
    *   Invoke the `VersionedStorageEngine` for the `metadata/` folder and each folder in `transactions/`.
    *   **If any conflict is detected:** The synchronization process MUST fail immediately to prevent overwriting diverging data. The UI must then guide the user to resolve conflicts locally before re-attempting sync.
    *   **If no conflicts are found:** Proceed to the next step.

3.  **Local-to-Remote Scan (Upload Phase):**
    *   Navigate recursively through all local folders.
    *   For each folder existing locally but missing on remote:
        *   Initiate the **Folder Copying Flow** (see 5.2) to push it to remote storage.
    *   If a folder exists remotely with a `.synching` suffix:
        *   Empty the remote `.synching` folder and restart the copy from local.

### 5.2. Atomic Folder Copying Flow (`.synching` Pattern)
To handle network interruptions or app crashes during transfer, all folder-copying operations MUST be atomic using a temporary suffix:

*   **Remote to Local:**
    1.  Create the destination folder locally with a `.synching` suffix (e.g., `002_upd_dev1.synching`).
    2.  Copy all files from the remote folder into this local `.synching` directory.
    3.  Once all files are successfully verified, rename the local folder by dropping the `.synching` suffix.
*   **Local to Remote:**
    1.  Create the destination folder on the remote storage with a `.synching` suffix.
    2.  Copy all local files into this remote `.synching` directory.
    3.  Once the remote provider confirms all files are uploaded, rename the remote folder by dropping the `.synching` suffix.

### 5.3. Error Handling
*   **Success:** The process is complete only when all recursive scans and copies finish without exceptions.
*   **Failure:** Any exception (API error, Disk Full, Permission Denied) during the process MUST abort the synchronization and return a descriptive error to the UI. The state of partial transfers is safely managed by the `.synching` suffix logic.
