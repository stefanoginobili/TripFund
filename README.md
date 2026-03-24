# 🌍 TripFund 

[![.NET MAUI](https://img.shields.io/badge/.NET-MAUI-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/en-us/apps/maui)
[![Blazor](https://img.shields.io/badge/Blazor-Hybrid-512BD4?style=for-the-badge&logo=blazor)](https://dotnet.microsoft.com/en-us/apps/aspnet/web-apps/blazor)
[![Offline First](https://img.shields.io/badge/Architecture-Offline--First-success?style=for-the-badge)](#)
[![Platform](https://img.shields.io/badge/Platform-iOS%20%7C%20Android-lightgrey?style=for-the-badge)](#)

> **The ultimate, offline-first group travel fund manager.** <br>
> Track shared expenses, manage multiple currencies, and split the bill with your friends—even in the middle of nowhere.

## 💡 The Problem
Group trips are amazing, but managing the "shared fund" (la cassa comune) is usually a nightmare. People contribute in different currencies, at different times. You need to track who paid for dinner, who skipped the guided tour, and what the remaining balance is for each person. Oh, and you need to do all of this in remote locations **without internet access**.

## 🚀 The Solution: TripFund
TripFund is a modern, cross-platform mobile app built to solve exactly this. It's fully **offline-first**, meaning you can track every cent and snap photos of receipts completely offline. Once you're back at the hotel with Wi-Fi, the app seamlessly syncs everything using a unique **Google Drive** folder structure.

### ✨ Key Features
* **📴 100% Offline-First:** Designed for real travelers. Everything works without a connection.
* **💱 Multi-Currency Magic:** A single trip can have multiple currency sub-funds (e.g., EUR and ARS).
* **⚖️ Smart Splitting:** Divide expenses equally among the group, or define custom amounts for that one friend who didn't drink wine.
* **☁️ Bring Your Own Backend (BYOB):** Zero servers, zero subscriptions. TripFund syncs data directly to a shared **Google Drive** folder owned by your group.
* **📍 Location & Receipts:** Attach photos of receipts and save the GPS coordinates of where the expense happened.
* **📊 Clear Dashboard:** Instantly see the total fund status and every member's remaining balance.


## 🛠️ Tech Stack & Architecture

TripFund is built with **.NET MAUI Blazor Hybrid**, allowing us to share web UI components (HTML/CSS) while retaining full native access to the device's File System, Camera, and GPS.

**The "Exotic" Sync Engine:**
Instead of a traditional database, TripFund relies on an **Append-Only File System** architecture. 
* Transactions are saved locally as `.json` files inside unique versioned folders (e.g., `[Timestamp-GUID]/001_mario-rossi/data.json`).
* Attachments are renamed with GUIDs to prevent collisions.
* Deletions are handled via soft-delete `.deleted` files.
* When online, the app syncs this folder tree with Google Drive, gracefully handling merge conflicts manually via UI if two people edit the same expense simultaneously.


## 🚀 Getting Started

### Prerequisites
* [.NET 8 SDK](https://dotnet.microsoft.com/download) or later.
* Visual Studio 2022 (with .NET MAUI workload) or VS Code with the MAUI extension.
* A Google Cloud Console account (to generate the Google Drive API OAuth client ID).

### Installation
1. Clone the repo:
   ```bash
   git clone https://github.com/stefanoginobili/TripFund.git
   ```
2.  Open the solution in your IDE.
3.  Add your Google Drive API credentials in the configuration files (see `docs/SETUP.md` - *coming soon*).
4.  Build and deploy to your Android emulator or iOS simulator\!


## 🤖 Built with Vibe Coding

This project is an experiment in **AI-Assisted Vibe Coding**. The architecture, requirements, and coding constraints were carefully designed and fed to an LLM (Large Language Model) to generate the core components through iterative, highly-scoped prompts.

Want to see how the AI was instructed? Check out the [`GEMINI.md`](https://www.google.com/search?q=./GEMINI.md) master plan and the [`docs/`](https://www.google.com/search?q=./docs) folder\!

-----

<p align="center">
Made with ❤️ (and a lot of prompt engineering) for travelers everywhere.
</p>