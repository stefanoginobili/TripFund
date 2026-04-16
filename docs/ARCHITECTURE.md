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

1. **Config:** `[AppData]/trips/[TripSlug]/config/`
    - Contains high-level trip settings.
    - **Is Versioned:** YES.
2. **Transactions:** `[AppData]/trips/[TripSlug]/transactions/[TransactionID]/`
    - **TransactionID Format:** `yyyyMMddTHHmmssZ-[guidprefix]` (e.g., `20260325T143000Z-a1b2c3d4`). The prefix is the first 8 characters of a standard GUID.
    - **Structure:**
        - `details/`: Versioned folder containing `transaction_details.json`.
        - `attachments/[AttachmentName]/`: Unversioned leaf folders containing the actual attachment files.
    - **Attachment Rule:** All non-JSON files (images, PDFs) MUST be stored in a dedicated leaf folder `attachments/[AttachmentName]/` where `[AttachmentName]` is formatted as `ATT_[timestamp]` (UTC `yyyyMMddTHHmmssfffZ`, including milliseconds). The original filename MUST be preserved inside this folder. Metadata about the attachment (name, original name, timestamp) MUST be stored in the `transaction_details.json` file. Existing attachments are never re-saved or moved when a new transaction version is created.

## 3. The Versioned Storage Engine
The application utilizes an append-only, soft-deletion storage engine. When a folder is designated as "Versioned" (like Config or specific Transactions), its active state is determined by its sub-folders.

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
        2. Include all modified files (e.g., an updated `transaction_details.json`).
        3. Copy all **untouched** files from the immediate previous version.
        4. Explicitly **exclude/drop** any files the user intended to delete (e.g., removing a specific attachment).
- **`DEL` (Soft Deletion):** Deletes the *entire entity*.
    - **Rule:** The folder MUST ONLY contain a file named `.deleted.tf`. No `transaction_details.json` or attachments are copied forward. The `.deleted.tf` MUST contain 2 line:
      - `author=Mario Rossi`: where "Mario Rossi" in this example is the author from the Global Settings.
      - `deletedAt=20260332T212354Z`: where the timestamp is the timestamp of the deletion time.
- **`RES` (Resolution):** Closes a conflict state by merging multiple diverging branches. 
    - **Rule:** The folder MUST contain a `.resolved_versions.tf` file listing the exact folder names of the branches it is merging (one per line). It contains the exact file payload (or `.deleted.tf` marker) of the chosen winning state.

### 3.3. Standard Commit Operation Algorithm
When a user modifies data (changing one or multiple files) and saves:
1. Scan the versioned folder for all sub-folders to find the latest state.
2. Calculate the next sequence number: `NextSeq = MAX([nnn]) + 1`.
3. Create the new folder: `[NextSeq]_[kind]_[deviceId]`.
4. Populate the folder resolving the batch of changes against the previous state (copying untouched files, writing new/changed files, omitting deleted files).
5. Commit the operation atomically to the file system.

### 3.4. Conflict Detection & Resolution (DAG)
The versioning system operates as a **Directed Acyclic Graph (DAG)** of folders. Conflicts occur when multiple devices commit against the same base state without synchronizing.

**Divergence Detection (Leaves):**
A version folder is a "Leaf" if it is NOT **superseded** by any other folder. A conflict is active if there is more than one Leaf.
A folder **A** supersedes folder **B** if:
1.  **Device-Local Progression**: Both folders belong to the same `deviceId` and `A.Sequence > B.Sequence`.
2.  **Global Linear Progression**: Folder `A` has sequence `B.Sequence + 1`, and `B` was the only folder at its sequence level.
3.  **Explicit Resolution**: Folder `A` is a `RES` (Resolution) kind and its `.resolved_versions.tf` file explicitly lists `B.FolderName`.

**Conflict State Behavior:**
1.  **Thread Identification**: The engine identifies all current "Leaf" folders.
2.  **UI Feedback**: The engine surfaces the data from all active Leaves. The UI remains in a "locked/conflict" state, requiring a resolution.
3.  **Base Version Discovery**: The common ancestor (Base Version) is found by scanning backwards from the minimum sequence of the leaves until a sequence with exactly one folder is found.

**Resolution Algorithm:**
1.  **User Selection**: The user chooses a winning state (or merges data) via the UI.
2.  **Create RES folder**: The engine calculates `NextSeq = MAX(all_folders.Sequence) + 1` and creates a `[NextSeq]_RES_[deviceId]` folder.
3.  **Explicit Linkage**: The engine writes a `.resolved_versions.tf` file inside the new `RES` folder containing the folder names of all current Leaves.
4.  **Payload**: The winning data payload is copied into the `RES` folder.

