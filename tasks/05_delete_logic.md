**Task 5: Transaction Deletion Flow**

Implement the soft-deletion logic for a transaction from the UI. 
1. When a user deletes a transaction, use the `LocalTripStorageService` to create a new incremented version folder for that transaction ID.
2. Place an empty `.deleted` file inside this new folder. Do not write a `data.json` for deleted versions. 
3. Update the Dashboard UI so it immediately recalculates balances and reflects the deleted state.