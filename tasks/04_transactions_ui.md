**Task 4: Expense & Contribution Creation Flow**

Create two components in Italian: 'Aggiungi Spesa' (Add Expense) and 'Aggiungi Versamento' (Add Contribution). For both, the user selects the currency, inputs the total amount, and sets the `split` dictionary. 

- **Expense:** - **CRITICAL:** Do NOT include a "Paid By" field. Expenses are paid from the shared fund.
  - The `split` defines who consumed the expense (to deduct from their balance). By default, divide the amount equally among all trip members' slugs. Provide UI toggles to exclude members or set exact custom amounts. Validate that the split sum equals the total amount.
- **Contribution:** - The `split` belongs to the member(s) who are putting money INTO the shared fund. Usually, it's just one member making an installment toward their expected quota.

For both types:
Set the `registeredBy` field to the slug of the current logged-in user. Include attachment functionality (renaming to `attachment_[guid].[extension]`) and GPS location using MAUI Essentials. Save using `LocalTripStorageService`. Provide the necessary narrow integration tests.