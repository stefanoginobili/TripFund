**Task 6: Google Drive Sync Service (Core)**

Implement the `GoogleDriveSyncService`. 
1. Authenticate the user and handle the two flows described in `ARCHITECTURE.md` (Create folder for new trip, Pick folder for existing trip). 
2. Implement two-way sync of the JSON files, attachment folders, and `.deleted` files between the local device and Google Drive.
3. Gracefully handle 403 Forbidden permissions (if a user is read-only) by showing a standard Italian error message without crashing.