# Socket Group Chat Application

A real-time multi-user group chat application built with **C#**, **TCP Socket Programming**, **WPF**, and **.NET 8**.

This project demonstrates how multiple clients can communicate through a centralized TCP server using socket connections in a local network environment.

---

# Features

* Real-time group chat
* Multi-client communication
* TCP socket networking
* WPF modern UI
* Emoji support
* Sticker support
* Username system
* Auto-scroll chat
* Modern chat bubble interface
* LAN/WiFi communication
* Beginner-friendly architecture

---

# Technologies Used

* C#
* .NET 8
* WPF
* TCP Sockets
* TcpClient
* TcpListener
* Async Programming

---

# Project Structure

```text
SocketChatApp
│
├── ChatServer     -> Console TCP Server
│
└── ChatClient     -> WPF Chat Client
```

---

# How It Works

The server acts as a central hub:

1. Multiple clients connect to the server
2. Each client sends messages to the server
3. The server broadcasts messages to all connected clients
4. All users receive messages in real-time

Architecture:

```text
Client A
     \
Client B ---> Server ---> Broadcast to all clients
     /
Client C
```

---

# Requirements

* Windows
* .NET 8 SDK
* Visual Studio 2022

---

# Run The Server

Open terminal inside:

```bash
ChatServer
```

Run:

```bash
dotnet run
```

If successful:

```text
SERVER STARTED
```

---

# Find Host IP Address

On the host machine:

```bash
ipconfig
```

Find:

```text
IPv4 Address
```

Example:

```text
192.168.1.41
```

---

# Configure Client Connection

Inside:

```csharp
ConnectToServer()
```

Change:

```csharp
await client.ConnectAsync("127.0.0.1", 5000);
```

To:

```csharp
await client.ConnectAsync("192.168.1.41", 5000);
```

Replace with your host machine IP address.

---

# Run The Client

Open terminal inside:

```bash
ChatClient
```

Run:

```bash
dotnet run
```

Or run directly from Visual Studio.

---

# LAN / WiFi Usage

All devices must:

* Be connected to the same WiFi/LAN network
* Use the host machine IPv4 address
* Allow firewall access for .NET applications

---

# Firewall Setup

If Windows asks:

```text
Allow access?
```

Click:

```text
Allow
```

Otherwise clients may fail to connect.

---

# Emoji Support

Users can send emojis such as:

* 😂
* 🔥
* ❤️
* 👍
* 😭

---

# Sticker Support

The application supports custom stickers using PNG images.

Sticker folder:

```text
ChatClient/Stickers
```

Example files:

```text
cute.png
frog.png
heart.png
```

---

# Screenshots

(Add screenshots here)

---

# Learning Objectives

This project helps beginners understand:

* TCP networking
* Client-server architecture
* Real-time communication
* WPF UI development
* Async socket programming
* Broadcasting messages
* Multi-user systems

---

# Future Improvements

* Private messaging
* Authentication
* File sharing
* Voice chat
* Database storage
* Online user list
* Message history
* Cloud deployment

---

# Author

Developed for learning socket programming and WPF application development using C# and .NET.
