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
