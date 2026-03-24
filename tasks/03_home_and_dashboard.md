**Task 3: Home Page, Trip Management & Dashboard UI**

1. **Home Page (Italian):** Create the startup page of the app reading the `known_trips.json` file.
   - Group trips into: "I miei viaggi" (isOwner = true) and "Viaggi condivisi con me" (isOwner = false).
   - Bottom buttons: "Crea nuovo viaggio" and "Aggiungi viaggio esistente".
2. **Trip Creation/Edit/Join Flows (Italian):** - Create UI pages for creating a new Trip, **editing an existing one** (Name, Description, Start/End Dates, Currencies, Expected Quotas), or joining an existing one. 
   - When adding or editing members, require an Email (for receipts) and an Emoji character as an Avatar (default to "👤").
   - Auto-suggest unique slugs based on member names. Update both `known_trips.json` and `trip_config.json` upon saving.
3. **Trip Dashboard (Italian):** Build the Dashboard layout. Display the trip dates and the members with their emoji avatars. 
   - Implement the mathematical logic for the 'Quota Residua' (User Balance): `(Sum of 'split' amounts in active 'contribution' transactions) - (Sum of 'split' amounts in active 'expense' transactions)`.
4. **Testing:** Write narrow integration tests for Home Page rendering, the Trip creation/edit flow (ensuring all JSON fields like dates and emojis are saved correctly), and the dashboard balance logic.