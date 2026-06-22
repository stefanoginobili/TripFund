# Coding Style & Best Practices

## Language Policy (CRITICAL)
- **Code & Implementation:** 100% ENGLISH. Variables, methods, classes, comments, commits, and logs must be in English.
- **User Interface (UI):** 100% ITALIAN. All text visible to the user (buttons, labels, alerts, placeholders) must be in Italian.

## .NET MAUI Blazor Hybrid Best Practices
- **Componentization:** Break down complex UI into small, reusable `.razor` components.
- **Dependency Injection:** Use DI for all services (StorageService, RemoteStorageService, LocationService).
- **Separation of Concerns:** Keep C# logic in `@code` blocks or separate `.razor.cs` code-behind files. Do not mix heavy business logic inside HTML.
- **Async/Await:** All I/O operations (File System, Microsoft OneDrive API, GPS) must be asynchronous. Do not block the UI thread.
- **Styling:** Use standard CSS or a lightweight framework (like Bootstrap or Tailwind) integrated via Blazor. Assets will be provided externally.

## JSON Serialization
- Use `System.Text.Json`.
- Ensure options are set for human-readable output (`WriteIndented = true`).

## Testing Strategy (CRITICAL)
- **Narrow Integration Tests:** Focus testing efforts on high-level application features and user flows rather than granular unit tests for every single internal component. 
- **Mocking External Services:** All external HTTP calls must be mocked. Specifically, use **WireMock.Net** to mock the Microsoft OneDrive API responses (e.g., simulating 200 OK for uploads, or 403 Forbidden to test permission handling).
- **UI Integration Tests:** UI integration testing (e.g., using `bUnit` for the Blazor components) is highly encouraged to ensure the interface correctly reflects the underlying state (e.g., verifying that the Dashboard balances update after saving a new transaction).
- **Test-Driven Delivery:** Whenever you deliver a feature, you must also deliver the corresponding narrow integration tests that prove the high-level functionality works as expected.

## Constants & Configuration
- **AppConstants:** Centralize all system-wide strings (file names, metadata keys, content types, API URLs, and parameter keys) in `TripFund.App.Constants.AppConstants`. Always check this class before hardcoding magic strings.
- **Service Parameters:** When a service requires parameters stored in `known_trips.json`, always use the constants defined in `AppConstants.Metadata` to access the keys in the parameters dictionary.