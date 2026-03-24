**Task 4: Expense & Contribution Creation Flow**

Create two components in Italian: 'Aggiungi Spesa' (Add Expense) and 'Aggiungi Versamento' (Add Contribution). For both, the user selects the currency, inputs the total amount, and sets the `split` dictionary. 
- **Expense:** By default, divide the amount equally among all trip members' slugs. Provide UI toggles to exclude members or set exact custom amounts. Validate that the split sum equals the total amount.
- **Contribution:** The split usually belongs to a single member.
Set the `registeredBy` field to the slug of the current logged-in user. Include attachment functionality (renaming to `attachment_[guid].[extension]`) and GPS location using MAUI Essentials. Save using `LocalTripStorageService`.