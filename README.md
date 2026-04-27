# 🤖 Smart Assistant — AI-Powered Scheduling & Reminder System

> Final Year Project | Federal Urdu University of Arts & Technology, Karachi

---

## 📌 Overview

Smart Assistant is an intelligent scheduling and reminder automation system that reads your emails, understands meeting requests, and manages your Google Calendar — all without manual input.

It handles the full lifecycle of a meeting:
- Detects a scheduling request in your inbox
- Checks your Google Calendar for conflicts
- Suggests an available time slot
- Sends a polite confirmation or rescheduling email automatically

Built with a **.NET Core** backend and a **.NET MAUI** frontend using **Razor Pages**.

---

## ✨ Features

### 📅 Auto Reminder Creation
Parses incoming emails to extract meeting context, attendee name, and requested time — then creates a reminder directly in Google Calendar without any manual input.

### ⚡ Conflict Detection & Slot Suggestion
When a requested time slot is already occupied, the system detects the conflict and suggests an alternative slot. The user can edit the suggested time before confirming.

### 📧 Automated Email Replies
Once a slot is selected, a polite confirmation email is sent automatically with a single click. If the user declines, a rescheduling email is sent instead. Some replies are generated using the **Gemini API** to produce context-aware, naturally worded responses tailored to the email content.

### 🏢 Out-of-Office Handling
If a meeting request arrives outside office hours, the system recognizes this and seeks permission before booking — rather than blindly creating a calendar event.

### 🔄 Auto-Update Reminders
When a follow-up email arrives from the same client with updated details — new time, changed attendee, or rescheduled meeting — the system finds the existing reminder and runs the same checks as a new request: verifies office hours and checks slot availability. If the new time is outside office hours, it seeks permission. If the slot is taken, it suggests an alternative.

### 🗑️ Auto-Delete Reminders
When an email arrives containing cancellation keywords (e.g. "cancel", "no longer", "won't be able to"), the system checks if the sender is a known client and whether an existing reminder is linked to them. If both match, the reminder is automatically deleted from Google Calendar — no manual cleanup needed.

---

## 🛠️ Tech Stack

| Layer | Technology |
|---|---|
| Backend | .NET Core |
| Frontend | .NET MAUI, Razor Pages |
| Email Integration | Gmail API |
| Calendar Integration | Google Calendar API |
| Authentication | OAuth 2.0 |
| Email Parsing | Custom parser |
| AI Email Replies | Gemini API |
| Database | MS SQL Server |

---

## 🏗️ Architecture

```
Incoming Email (Gmail API)
        ↓
  Email Parser
  (Extract name, time, intent, sender)
        ↓
  ┌─────────────────────────────────────────────────────────┐
  │  What kind of email is this?                            │
  │                                                         │
  │  NEW REQUEST          UPDATE              CANCELLATION  │
  │      ↓                   ↓                    ↓        │
  │  Office Hours?      Find Existing       Search Keywords │
  │  Yes → Continue     Reminder            (cancel/no      │
  │  No  → Seek         (by sender +        longer/etc.)   │
  │  Permission         context)                 ↓         │
  │      ↓                   ↓             Known Client +   │
  │  Slot Available?    Office Hours?       Reminder Exists? │
  │  Yes → Create       Yes → Continue          ↓          │
  │  No  → Suggest      No  → Seek         Auto Delete      │
  │  Slot               Permission               │          │
  │      ↓                   ↓                             │
  │              Slot Available?                            │
  │              Yes → Update Reminder                      │
  │              No  → Suggest New Slot                     │
  └─────────────────────────────────────────────────────────┘
        ↓
  User Confirms / Edits Time
        ↓
  Auto Email Reply (Gmail API)
  Confirmation or Rescheduling
```

---




## 👤 Author

**Anas Saleem**
- LinkedIn: [linkedin.com/in/anas-saleem07](https://linkedin.com/in/anas-saleem07)
- Email: m.anassaleem075@gmail.com

---

## 📄 License

This project is developed as a Final Year Project at Federal Urdu University of Arts & Technology, Karachi.
