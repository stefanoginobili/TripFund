# Coding Style & Best Practices

## Language Policy (CRITICAL)
- **Code & Implementation:** 100% ENGLISH. Variables, methods, classes, comments, commits, and logs must be in English.
- **User Interface (UI):** 100% ITALIAN. All text visible to the user (buttons, labels, alerts, placeholders) must be in Italian.

## .NET MAUI Blazor Hybrid Best Practices
- **Componentization:** Break down complex UI into small, reusable `.razor` components.
- **Dependency Injection:** Use DI for all services (StorageService, SyncService, LocationService).
- **Separation of Concerns:** Keep C# logic in `@code` blocks or separate `.razor.cs` code-behind files. Do not mix heavy business logic inside HTML.
- **Async/Await:** All I/O operations (File System, Google Drive API, GPS) must be asynchronous. Do not block the UI thread.
- **Styling:** Use standard CSS or a lightweight framework (like Bootstrap or Tailwind) integrated via Blazor. Assets will be provided externally.

## JSON Serialization
- Use `System.Text.Json`.
- Ensure options are set for human-readable output (`WriteIndented = true`).