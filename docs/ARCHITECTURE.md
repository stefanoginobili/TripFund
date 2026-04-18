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

1. **Config:** `[AppData]/trips/[TripSlug]/config_versioned/`
    - Contains high-level trip settings.
    - **Is Versioned:** YES.
2. **Transactions:** `[AppData]/trips/[TripSlug]/transactions/[TransactionID]/`
    - **TransactionID Format:** `yyyyMMddTHHmmssZ-[guidprefix]` (e.g., `20260325T143000Z-a1b2c3d4`). The prefix is the first 8 characters of a standard GUID.
    - **Structure:**
        - `details_versioned/`: Versioned folder containing the transaction state.
        - `attachments/[AttachmentName]/`: Unversioned leaf folders containing the actual attachment files.

## 3. The Versioned Storage Engine
The application utilizes an append-only, soft-deletion storage engine. Data is stored in "Leaf" folders.

### 3.1. Leaf Folder Structure
Every Leaf folder (whether a version folder or an attachment folder) MUST strictly follow this structure:
- **`.data/`**: A subdirectory containing all user payload files (e.g., `transaction_details.json`, `trip_config.json`, or the actual attachment file).
- **`.metadata`**: A text file containing key-value pairs (one per line). Default keys added upon creation:
    - `author`: The name of the user who created the leaf.
    - `device`: The `deviceId` that initiated the commit.
    - `timestamp`: The creation time in `yyyy-MM-ddTHH:mm:ssZ` format.

### 3.2. Version Sub-Folder Naming Convention
Versioned folders (like Config or details_versioned) contain sub-folders adhering to this regex:
`^(?<nnn>\d{3})_(?<kind>NEW|UPD|RES|DEL)_(?<deviceId>[a-z0-9\-]+)$`

- `[nnn]`: A 3-digit progressive integer (e.g., `001`, `002`).
- `[kind]`: The commit type (see 3.3).
- `[deviceId]`: The globally configured deviceId initiating the commit.

### 3.3. Commit Kinds & Multi-File Rules
Commits are **atomic**. A single version bump MUST be able to process a batch of multiple file changes simultaneously.

- **`NEW` (Creation):** Always paired with `001`. Contains the initial dataset in `.data/`.
- **`UPD` (Update):** Contains a batch of modifications.
    - **Rule:** When creating an `UPD` folder, the system MUST:
        1. Include all newly created files in `.data/`.
        2. Include all modified files in `.data/`.
        3. Copy all **untouched** files from the `.data/` folder of the immediate previous version.
        4. Explicitly **exclude/drop** any files the user intended to delete.
- **`DEL` (Soft Deletion):** Deletes the *entire entity*.
    - **Rule:** The `.data/` folder MUST be EMPTY. No payload files are copied forward.
- **`RES` (Resolution):** Closes a conflict state by merging multiple diverging branches. 
    - **Rule:** The `.metadata` file MUST contain a `resolved_versions` key listing the folder names of the branches it is merging (comma-separated). It contains the winning state payload in `.data/`.

### 3.4. Standard Commit Operation Algorithm
When a user modifies data and saves:
1. Scan the versioned folder for all sub-folders to find the latest state.
2. Calculate the next sequence number: `NextSeq = MAX([nnn]) + 1`.
3. Create the new folder and its `.data/` subdirectory.
4. Populate `.data/` resolving the changes against the previous state.
5. Write the `.metadata` file.
6. Commit the operation atomically to the file system.

### 3.5. Conflict Detection & Resolution (DAG)
The versioning system operates as a **Directed Acyclic Graph (DAG)** of folders.

**Divergence Detection (Leaves):**
A version folder is a "Leaf" if it is NOT **superseded** by any other folder. A conflict is active if there is more than one Leaf.
A folder **A** supersedes folder **B** if:
1.  **Device-Local Progression**: Both folders belong to the same `deviceId` and `A.Sequence > B.Sequence`.
2.  **Global Linear Progression**: Folder `A` has sequence `B.Sequence + 1`, and `B` was the only folder at its sequence level.
3.  **Explicit Resolution**: Folder `A` is a `RES` kind and its `.metadata` file explicitly lists `B.FolderName` in the `resolved_versions` key.