**Resolution Invalidation (DAG Edge Case):**
If a device was offline during a resolution and later uploads a new `UPD` or `DEL` (even with a lower sequence number than the `RES`), this new commit will not be superseded by the existing `RES` because it wasn't explicitly listed in the `.resolved_versions.tf` file. The engine will detect multiple Leaves again, re-triggering a conflict state. This ensures no data is ever silently lost.

## 5. Remote Storage Synchronization
The synchronization process ensures the local offline-first storage and the remote provider (Microsoft OneDrive, Git, etc.) are eventually consistent. This process operates at the folder level, navigating recursively through the `trips/[TripSlug]` structure.

### 5.1. Synchronization Flow
The process compares the local trip root (`trips/[TripSlug]`) against the remote root defined by the provider-specific parameters. It follows these macro-steps:

1.  **Remote-to-Local Scan (Download Phase):**
    *   Navigate recursively through all remote folders.
    *   For each leaf folder (see 5.2):
        *   If the local destination fails the "Fully Copied" rule, restart the copy.
        *   Otherwise, update files as needed.

2.  **Integrity & Conflict Check:**
    *   Invoke the `VersionedStorageEngine` for the `config/` folder and each folder in `transactions/`.
    *   **If any conflict is detected:** The synchronization process MUST fail by throwing a `SyncConflictException`. This exception contains a list of all detected conflicts (specialized as `TripConfigConflictException` or `TransactionConflictException`). Each conflict details the diverging version folders and the common base version to facilitate UI-side resolution. The UI must catch this exception and guide the user to resolve conflicts locally before re-attempting sync.
    *   **If no conflicts are found:** Proceed to the next step.

3.  **Local-to-Remote Scan (Upload Phase):**
    *   Navigate recursively through all local folders.
    *   For each leaf folder (see 5.2):
        *   If the remote destination fails the "Fully Copied" rule, restart the copy.
        *   Otherwise, update files as needed.

### 5.2. Atomic Leaf Folder Sync & ".synching.tf" Logic
To ensure data integrity during network interruptions or crashes, the sync process follows these strict rules:

- **Strict Folder Types**: A folder MUST contain either ONLY files (Leaf folder) or ONLY subfolders (Node folder). Mixing files and folders in the same directory is strictly prohibited. 
- **Atomic Leaf Folder Sync**: Leaf folders (e.g., version folders like `001_NEW_device1`) represent the atomic unit of synchronization.
- **"Fully Copied" Rule**: A leaf folder is considered fully copied ONLY if:
    1. The destination folder is **NOT EMPTY**.
    2. The destination folder **DOES NOT contain a `.synching.tf` file**.
- **Restart Mechanism**: If a leaf folder fails the "Fully Copied" rule (e.g., it is empty or contains a `.synching.tf` file from a previous interrupted attempt), the synchronization process MUST:
    1. Clear all existing contents of the destination folder.
    2. Create a `.synching.tf` file inside the folder.
    3. Restart the copy of all files from the source.
    4. Delete the `.synching.tf` file only after all files are successfully transferred and verified.

Node folders (folders containing other folders, like `config/` or `transactions/`) are traversed recursively and do not use the `.synching.tf` logic themselves; the logic applies to their descendant leaf folders.

### 5.3. Sync Optimization (".synched.tf" Marker)
To minimize redundant network traffic and API calls, the sync engine utilizes a local-only optimization marker:

- **The ".synched.tf" Marker**: A file named `.synched.tf` is created inside a local leaf folder immediately after it has been successfully synchronized (either downloaded from remote or uploaded to remote).
- **Fast-Path Skip**: During the synchronization flow, if the engine detects a `.synched.tf` file in a local leaf folder, it **immediately skips** both the download and upload phases for that folder. It does not perform any remote listing or comparison for that specific directory.
- **Immutability Reliance**: This optimization is safe because versioned leaf folders (e.g., `001_NEW_device1`) are designed to be immutable once committed. Any change results in a new version folder with a higher sequence number.
- **Local-Only**: The `.synched.tf` file MUST NEVER be uploaded to the remote storage provider. It is strictly a local hint for the sync engine.

### 5.4. Error Handling
*   **Success:** The process is complete only when all recursive scans and copies finish without exceptions.
*   **Failure:** Any exception (API error, Disk Full, Permission Denied) during the process MUST abort the synchronization and return a descriptive error to the UI. The state of partial transfers is safely managed by the `.synching.tf` suffix logic.
