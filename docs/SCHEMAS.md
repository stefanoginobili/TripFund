# Data Schemas

This document defines the exact JSON structures to be used in the application. As per architecture guidelines, we heavily rely on Dictionaries (Key-Value objects).

## 1. Trip Configuration (`trip_config.json`)
This file is stored inside the specific version folder of the trip's `metadata` folder (see `ARCHITECTURE.md` for folders' structure).
**Crucial:** Members are identified by unique, human-readable slugs (e.g., `alice`, `mario-rossi`), NOT GUIDs.

```json
{
  "id": "guid-trip-1234",
  "name": "Patagonia 2026",
  "description": "Roadtrip in Argentina and Chile",
  "startDate": "2026-11-01",
  "endDate": "2026-11-20",
  "createdAt": "2026-03-24T12:00:00Z",
  "updatedAt": "2026-03-27T13:40:00Z",
  "currencies": {
    "EUR": { "symbol": "€", "name": "Euro", "expectedQuotaPerMember": 500.00 },
    "ARS": { "symbol": "$", "name": "Argentine Peso", "expectedQuotaPerMember": 150000.00 }
  },
  "members": {
    "mario-rossi": { "name": "Mario Rossi", "email": "mario@example.com", "avatar": "🎒" },
    "luigi": { "name": "Luigi", "email": "luigi@example.com", "avatar": "👤" }
  }
}
```

Note: `expectedQuotaPerMember` defines the target contribution amount that EACH member is expected to deposit into the shared fund for that specific currency.

## 2. Transaction (`data.json`)
This file is stored inside the specific version folder of the trip's `transactions` (see `ARCHITECTURE.md` for folders' structure).

```json
{
  "id": "20260325T143000Z-a1b2c3d4",
  "type": "expense", 
  "date": "2026-03-25T14:30:00Z",
  "currency": "ARS",
  "amount": 15000.50,
  "description": "Cena a Buenos Aires",
  "author": "Mario Rossi",
  "split": {
    "mario-rossi": 10000.00,
    "luigi": 5000.50
  }, 
  "location": {
    "latitude": -34.6037,
    "longitude": -58.3816,
    "name": "Restaurante El Gaucho"
  },
  "attachments": [
    "attachment_abc123.jpg"
  ]
}
```

* Note 1: `id` MUST be formatted as a compact GMT timestamp followed by an 8-character GUID prefix (e.g., `yyyyMMddTHHmmssZ-[guid-prefix]`). This ensures folders are chronologically sortable on the file system while remaining unique.
* Note 2: `author` is a plain string representing the physical user of the device (retrieved from `app_settings.json`'s `authorName`), NOT a trip member's slug. It is used purely for auditing and conflict resolution purposes.
* Note 3: `type` can be "expense" (spesa) or "contribution" (versamento in cassa). The split dictionary determines the money flow per user. If "expense", the split amounts are DEDUCTED from the users' balances. If "contribution", the `split` amount is ADDED to the user's balance. The sum of all values in split MUST exactly equal the `amount`.

## 3. Local Trip Registry (`known_trips.json`)
This file is stored in the root of the app's local storage. It acts purely as a pointer registry to find the trip folders.

```json
{
  "trips": {
    "patagonia-2026": {
      "driveFolderId": "drive-folder-xyz"
    },
    "giappone-2027": {
      "driveFolderId": "drive-folder-abc"
    }
  }
}
```

Note: The keys in the `trips` dictionary ("patagonia-2026", "giappone-2027") are the actual names of the local folders (the URL-safe slugs of the trip names). To display the Home Page, the app iterates through these keys, accesses each folder, and reads the `trip_config.json` inside to get the `name` and `startDate`/`endDate` for the UI.

## 4. Global App Settings (`app_settings.json`)
This file is stored in the root of the app's local storage (next to `known_trips.json`). It stores global device preferences.

```json
{
  "authorName": "Mario Rossi",
  "deviceId": "mario-rossi-abcd1234"
}
```

Note: `deviceId` is auto-generated from `authorName` during the first launch/settings and is used as the suffix for transaction version folders. Its value is composed by the slug of the `authorName` and the first 8 characters of a random GUID.