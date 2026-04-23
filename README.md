<p align="center">
  <img src="docs/logo.png" alt="TripFund Logo" width="180">
</p>

# 🌍 TripFund 

[![.NET MAUI](https://img.shields.io/badge/.NET-MAUI-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/en-us/apps/maui)
[![Blazor](https://img.shields.io/badge/Blazor-Hybrid-512BD4?style=for-the-badge&logo=blazor)](https://dotnet.microsoft.com/en-us/apps/aspnet/web-apps/blazor)
[![Offline First](https://img.shields.io/badge/Architecture-Offline--First-success?style=for-the-badge)](#)
[![Platform](https://img.shields.io/badge/Platform-iOS%20%7C%20Android-lightgrey?style=for-the-badge)](#)

> **The ultimate, offline-first Shared Wallet manager for group travels.** <br>
> Collect predefined quotas, manage multi-currency funds, and track group expenses from a central shared pot—even in the middle of nowhere.

---

## 💡 The Problem
Most travel apps focus on "who owes who" (e.g., Alice paid for dinner, Bob paid for gas). But real group travels often rely on a **"Cassa Comune" (Shared Wallet)**: everyone is supposed to put a predefined amount of money into a shared pot *before* the trip starts, often in multiple currencies. 

The nightmare isn't figuring out who paid the waiter; it's tracking who has actually deposited their expected quota into the pot, and deducting the daily group expenses from that shared fund, especially when you are completely offline.

## 🚀 The Solution: TripFund
TripFund flips the standard expense-splitter model. It is a pure **Shared Wallet Tracker**. 
Participants pour money *into* the fund (Contributions), and money is taken *out* of the fund to pay for things (Expenses). You only need to track who registered the operation and whose balance the expense should be deducted from.

### ✨ Key Features
* **📴 100% Offline-First:** Designed for real travelers. Everything works without a connection.
* **💱 Multi-Currency Magic:** A single trip can have multiple currency sub-funds (e.g., EUR and ARS) with predefined expected quotas for each participant.
* **📥 Quota Tracking:** Easily see who has deposited their full expected quota into the shared fund and who is still lagging behind.
* **📤 Fund-Based Expenses:** Log expenses paid *from* the shared wallet. No more "paid by" confusion. Just select who the expense applies to (to deduct it from their personal balance).
* **☁️ Bring Your Own Backend (BYOB):** Zero servers, zero subscriptions. TripFund syncs data directly to a shared **Microsoft OneDrive** folder owned by your group.
* **📍 Location & Receipts:** Attach photos of receipts and save the GPS coordinates of where the expense happened.


## 🛠️ Tech Stack & Architecture

TripFund is built with **.NET MAUI Blazor Hybrid**, allowing us to share web UI components (HTML/CSS) while retaining full native access to the device's File System, Camera, and GPS.

### 🧩 The "Exotic" Sync Engine
Instead of a traditional database, TripFund relies on an **Append-Only File System** architecture. 
* **Transaction Atomic Versions:** Changes are saved locally as `.json` files inside unique versioned folders (`001_NEW_device-id`, `002_UPD_device-id`).
* **Conflict Resolution via DAG:** The versioning system operates as a Directed Acyclic Graph (DAG) of folders, allowing the app to detect and resolve divergence through a dedicated UI.
* **Metadata & Immutability:** Each leaf folder contains a `.tripfund` file with author, device, and timestamp information, ensuring auditability.
* **ZIP-Based Sync:** When online, the app bundles local changes into differential ZIP packages and syncs them with Microsoft OneDrive, ensuring atomicity across devices.

Check out the [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for a deep dive into this unique system.


## 🚀 Getting Started

### Prerequisites
* [.NET 8 SDK](https://dotnet.microsoft.com/download) or later.
* Visual Studio 2022 (with .NET MAUI workload) or VS Code with the MAUI extension.
* A Microsoft Entra ID (Azure AD) application (to generate the Client ID for OneDrive access).

### Installation
1. Clone the repo:
   ```bash
   git clone https://github.com/stefanoginobili/TripFund.git
   ```
2. Open the solution in your IDE.
3. Configure your Microsoft Auth credentials (see [`docs/INTEGRATIONS.md`](docs/INTEGRATIONS.md)).
4. Build and deploy to your Android emulator or iOS simulator!


## 🤖 Built with Vibe Coding

This project is an experiment in **AI-Assisted Vibe Coding**. The architecture, requirements, and coding constraints were carefully designed and fed to an LLM (Large Language Model) to generate the core components through iterative, highly-scoped prompts.

Want to see how the AI was instructed? Check out the [`GEMINI.md`](GEMINI.md) master plan and the [`docs/`](docs/) folder!

---

<p align="center">
Made with ❤️ (and a lot of prompt engineering) for travelers everywhere.
</p>