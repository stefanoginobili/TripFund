# Data Schemas

This document defines the exact JSON structures to be used in the application. As per architecture guidelines, we heavily rely on Dictionaries (Key-Value objects).

## 1. Trip Configuration (`trip_config.json`)
This file is stored inside the specific version folder of the trip's `config_versioned` folder (see `ARCHITECTURE.md` for folders' structure).
**Crucial:** Members are identified by unique, human-readable slugs (e.g., `alice`, `mario-rossi`), NOT GUIDs.

```json
{
  "id": "guid-trip-1234",
  "name": "Patagonia 2026",
  "description": "Roadtrip in Argentina and Chile",
  "startDate": "2026-11-01",
  "endDate": "2026-11-20",
  "createdAt": "2026-03-24T12:00:00.000Z",
  "updatedAt": "2026-03-27T13:40:00.000Z",
  "author": "Mario Rossi",
  "currencies": {
    "EUR": {
      "symbol": "€",
      "name": "Euro",
      "decimals": 2,
      "expectedQuotaPerMember": 500.00
    },
    "ARS": {
      "symbol": "$",
      "name": "Argentine Peso",
      "decimals": 0,
      "expectedQuotaPerMember": 150000.00
    }
  },
  "members": {
    "mario-rossi": {
      "name": "Mario Rossi",
      "email": "mario@example.com",
      "avatar": "🎒"
    },
    "luigi": {
      "name": "Luigi",
      "email": "luigi@example.com",
      "avatar": "👤"
    }
  }
}
```

* Note 1: `expectedQuotaPerMember` defines the target contribution amount that EACH member is expected to deposit into the shared fund for that specific currency. `author` contains the user name (from the Global Settings) that last saved (created or updated) the trip config.
* Note 2: `author` is a plain string representing the physical user of the device (retrieved from `app_settings.json`'s `authorName`), NOT a trip member's slug. It is used purely for auditing and conflict resolution purposes. It is set each time the configuration is created or updated.

## 2. Transaction (`transaction_details.json`)
This file is stored inside the specific version folder of the trip's `transactions/[TransactionID]/details_versioned` (see `ARCHITECTURE.md` for folders' structure).

```json
{
  "id": "20260325T143000Z-a1b2c3d4",
  "type": "expense",
  "date": "2026-03-25T14:30:00+02:00",
  "timezone": "Europe/Paris",
  "createdAt": "2026-03-25T14:30:00.000Z",
  "updatedAt": "2026-03-26T11:20:00.000Z",
  "currency": "ARS",
  "amount": 15000.50,
  "description": "Cena a Buenos Aires",
  "author": "Mario Rossi",
  "split": {
    "mario-rossi": {
      "amount": 10000.00,
      "manual": true
    },
    "luigi": {
      "amount": 5000.50,
      "manual": false
    }
  },
  "location": {
    "latitude": -34.6037,
    "longitude": -58.3816,
    "name": "Restaurante El Gaucho"
  },
  "attachments": [
    {
      "name": "ATT_20260406T133421555Z",
      "originalName": "PXL_20260406_133421_Z.jpg",
      "createdAt": "2026-04-06T13:34:21.555Z"
    }
  ]
}
```

