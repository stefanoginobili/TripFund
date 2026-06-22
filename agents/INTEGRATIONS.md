# External Integrations Setup

This document provides instructions for configuring external services required for TripFund's remote storage and synchronization features.

## Microsoft OneDrive Integration (Azure/Entra ID)

TripFund uses Microsoft OneDrive for cloud synchronization. To enable this, you must register an application in the Microsoft Entra admin center (formerly Azure Active Directory).

### 1. App Registration
1.  Go to the [Microsoft Entra admin center](https://entra.microsoft.com/).
2.  Navigate to **Identity > Applications > App registrations**.
3.  Click **New registration**.
4.  **Name**: `TripFund` (or your preferred name).
5.  **Supported account types**: `Accounts in any organizational directory (Any Microsoft Entra ID tenant - Multitenant) and personal Microsoft accounts (e.g. Skype, Xbox)`.
    *   *Note: This is critical for allowing personal OneDrive accounts to use the app.*
6.  **Redirect URI**:
    *   Select **Public client/native (mobile & desktop)**.
    *   Enter: `msal[YOUR_CLIENT_ID]://auth` (Replace `[YOUR_CLIENT_ID]` with the Application ID generated after registration, or use a custom scheme if configured in `MauiProgram.cs`).
    *   For development, you may also need: `http://localhost`.

### 2. Authentication Settings
1.  Under **Manage**, select **Authentication**.
2.  Ensure **Allow public client flows** is set to **Yes** if you are using ROPC (not recommended) or specific mobile flows.
3.  For MAUI, ensure the **Platform configuration** for Android and iOS are added with the correct Bundle ID / Package Name and Signature Hash.

### 3. API Permissions
Configure the following permissions under **Manage > API permissions**:

| API / Permissions name | Type | Description |
| :--- | :--- | :--- |
| **Microsoft Graph** | | |
| `Files.ReadWrite` | Delegated | Allows the app to read, create, update and delete the signed-in user's files. |
| `User.Read` | Delegated | Required to sign in and read user profile. |
| `offline_access` | Delegated | Required to maintain access when the app is not active (Refresh Tokens). |

### 4. Configuration in Code
Update the `IMicrosoftAuthConfiguration` implementation in `src/TripFund.App/Services/` (or your environment settings) with the following values:
*   **ClientId**: The "Application (client) ID" from the Overview page.
*   **TenantId**: `common` (since we support personal accounts).
*   **Scopes**: `["User.Read", "Files.ReadWrite", "offline_access"]`.

## OneDrive Shared Link Resolution

To allow users to join trips without explicit file invitations, TripFund implements a robust resolution strategy for "Anyone with the link" URLs.

### 1. Multi-Trial Resolution Strategy
Microsoft Graph's `shares` API is sensitive to URL formats and query parameters. The app automatically attempts the following variations in order:
1.  **Original URL**: Encodes the exact string pasted by the user.
2.  **Stripped URL**: Removes tracking/event parameters (e.g., `?e=...`) while preserving redemption tokens.
3.  **Expanded URL**: Follows redirects (e.g., from `1drv.ms`) to find the canonical OneDrive/SharePoint URL before encoding.

### 2. Anonymous vs. Authenticated Access
- **Anonymous Attempt**: For Personal OneDrive, providing an `Authorization` header on a public link can sometimes trigger an `accessDenied` error if the user isn't the owner. The app always tries anonymous resolution first.
- **Authenticated Redemption**: For SharePoint Online (SPO) and migrated Personal accounts, the app retries with the user's token and the `Prefer: redeemSharingLink` header. This programmatically "redeems" the link for the user, granting them durable access.

### 3. Unified Lifecycle
Once a shared link is successfully resolved to a `driveId` and `folderId`, the trip is treated **identically** to one added via the manual folder picker. The app uses standard `/drives/{driveId}/items/{itemId}` pathing for all operations.

### 4. Dynamic Write-Permission Detection
Trips added via shared links default to a "Read-Write" state. The `RemoteStorageSyncEngine` then performs a **Self-Healing Evaluation**:
1.  It attempts to ensure the remote structure (`/devices`, `/packages`) and register the local device.
2.  It performs a write test by uploading a small `.last-seen` file.
3.  If any write operation fails (e.g., the link only has "View" permissions), the engine **automatically downgrades** the local trip registry to `Read-Only` and continues in download-only mode.
4.  If permissions are later upgraded on the link, the next sync run will automatically detect the change and "upgrade" the local trip back to `Read-Write`.
