**Task 1: Project Initialization & Models**

Read the documentation in the `docs/` folder, paying special attention to `SCHEMAS.md`. 
1. Create a new .NET MAUI Blazor Hybrid application. 
2. Set up the basic folder structure for Services, Models, and Pages. 
3. Implement the core C# Models (`TripConfig`, `Transaction`, `User`, etc.) exactly matching the JSON structures defined in `SCHEMAS.md`. Use string `slugs` as dictionary keys for users.
4. **Important Utilities to implement:**
   - A string utility to generate URL-safe slugs from user names.
   - A transaction ID generator that outputs a compact GMT timestamp + 8-char GUID prefix (format: `yyyyMMddTHHmmssZ-[guid-prefix]`). Use this generator for the `id` property of new transactions.