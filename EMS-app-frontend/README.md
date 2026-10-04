# Energy Management System (EMS) - Frontend Interface

This repository contains the frontend application for the Energy Management System. It is built using **React**, **Vite**, and **JavaScript**, providing a modern, fast, and responsive user interface for both Clients and Administrators to monitor smart energy devices.

## 🚀 Features

- **Role-Based Dashboards**: Secure access and distinct views for Administrators and regular Clients.
- **Real-Time Monitoring**: Interactive charts and data visualizations for energy consumption history.
- **WebSocket Integration**: Live notifications when energy consumption exceeds predefined limits.
- **Device Management**: Interfaces for linking and managing smart meters associated with users.
- **Admin Chat / Support**: Integrated communication channels for customer support.

## 🛠 Tech Stack

- **Framework**: React 18
- **Build Tool**: Vite (for rapid development and optimized builds)
- **HTTP Client**: Axios (configured with interceptors for JWT injection)
- **Styling**: Modular CSS and responsive design patterns
- **Real-Time**: SignalR / WebSockets for live alerts

## 💻 Local Development

### Prerequisites
- Node.js (v18+)
- npm or yarn

### Setup Instructions

1. **Install dependencies:**
   ```bash
   npm install
   ```

2. **Start the development server:**
   ```bash
   npm run dev
   ```
   The application will be available at `http://localhost:5173`.

3. **Build for production:**
   ```bash
   npm run build
   ```

### API Integration

The frontend expects the EMS backend services to be running (usually via Docker Swarm/Compose and Traefik). 
By default, the Vite configuration and Axios instances are set up to communicate with the Traefik API Gateway.

- **Auth Service**: Handled via `auth.docker.localhost`
- **Device Service**: Handled via `device.docker.localhost`
- **User Service**: Handled via `user.docker.localhost`

## 📁 Project Structure

- `src/components/` - Reusable UI components (Navbars, Modals, ChatWidgets)
- `src/api/` - Axios configurations and HTTP service interceptors
- `src/service/` - Dedicated logic for communicating with backend microservices
- `src/assets/` - Static files and images

---
*This project is part of the Distributed Systems (DS2025) assignment.*
