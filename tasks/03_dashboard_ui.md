**Task 3: Trip Management UI & Dashboard Skeleton**

1. Create the Blazor UI pages for creating a new Trip (generating the `trip_config.json`) and joining an existing one. When creating a trip and adding members, the UI must auto-suggest a unique slug based on the member's name, allowing the user to edit it. Ensure slugs are unique within the trip.
2. Build the Trip Dashboard layout. **Remember: UI must be in Italian.**
3. Implement the exact mathematical logic for the 'Quota Residua' (User Balance) per user, per currency:
   `User Balance = (Sum of 'split' amounts in all active 'contribution' transactions for this user) - (Sum of 'split' amounts in all active 'expense' transactions for this user)`. 
   Only use the latest version of transactions.