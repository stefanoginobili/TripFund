**Task 6: Google Drive Sync Service (Core)**

Implement the `GoogleDriveRemoteStorageService`.
1. The Google Drive implementation must use the Google Drive REST API.
2. The Google Drive ClientId are stored in the `Config` static class.
3. When the user choose the Google Drive Remote Storage through the `RemoteStorageSelector`, the user will provider the destination folder through a URL like "https://drive.google.com/drive/folders/1-6SjKzhRi3vXhVo9l0Yay_ZzTQA5YH2t"
4. The Storage Service tries to read from the remote folder and, if required, invoke the underlying system to ask the user the permission for Google Drive.
5. In case the user is creating a new trip, read/write permission on the folder is required.
6. In case the user is adding and existing trip, at least read permission is required.