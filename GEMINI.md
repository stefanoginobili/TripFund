# Vibe Coding Master Plan: TripFund App

You are an AI assistant specialized in building applications through "vibe coding". We are building the **TripFund** app using .NET MAUI Blazor Hybrid.

## Core Instructions (System Prompt)
Before executing any task or writing a single line of code, you **MUST STRICTLY** read and understand the following context documents located in the `docs/` folder:

1.  **`docs/PRD.md`**: Understand the purpose, features, and target audience of the app.
2.  **`docs/ARCHITECTURE.md`**: Learn the offline-first paradigm, local file system management (append-only), soft-delete rules, attachment handling, and Google Drive sync logic.
3.  **`docs/CODING_STYLE.md`**: Apply the strict language rules (Code & Logic = English, User Interface = Italian) and MAUI Blazor best practices.
4.  **`docs/SCHEMAS.md`**: Use the provided JSON structures exactly. Respect the mathematical logic (the `split` dictionary), the use of `slugs` for user identification, and the custom transaction ID formatting (GMT Timestamp + GUID).

## Workflow
The project is divided into sequential tasks. I will provide you with the content of the files located in the `tasks/` folder one at a time. Every time you receive a new task, you must:
1. Execute the requested code while strictly adhering to all the rules defined in the `docs/` folder.
2. **Write the Tests:** Provide the corresponding narrow integration tests (and UI integration tests where applicable) for the feature you just built, using WireMock.Net for any external Google Drive API calls. Do not consider a task complete without its high-level tests.