* Note 1: `id` MUST be formatted as a compact GMT timestamp followed by an 8-character GUID prefix (e.g., `yyyyMMddTHHmmssZ-[guid-prefix]`). This ensures folders are chronologically sortable on the file system while remaining unique.
* Note 2: `author` is a plain string representing the physical user of the device (retrieved from `app_settings.json`'s `authorName`), NOT a trip member's slug. It is used purely for auditing and conflict resolution purposes. It is set each time a transaction is created or updated.
* Note 3: `manuale` is a flag indicating wheter the amount has been manually set by the user or it has been automatically calculated by the application.
* Note 4: `type` can be "expense" (spesa) or "contribution" (versamento in cassa). The split dictionary determines the money flow per user. If "expense", the split amounts are DEDUCTED from the users' balances. If "contribution", the `split` amount is ADDED to the user's balance. The sum of all values in split MUST exactly equal the `amount`.
* Note 5: `attachments.name` MUST be formatted as `ATT_yyyyMMddTHHmmssfffZ` (using the UTC `createdAt` timestamp including milliseconds). The `originalName` MUST store the original filename as provided by the camera or file system. The physical file will be stored in `attachments/[attachments.name]/[originalName]`.

## 3. Local Trip Registry (`known_trips.json`)
This file is stored in the root of the app's local storage. It acts purely as a pointer registry to find the trip folders.

```json
{
  "trips": {
    "patagonia-2026": {
      "createdAt": "2026-05-01T13:30:00.000Z",
      "remoteStorage": {
        "provider": "onedrive",
        "parameters": {
          "folderId": "abcdef1234567890"
        },
        "readonly": false
      }
    },
    "giappone-2027": {
      "createdAt": "2026-04-01T13:30:00.000Z",
      "remoteStorage": {
        "provider": "git",
        "parameters": {
          "repository": "https://github.com/mario/giappone.git"
        },
        "readonly": true
      }
    }
  }
}
```

* Note 1: The keys in the `trips` dictionary ("patagonia-2026", "giappone-2027") are the actual names of the local folders (the URL-safe slugs of the trip names). To display the Home Page, the app iterates through these keys, accesses each folder, and reads the `trip_config.json` inside to get the `name` and `startDate`/`endDate` for the UI.
* Note 2: The `remoteStorage.provider` property defines the implementation to use to sync the trip with a remote storage. `remoteStorage.parameters` is a key/value dictionary containing the configuration required by the provider. They are usually provided by the user when adding a trip and after they have chosen the remote storage provider for the trip.

## 4. Global App Settings (`app_settings.json`)
This file is stored in the root of the app's local storage (next to `known_trips.json`). It stores global device preferences.

```json
{
  "authorName": "Mario Rossi",
  "deviceId": "mario-rossi-abcd1234"
}
```

Note: `deviceId` is auto-generated from `authorName` during the first launch/settings and is used as the suffix for transaction version folders. Its value is composed by the slug of the `authorName` (with leading/trailing hyphens trimmed) and the first 8 characters of a random GUID.

## 5. Trip Sync State (`sync_state.json`)
This file is stored in a subfolder of the specific trip (`[AppData]/trips/[TripSlug]/sync/sync_state.json`). It is local-only and tracks the progress of the differential synchronization.

```json
{
  "sync": {
    "lastSuccessAt": "2026-04-12T22:33:12.890Z",
    "remote": {
      "appliedPackages": [
        "pack_20260413T143255890Z_mario-rossi-abcd1234.zip",
        "pack_20260416T123155230Z_luigi-efgh5678.zip"
      ]
    },
    "local": {
      "pending": [
        {
          "path": "config_versioned/001_NEW_mario-rossi-abcd1234",
          "createdAt": "2026-04-12T22:33:12.890Z"
        },
        {
          "path": "transactions/20260416T204312Z-021c3e7b/details_versioned/001_NEW_mario-rossi-abcd1234",
          "createdAt": "2026-04-16T20:43:12.450Z"
        }
      ]
    }
  }
}
```

* Note 1: `remote.appliedPackages` is a flat list of strings containing the filenames of all ZIP packages already downloaded and extracted into the local trip folder.
* Note 2: `local.pending` is a list of local leaf folders (relative paths from the trip root) that have been created or modified locally but not yet uploaded in a package.
* Note 3: `pending.createdAt` uses the standard GMT format including milliseconds (`yyyy-MM-ddTHH:mm:ss.fffZ`) to ensure precise ordering and unique identification of packages.
* Note 4: This file MUST be saved using the atomic **temp-and-rename** strategy to prevent corruption.

## 6. Remote Trip Initialization (`.tripfund`)
This is a plain text file (UTF-8) stored in the root of the remote storage folder. It is used for quick discovery and validation.

```
contentType=tripfund/trip
tripSlug=patagonia-2026
author=Mario Rossi
createdAt=2026-03-24T12:00:00.000Z
```

* Note 1: `contentType` MUST be exactly `tripfund/trip`.
* Note 2: `trip.slug` is the original slug of the trip (used as the base for the local folder name).
* Note 3: `author` and `createdAt` are used to display a confirmation message to the user when adding an existing trip.

## 7. Leaf Folder Metadata (`.tripfund`)
Every leaf folder contains a `.tripfund` file (plain text, key-value pairs).

**Mandatory Keys:**
- `author`: Physical user name.
- `device`: Device ID.
- `createdAt`: `yyyy-MM-ddTHH:mm:ss.fffZ`.
- `contentType`: Explicit semantic type.
- `versioning.parents`: Comma-separated list of parent folder names (empty for `001_NEW`).

**Allowed Content Types:**
- `tripfund/trip-config`: For trip configuration versions.
- `tripfund/transaction-detail`: For transaction detail versions.
- `tripfund/transaction-attachment`: For transaction attachments.