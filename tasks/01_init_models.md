**Task 1: Project Initialization & Models**

Read the documentation in the `docs/` folder, paying special attention to `SCHEMAS.md`. 
1. Create a new .NET MAUI Blazor Hybrid application. 
2. Set up the basic folder structure for Services, Models, and Pages. 
3. Implement the core C# Models (`TripConfig`, `Transaction`, `User`, `LocalTripRegistry`, `AppSettings`) exactly matching the JSON structures defined in `SCHEMAS.md`. **Note that the expected quota is now a property (`expectedQuotaPerMember`) inside the Currency object, not a separate dictionary.**
4. **Important Utilities to implement:**
   - A string utility to generate URL-safe slugs from user names.
   - A transaction ID generator that outputs a compact GMT timestamp + 8-char GUID prefix (format: `yyyyMMddTHHmmssZ-[guid-prefix]`). Use this generator for the `id` property of new transactions.
5. Initialize an xUnit test project alongside the main MAUI app. Install necessary NuGet packages for testing (e.g., FluentAssertions, bUnit, WireMock.Net) and set up the basic test infrastructure.