**Resolution Algorithm:**
1.  **User Selection**: The user chooses a winning state via the UI.
2.  **Create RES folder**: Calculate `NextSeq = MAX(all_folders.Sequence) + 1` and create a `[NextSeq]_RES_[deviceId]` folder.
3.  **Explicit Linkage**: Write `resolved_versions=[list]` in the `.metadata` file.
4.  **Payload**: Copy the winning data payload into the `.data/` folder.

## 5. Remote Storage Synchronization
The synchronization process ensures the local offline-first storage and the remote provider are eventually consistent.

### 5.1. Synchronization Flow
The process navigates recursively through the `trips/[TripSlug]` structure. For each leaf folder (identified by the presence of `.metadata` or `.data/`):
- Only the `.metadata` file and the `.data/` folder (recursively) are synchronized.
- Application-local markers (like `.synched`) are NEVER synchronized.

### 5.2. Atomic Leaf Folder Sync & Markers
To ensure data integrity, the sync process uses the following markers at the root of the Leaf folder:

- **`.uploading`**: Created in the **remote** destination folder when copying from local to remote. It contains:
    - `source=[deviceId]`
    - `begin=[timestamp]`
- **`.downloading`**: Created in the **local** destination folder when copying from remote to local. It contains:
    - `begin=[timestamp]`

**"Fully Copied" Rule**: A leaf folder is considered fully copied ONLY if it contains a `.metadata` file and DOES NOT contain the appropriate `.uploading` or `.downloading` marker.

### 5.3. Sync Optimization (".synched" Marker)
- **The ".synched" Marker**: A local-only file named `.synched` is created inside a local leaf folder immediately after successful synchronization.
- **Fast-Path Skip**: If the engine detects a `.synched` file, it **immediately skips** both the download and upload phases for that folder.
- **Local-Only**: The `.synched` file MUST NEVER be uploaded to the remote storage provider.

### 5.4. Error Handling
Any exception (API error, Disk Full, Conflict) during the process MUST abort the synchronization. Conflicts detected during the "Integrity Check" phase (between Download and Upload) will throw a `SyncConflictException` to be handled by the UI.

## 6. Resilient Managed Operations

To ensure the application remains stable and data-consistent under various failure scenarios (app crashes, power loss, network instability), the following resiliency algorithms are implemented.

### 6.1. Atomic Global Configuration
Global JSON files (`app_settings.json` and `known_trips.json`) are critical for app functionality. To prevent file corruption during write operations, the system employs a **temp-and-rename** pattern:
1. Serialize the data to a temporary file (e.g., `known_trips.json.tmp`).
2. Ensure the write to the temporary file is complete and flushed to disk.
3. Replace the original file with the temporary file using an atomic `Move` operation.
4. If a `JsonException` occurs during reading (indicating corruption), the system gracefully recovers by returning a default/empty state to prevent a startup loop.

### 6.2. Leaf Folder Integrity ("Fully Copied" Rule)
The versioning and sync systems rely on the integrity of "Leaf" folders. A folder is considered valid and committed ONLY if:
1. It contains a valid `.metadata` file.
2. It DOES NOT contain an active marker file (`.uploading` or `.downloading`).
This ensures that incomplete transfers or local commits are ignored or cleaned up.

### 6.3. Initial Import Protection
When "Joining" or "Creating" a trip, the application performs multiple steps (registry entry, directory creation, initial sync). To handle failures during this multi-step process:
1. An `.initial_import` marker file is created in the trip's root folder at the start.
2. If the process completes successfully, the marker is deleted.
3. On every app launch, the system scans for any trip folders still containing an `.initial_import` marker and automatically removes them and their registry entries, allowing the user to retry the operation cleanly.

### 6.4. Diagnostic Sync Logs
For troubleshooting purposes, every synchronization session generates a detailed execution log.
- **Location:** `[AppData]/debug/sync/[TripSlug].txt`
- **Retention:** These logs are **strictly local** and are never uploaded to remote storage. They provide a step-by-step audit of API calls, file transfers, and error details.
