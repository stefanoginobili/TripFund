# Data Schemas

This document defines the exact JSON structures to be used in the application. As per architecture guidelines, we heavily rely on Dictionaries (Key-Value objects).

## 1. Trip Configuration (`trip_config.json`)
This file is stored at the root of the Trip folder. 
**Crucial:** Users are identified by unique, human-readable slugs (e.g., `alice`, `mario-rossi`), NOT GUIDs.

```json
{
  "id": "guid-trip-1234",
  "name": "Patagonia 2026",
  "description": "Roadtrip in Argentina and Chile",
  "createdAt": "2026-03-24T12:00:00Z",
  "currencies": {
    "EUR": { "symbol": "€", "name": "Euro" },
    "ARS": { "symbol": "$", "name": "Argentine Peso" }
  },
  "members": {
    "mario-rossi": { "name": "Mario Rossi", "email": "mario@example.com" },
    "luigi": { "name": "Luigi", "email": "luigi@example.com" }
  },
  "expectedQuotas": {
    "EUR": {
      "mario-rossi": 500.00,
      "luigi": 500.00
    },
    "ARS": {
      "mario-rossi": 150000.00,
      "luigi": 150000.00
    }
  }
}
```

## 2. Transaction (`data.json`)
This file is stored inside the specific version folder of a transaction (e.g., `Transactions/[TransactionID]/001_userid/data.json`).

```json
{
  "id": "20260325T143000Z-a1b2c3d4",
  "type": "expense", 
  "date": "2026-03-25T14:30:00Z",
  "currency": "ARS",
  "amount": 15000.50,
  "description": "Cena a Buenos Aires",
  "registeredBy": "luigi",
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
* Note 2: `registeredBy` indicates which user (slug) created the transaction in the app. It does not dictate who the money belongs to.
* Note 3: `type` can be "expense" (spesa) or "contribution" (versamento in cassa). The split dictionary determines the money flow per user. If "expense", the split amounts are DEDUCTED from the users' balances. If "contribution", the `split` amount is ADDED to the user's balance. The sum of all values in split MUST exactly equal the `amount`.