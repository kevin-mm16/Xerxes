# Xerxes
# 🛡️ Xerxes – Modular Malware Simulator (Educational Red Team Tool)

**Xerxes** is a modular malware simulator built in **C#**, designed for educational and red teaming purposes. It replicates core post-exploitation tasks such as **system enumeration**, **file collection**, **persistence**, and **C2 beaconing**, all while maintaining modularity and clarity for researchers, students, and red teamers to learn from and extend.

> 🚨 **Ethical Notice**: This project is for **authorized testing and educational use only**. Do not deploy this on systems you do not own or have explicit permission to test.

---

## 📦 Features

| Module | Description |
|--------|-------------|
| 🛰️ Beacon | Periodic beaconing to a remote C2 server (HTTP) |
| 🗂️ FileGrabber | Collects documents, images, or sensitive files from predefined paths |
| 🧠 SystemScanner | Gathers information on the machine, network interfaces, users, and environment |
| 🔒 Persistence | Implements registry-based startup persistence |
| 🧪 Modular Design | Easily extendable class-based structure for red teaming exercises |

---

## 🧰 Tech Stack

- **C# (.NET Framework)** – core logic and malware simulation
- **Python Flask API** – simple C2 server backend (can be replaced with any framework)
- **WMI + Windows Registry** – for system-level interactions

---

## 🚀 Setup & Usage

### 📌 Prerequisites

- Windows machine (.NET Framework 4.x)
- Python 3.x (for the Flask C2 server)

### 💻 Running the Client (Xerxes)

```bash
# Compile the C# solution in Visual Studio or via command line
# Run the executable
Xerxes.exe
