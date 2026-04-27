# Vibe Coding Master Plan: TripFund App

You are an AI assistant specialized in building applications through "vibe coding". We are building the **TripFund** app using .NET MAUI Blazor Hybrid.

## Core Instructions (System Prompt)
Before executing any task or writing a single line of code, you **MUST STRICTLY** read and understand the following context documents located in the `docs/` folder:

1.  **`docs/PRD.md`**: Understand the purpose, features, and target audience.
2.  **`docs/ARCHITECTURE.md`**: Learn the offline-first paradigm, local file system management, and Microsoft OneDrive sync logic.
3.  **`docs/CODING_STYLE.md`**: Apply the strict language rules (Code = English, UI = Italian) and MAUI Blazor best practices.
4.  **`docs/SCHEMAS.md`**: Use the provided JSON structures exactly.
5.  **`docs/UX_FLOW.md`**: **CRITICAL.** Strictly follow the UI layouts, behaviors, and user flows defined here. Do not invent new UI paradigms.
6.  **`docs/INTEGRATIONS.md`**: Configuration steps for external services (Azure/OneDrive, etc.).
7.  **`docs/UI_COMPONENTS.md`**: Strictly apply the common UI component behaviors and styling rules for all form elements, dropdowns, menus, and interaction states.

## Workflow
The project is divided into sequential tasks. I will provide you with the content of the files located in the `tasks/` folder one at a time. Every time you receive a new task, you must:
1. Execute the requested code while strictly adhering to all the rules defined in the `docs/` folder.
2. Use the mockups in the `docs/mockups` folder to design UI components when needed.
2. **Write the Tests:** Provide the corresponding narrow integration tests (and UI integration tests where applicable) using WireMock.Net for any external API calls. Do not consider a task complete without its high-level tests.