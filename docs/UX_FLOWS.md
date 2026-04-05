# UX & UI Flows

This document strictly defines the user flows and UI layouts. **All UI text must be in Italian.**

## 1. Landing Page
- **Layout:** Vertical scrolling list of configured trips. Each row shows: Trip Name, Dates, and Member count.
- **Top Bar:** A gear icon ⚙️ (top-right) opens "Impostazioni Globali" (Flow 10).
- **Bottom:** Two main buttons: "Crea nuovo viaggio" and "Aggiungi viaggio esistente".

## 2. Join Existing Trip
- **Action:** Opens a Google Drive folder picker.
- **Result:** Upon selecting a valid folder, the app registers the trip in `known_trips.json` and navigates to the **Trip Dashboard** (Flow 4).

## 3. Create New Trip
- **Step A (Drive Picker):** User selects a parent folder on Google Drive.
- **Step B (Initial Data Form):** A dialog to input Name, Slug (auto-calculated from Name but editable; regex: `^[a-z0-9-_]+$`), Dates, and at least one Currency. **Crucial:** Slugs must be unique globally across the user's trips.
- **Result:** Creates the trip folder, generates `trip_config.json`, registers it locally, and navigates to the **Trip Dashboard** (Flow 4).

## 4. Trip Dashboard
- **Top Bar:** Back button (left), Edit pencil icon ✏️ (right) leading to **Edit Trip** (Flow 5).
- **Header:** Horizontal currency selector (max 3 currencies). Selecting a currency changes the context of the entire page.
- **Summary:** Large display of the Total Remaining Balance (Saldo) and, slightly smaller, the Total Contributed (Totale versato) in the selected currency.
- **Progress Bar:** A visual progress bar immediately below the summary showing the overall funding status. 
  - `Target (100%) = expectedQuotaPerMember * Number of Members`
  - `Current Progress = Total Contributed by all members`
- **Quick Actions:** Two buttons immediately below the progress bar: "Registra Versamento" (Flow 6) and "Registra Spesa" (Flow 7).
- **Members List:** Vertical list. Each row shows: Emoji, Name, Total Contributed, and Remaining Balance. Tapping a row opens the **Member Dashboard** (Flow 8).
- **Transactions List:** Vertical list of recent transactions. Tapping a row opens **Transaction Detail** (Flow 9).

## 5. Edit Trip
- **Top Bar:** Back button (left).
- **Form:** Edit Name, Dates, Members, and Currencies.
- **Trip Slug:** Displayed as read-only.
- **Members Management:** Inline creation (empty row). Requires Name, Slug, Email, and Emoji (default "👤"). **Crucial:** Member slug is editable ONLY during creation. Once saved, it becomes read-only. Slugs must be unique within the trip.
- **Currencies Management:** Add/Remove only. Adding prompts for Code (e.g., "EUR"), Symbol (e.g., "€"), and **Expected Quota per Member** (e.g., "1000"). Codes must be unique.
- **Bottom:** A "Danger Zone" button to "Elimina Viaggio". **Logic:** This deletes the local folder and registry entry, but DOES NOT delete data on Google Drive.

## 6. Add Contribution (Registra Versamento)
- **Top Bar:** Back button (left).
- **Header:** Contextual currency selector (pre-selected based on the dashboard's state).
- **Form:** - Datetime picker (default: now).
  - Large text input for Amount.
  - Dropdown to select the contributing Member (shows Emoji, Name, and current contributed total in the selected currency).
  - Switch: "Invia ricevuta via email" (Send email receipt).
- **Bottom:** Submit button. Creates the transaction on the file system.

## 7. Add Expense (Registra Spesa)
- **Top Bar:** Back button (left).
- **Header:** Contextual currency selector (pre-selected based on the dashboard).
- **Form:**
  - Large text input for Amount.
  - Text input for Description.
  - Datetime picker (default: now).
- **Split Logic (The Members List):** Shows all members. Each row has:
  - Emoji & Name.
  - A main Switch (default: ON) to include/exclude the member from this expense.
  - If ON, show an inner Switch (Auto/Manual) and a Number Input.
  - If Auto: Number input is disabled and shows the app-calculated equal split.
  - If Manual: Number input is enabled for custom amounts.
- **Attachments:** Buttons for "Scatta foto" (Camera) and "Seleziona file" (Picker).
- **Location:** Button to add GPS location (fetches Latitude, Longitude, Name).
- **Bottom:** Submit button.

## 8. Member Dashboard
- **Top Bar:** Back button (left).
- **Header:** User Emoji, Name, and Email. Horizontal currency selector.
- **Summary:** Large display of Total Contributed and, slightly smaller, Remaining Balance for the selected currency.
- **Progress Bar:** A visual progress bar immediately below the summary showing this specific member's funding status.
  - `Target (100%) = expectedQuotaPerMember`
  - `Current Progress = Total Contributed by this specific user`
- **Action:** A "Registra Versamento" button (pre-contextualized to this user and currency).
- **Transactions List:** List of transactions involving this user. For expenses, show ONLY the split amount attributed to this user, not the total expense amount. Tapping opens **Transaction Detail** (Flow 9).

## 9. Transaction Detail
- **Top Bar:** Back button (left). 3-dot menu (right) containing: Edit ✏️ and Delete 🗑️ (Red).
- **View:** Read-only display of Type, Currency, Date/Time, Total Amount, Description, Author, Split details per member, Attachments, and Location.
- **Edit Action:** Opens Flow 6 or Flow 7 in edit mode. Saving creates a new version folder.
- **Delete Action:** Creates a `.deleted` version folder.

## 10. Global Settings (Onboarding)
- **First Launch Blocker:** If `app_settings.json` is missing, the app cannot proceed.
- **Form:** Requires "Nome Autore". As the user types, the "Slug Autore" is auto-generated and displayed as read-only.
- **Save:** Writes to `app_settings.json` and proceeds to Landing Page. Can be accessed later via the gear icon.

## 11. Conflict Resolution
- **Trigger:** Sync detects multiple active folders for the same transaction version.
- **UI:** The Trip Dashboard displays an alert banner at the top listing conflicted transactions.
- **Action:** Tapping a conflict opens a modal showing side-by-side basic info of the conflicting versions. The user picks the correct one, which is saved as the new highest version.