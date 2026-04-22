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
    - `createdAt`: The creation time in `yyyy-MM-ddTHH:mm:ss.fffZ` format.
    - `contentType`: Explicit semantic type (e.g., `tripfund/trip-config`, `tripfund/transaction-detail`, `tripfund/transaction-attachment`).

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
3. Create a temporary work directory (e.g., `temp/commits/`).
4. Build the new leaf folder and its `.data/` subdirectory inside the temporary directory.
5. Populate `.data/` resolving the changes against the previous state.
6. Write the `.metadata` file.
7. Once all files are written, atomically `Move` the completed folder to its final destination.

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
The synchronization process groups local changes into differential ZIP packages to minimize network overhead and ensure consistency.

### 5.1. Remote Layout
The remote storage is organized per-trip:
- **`.tripfund`**: A text file in the root for quick discovery and validation. Contains `contentType=tripfund/trip`, `tripSlug`, `author`, and `createdAt`.
- `/devices/[DeviceId]/`: A folder dedicated to the specific device for permission checks and discovery.
- `/packages/`: Contains ZIP packages named `pack_[Timestamp]_[DeviceId].zip`.

### 5.2. Synchronization State (`sync_state.json`)
A local-only file in the trip's `sync/` subfolder tracks sync progress:
- **`sync.lastSuccessAt`**: The timestamp of the last successful synchronization.
- **`sync.remote.appliedPackages`**: A flat list of applied remote package filenames.
- **`sync.local.pending`**: A list of objects `{ path, createdAt }` for local leaf folders awaiting upload.

### 5.3. Synchronization Flow
1. **Evaluation Phase**:
    - Verifies Read/Write permissions by writing a timestamped file (`.last-seen`) in the `/devices/[DeviceId]/` folder.
2. **Download Phase**:
    - Lists all files in `/packages/`.
    - Discards packages created by the local device and those already in `appliedPackages`.
    - Downloads and extracts remaining packages in alphabetical order into a temporary staging folder (`temp/packages/expanded/`).
    - Once all packages are extracted successfully, atomically `Move` the extracted leaf folders into the local trip directory (overwriting existing folders if necessary).
    - Updates `appliedPackages`.
3. **Integrity & Conflict Check**:
    - Scans for local conflicts. If any are detected, the **Upload phase is aborted** until the user resolves them.
4. **Upload Phase**:
    - Gathers all leaf folders from the `pending` list.
    - Packs them into a single ZIP (only `.data/` and `.metadata` included).
    - Package name uses the **lowest** timestamp from the pending list: `pack_[LowestTimestamp]_[DeviceId].zip`.
    - **Atomic Upload**: To ensure remote atomicity and avoid partial files:
        - Small files (<= 2MB) use a single **Simple Upload** (PUT).
        - Larger files use a chunked **Upload Session** (resumable), where the file is only visible in the remote folder once the final chunk is successfully committed.
    - Removes successfully uploaded folders from the `pending` list.

### 5.4. Error Handling
Any exception (API error, Disk Full) during the process MUST abort the synchronization. Conflicts detected during the "Integrity Check" phase will throw a `SyncConflictException` to be handled by the UI.

## 6. Resilient Managed Operations

To ensure the application remains stable and data-consistent under various failure scenarios (app crashes, power loss, network instability), the following resiliency algorithms are implemented.

### 6.1. HTTP Resilience & Retries
All remote storage operations (OneDrive/Graph API) are protected by a standardized **Resilience Policy**:
- **Transient Fault Handling**: Automatically retries on HTTP 429 (Rate Limit), 500, 502, 503, and 504.
- **Strategy**: Uses exponential backoff with jitter to prevent slamming the server during recovery.
- **Timeouts & Circuit Breaker**: Prevents the application from hanging indefinitely on non-responsive endpoints.

### 6.2. Atomic Global Configuration
Global JSON files (`app_settings.json`, `known_trips.json`, and `sync/sync_state.json`) are critical for app functionality. To prevent file corruption during write operations, the system employs a **temp-and-rename** pattern:
1. Serialize the data to a temporary file (e.g., `sync_state.json.tmp`).
2. Ensure the write to the temporary file is complete and flushed to disk.
3. Replace the original file with the temporary file using an atomic `Move` operation.
4. If a `JsonException` occurs during reading (indicating corruption), the system gracefully recovers by returning a default/empty state to prevent a startup loop.

### 6.2. Leaf Folder Integrity (Atomic Move)
The versioning and sync systems ensure the integrity of "Leaf" folders through atomicity. Leaf folders are always constructed in a temporary location and moved to their final destination only after all files (`.data/` and `.metadata`) have been successfully committed to disk. This prevents partial folders from being read or synchronized.

### 6.3. Initial Import Protection
When "Joining" or "Creating" a trip, the application performs multiple steps (registry entry, directory creation, initial sync). To handle failures during this multi-step process:
1. An `.initial_import` marker file is created in the trip's root folder at the start.
2. If the process completes successfully, the marker is deleted.
3. On every app launch, the system scans for any trip folders still containing an `.initial_import` marker and automatically removes them and their registry entries, allowing the user to retry the operation cleanly.

### 6.4. Diagnostic Sync Logs
For troubleshooting purposes, every synchronization session generates a detailed execution log.
- **Location:** `[AppData]/trips/[TripSlug]/sync/logs/[yyyyMMddTHHmmssZ].log`
- **Retention:** These logs are **strictly local** and are never uploaded to remote storage. The system maintains only the **last 20 logs** per trip, automatically rotating out older entries. They provide a step-by-step audit of API calls, file transfers, and error details.
