# Product Requirements Document (PRD): TripFund App

## Vision
A cross-platform mobile app (Android/iOS) to manage shared group travel funds (la "cassa comune"). It tracks contributions, multi-currency expenses, and user balances. 

## Key Features
1. **Multi-Currency Management:** A single trip fund can be split into different currencies (e.g., EUR and ARS). Users have expected contribution quotas for each currency.
2. **Contributions (Versamenti):** Track when and how much each user contributes to the shared fund.
3. **Expenses (Spese):** - Deducted from a specific currency sub-fund.
   - Can be split equally among all members or custom-split among a specific subset of members.
   - Must support attachments (receipt images/PDFs), location (GPS coordinates), and location name.
4. **Dashboard:** Displays overall fund status, balance per currency, remaining quotas per member, and total expenses.
5. **Offline-First:** The app must be fully functional without an internet connection.
6. **Trip Management:** Support for multiple concurrent trips. Users can create a new trip or join an existing one.

## Target Audience & Environment
Groups of travelers, often in remote locations with poor or no internet connectivity.