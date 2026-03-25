**Task 8: Email Receipt for Contributions**

Enhance the 'Aggiungi Versamento' (Add Contribution) flow to support sending an email receipt to the user who made the contribution.

1. **Email Service Abstraction:** Create an `IEmailService` and its implementation. Use `.NET MAUI Essentials` (`Microsoft.Maui.ApplicationModel.Communication.Email`) to compose the email. 
2. **UI Flow (Italian):** Immediately after successfully saving a new contribution, display a confirmation dialog in Italian: *"Vuoi inviare una ricevuta via email a [Nome Utente]?"* (Yes/No).
3. **Data Retrieval:** If the user clicks Yes, retrieve the target user's email address from the `members` dictionary inside the `trip_config.json`.
4. **HTML Message Content:** Pre-fill the email using `BodyFormat = EmailBodyFormat.Html`. The Subject and Body must be in Italian. Design a simple, clean HTML template for the receipt. Include the Trip Name, Contribution Amount, Currency, Date, and the new Total Balance for that user.
5. **Testing:** Write the corresponding narrow integration tests. You MUST mock the `IEmailService` so the tests do not attempt to trigger the OS native email client. Verify that the flow correctly prompts the user and calls the service with the HTML content if accepted.