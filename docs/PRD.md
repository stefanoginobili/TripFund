# Product Requirements Document (PRD): TripFund App

## Vision
A cross-platform mobile app (Android/iOS) to manage a "Shared Wallet" (la "cassa comune") for group travels. Unlike debt-settlement apps, TripFund assumes all money is pooled into a central fund. It tracks expected quotas, user contributions into the fund, multi-currency expenses paid *from* the fund, and individual user balances.

## Key Features
1. **Multi-Currency Management:** A single trip fund is split into different currencies (e.g., EUR and ARS). Users have a predefined "expected quota" they must contribute for each currency.
2. **Contributions (Versamenti):** Track when a user puts money *into* the shared wallet. A user can reach their expected quota in a single payment or multiple installments.
3. **Expenses (Spese):** - Paid *from* the shared wallet (deducted from a specific currency sub-fund).
   - There is NO "paid by" concept. We only track who registered the transaction and who the expense is allocated to (to deduct from their balance).
   - Can be allocated equally among all members or custom-split among a specific subset of members.
   - Must support attachments (receipt images/PDFs), location (GPS coordinates), and location name.
4. **Dashboard:** Displays overall fund status, expected vs. paid quotas per member, remaining balance per member, and total expenses.
5. **Offline-First:** The app must be fully functional without an internet connection.
6. **Trip Management & Home Page:** The app opens on a Home Page listing all configured trips. 
   - Trips are visually categorized into "Created by me" (I miei viaggi) and "Joined" (Viaggi condivisi con me).
   - At the bottom of the list, two main actions are always available: "Crea nuovo viaggio" and "Aggiungi viaggio esistente".
   - Users can edit trip details (name, description, dates, currencies, expected quotas).
   - Trip members must have an email address (for receiving receipts) and a simple Emoji avatar (to visually identify them in the UI without needing heavy image uploads).

## Target Audience & Environment
Groups of travelers using a centralized pool of money, often in remote locations with poor or no internet connectivity.