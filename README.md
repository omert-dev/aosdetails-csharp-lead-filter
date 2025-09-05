# aosdetails-csharp-lead-filter

A lightweight **C# console application** that I built to support my business, **AOS Details** (auto mobile car detailing).  
It ingests IMAP email notifications (e.g., Facebook Marketplace and OfferUp), extracts customer inquiries, **scores lead quality**, logs results to CSV, and (optionally) sends email alerts for qualified leads.  

It took me a while honestly but I combined **automation**, **.NET development**, and **secure email integration** to streamline lead capture and customer acquisition for AOS Details.

---

## ✨ Here are the main features!
- 🔐 **IMAP integration for AOS Details** — securely connects to Gmail/Outlook using App Passwords to fetch detailing inquiries from Facebook Marketplace and OfferUp.  
- 📥 **Customer message parsing** — extracts sender, requested service (e.g., interior detail, ceramic coating), location, and budget directly from emails.  
- 🧠 **Smart lead scoring for car detailing** — filters out low-quality or irrelevant messages and prioritizes serious customers (e.g., “ceramic coating in Dallas,” “full detail before selling car”).  
- 🗂️ **Customer data logging** — appends all incoming leads into `leads.csv` for easy tracking, follow-ups, and marketing insights.  
- 📤 **Instant notifications** — alerts me when high-value requests come in so I can respond quickly and secure the booking.  
- 🛡️ **Security-first** — keeps AOS Details customer data safe by excluding secrets from git, using environment variables, and including only a sample config file publicly.  

---

## 🛠️ Tech Stack
- **C# / .NET 8**  
- [MailKit](https://github.com/jstedfast/MailKit) – IMAP/SMTP  
- [CsvHelper](https://joshclose.github.io/CsvHelper/) – CSV logging  
- **Microsoft.Extensions.Configuration** – flexible config & env var overrides  

---

## ⚙️ Setup & Usage

1. Clone the repo:  
   `git clone https://github.com/omert-dev/aosdetails-csharp-lead-filter.git`  
   `cd aosdetails-csharp-lead-filter`

2. Restore dependencies:  
   `dotnet restore`

3. Configure:  
   - Copy `appsettings.sample.json` → `appsettings.json`  
   - Fill in your email `Username` and leave `Password` blank (use environment variables instead)  
   - Enable IMAP in Gmail/Outlook and create an **App Password**  

4. Run:  
   `$env:IMAP__PASSWORD="your-16-char-app-password"`  
   `dotnet run`

The app will connect to your inbox, fetch recent AOS Details inquiries, score them, log to `leads.csv`, and optionally send alerts.

---

## 📊 Example CSV Output
| TimestampUtc        | Source     | FromName   | Title                                | City     | Price | Score | Qualified |
|---------------------|-----------|------------|--------------------------------------|----------|-------|-------|-----------|
| 2025-09-05T10:14:33Z| Facebook  | John Smith | "Looking for full interior detail"   | Plano    | 180   | 0.82  | True      |
| 2025-09-05T12:27:10Z| OfferUp   | Sarah Lee  | "Can you do ceramic coating today?"  | Dallas   | 450   | 0.91  | True      |
| 2025-09-05T14:05:22Z| Facebook  | Mike Jones | "Quick exterior wash, cheapest price"| Frisco   | 25    | 0.28  | False     |
| 2025-09-05T15:45:09Z| OfferUp   | Emma Davis | "Need full detail before selling car"| McKinney | 220   | 0.77  | True      |

---

## 🚀 Why This Project Matters
- Built specifically for **AOS Details**, my car detailing business, to automate how customer inquiries are captured and qualified.  
- Saves hours by eliminating the manual work of sorting through Marketplace and OfferUp messages.  
- Ensures I focus only on **serious prospects** (e.g., higher-value detailing jobs, customers in my service area, or urgent booking requests).  
- Shows practical application of **C# backend development**, IMAP email handling, and external libraries to solve a real-world business problem.  
- Emphasizes **security best practices**: no secrets in GitHub, environment variable usage, `.gitignore` to protect customer data.  
- Provides a foundation to grow into a **full customer booking and CRM system** for AOS Details.  

---

## 🔮 Future Improvements
- 📅 **Customer booking system** — extend into a full calendar where AOS Details clients can request time slots directly.  
- 🌐 **Customer-facing dashboard** — build a simple ASP.NET Core web app to let customers book details, get reminders, and view available packages.  
- 🤖 **Smarter lead scoring** — integrate ML.NET to predict the likelihood of booking based on history, service type, and budget.  
- 📊 **Business analytics** — generate insights into top-requested services, busiest cities, and conversion rates for AOS Details marketing.  
- 📦 **CRM integration** — track repeat customers, referrals, and service history in a lightweight CRM system.  
- 🐳 **Deployment ready** — containerize with Docker so the system can run automatically 24/7 to capture every AOS Details lead.  

---

## 📜 License
MIT  

---
