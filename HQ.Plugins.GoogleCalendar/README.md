**Below is auto-generated documentation created by ChatGPT, it has not been review yet for accuracy**

# HQ.Plugins.GoogleCalendar

This plugin integrates Google Calendar functionalities into the **HQ** framework. By leveraging this plugin, you can create, update, and retrieve calendar events using AI-driven orchestration logic.

## Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Installation](#installation)
- [Setup and Configuration](#setup-and-configuration)
- [Usage](#usage)
    - [Creating Events](#creating-events)
    - [Listing Events](#listing-events)
    - [Updating Events](#updating-events)
    - [Deleting Events](#deleting-events)
- [Authentication and Scopes](#authentication-and-scopes)
- [Error Handling](#error-handling)
- [Contributing](#contributing)
- [License](#license)

---

## Overview

**HQ.Plugins.GoogleCalendar** is a plugin built for the **HQ** platform. It allows orchestrator flows and AI-driven bots to interact seamlessly with Google Calendar by:

- **Creating** calendar events,
- **Retrieving** existing calendar events,
- **Updating** event details,
- **Deleting** events.

This plugin is ideal for scheduling tasks, reminders, and meetings within AI-powered workflows without manually switching between different tools.

---

## Features

1. **Easy Integration** – Quickly add Google Calendar functionality to existing HQ workflows.
2. **Full Event Lifecycle** – Create, read, update, and delete events.
3. **Google OAuth 2.0** – Securely authenticate and authorize API requests.
4. **Multiple Calendars** – Specify a particular calendar or query events across calendars.

---

## Installation

1. **Clone** this repository (or the entire `hq.plugins` monorepo if that applies):
   ```bash
   git clone https://github.com/smartguy05/hq.plugins.git
   cd hq.plugins/HQ.Plugins.GoogleCalendar
   ```

2. **Add the Plugin** to your HQ setup. Depending on how your orchestrator is structured, you will usually:
    - Copy the plugin folder into your orchestrator’s `plugins` directory, or
    - Reference it in your orchestrator’s plugin configuration file.

3. **Restore Dependencies** (if required):
    - For .NET projects, run:
      ```bash
      dotnet restore
      ```
    - For other frameworks or package managers, follow the relevant installation steps.

---

## Setup and Configuration

1. **Create a Google Cloud Project** (if you don’t already have one).
2. **Enable the Google Calendar API** in the [Google Cloud Console](https://console.cloud.google.com/).
3. **Obtain OAuth 2.0 Credentials**:
    - Navigate to **APIs & Services** \> **Credentials**.
    - Create or configure an existing **OAuth Client ID** (e.g., for a Web application or a desktop app).
    - Download the `credentials.json` file or copy the **client ID** and **client secret**.

4. **Configure the Plugin**:
    - Place your `credentials.json` in the plugin’s working directory (if you’re using file-based credentials).
    - Or add the relevant client ID and secret to your environment variables/configuration for the plugin to access, for example:
      ```bash
      GOOGLE_CLIENT_ID=<YOUR_CLIENT_ID>
      GOOGLE_CLIENT_SECRET=<YOUR_CLIENT_SECRET>
      ```

5. **Set Up Token Storage** (for the OAuth refresh token):
    - By default, tokens may be stored in a token file (e.g., `token.json`).
    - Make sure your orchestrator can access and securely store tokens.

---

## Usage

Once installed and configured, you can access the **GoogleCalendar** plugin’s functionality through your HQ flows. Below are common scenarios:

### Creating Events

```csharp
// Example snippet using the plugin
var calendarPlugin = new GoogleCalendarPlugin(/* config parameters */);

var newEvent = await calendarPlugin.CreateEventAsync(
    calendarId: "primary",
    title: "Team Meeting",
    description: "Discuss project updates",
    startTime: DateTime.Now.AddDays(1).AddHours(9),
    endTime: DateTime.Now.AddDays(1).AddHours(10)
);
```

### Listing Events

```csharp
var events = await calendarPlugin.ListEventsAsync(
    calendarId: "primary",
    startTime: DateTime.Today,
    endTime: DateTime.Today.AddDays(1)
);

// Process the list of events
foreach (var calendarEvent in events)
{
    Console.WriteLine($"{calendarEvent.Id}: {calendarEvent.Summary}");
}
```

### Updating Events

```csharp
await calendarPlugin.UpdateEventAsync(
    calendarId: "primary",
    eventId: "<EXISTING_EVENT_ID>",
    newTitle: "Updated Meeting Title",
    newDescription: "Updated description for the meeting",
    newStartTime: DateTime.Now.AddDays(2).AddHours(11),
    newEndTime: DateTime.Now.AddDays(2).AddHours(12)
);
```

### Deleting Events

```csharp
await calendarPlugin.DeleteEventAsync(
    calendarId: "primary",
    eventId: "<EXISTING_EVENT_ID>"
);
```

---

## Authentication and Scopes

During the first run, the plugin will request OAuth consent. Once authenticated, it will store the refresh token for future requests. The plugin typically requests the following Google Calendar scope:

```
https://www.googleapis.com/auth/calendar
```

This allows managing and viewing calendars and events. Refer to [Google’s OAuth 2.0 docs](https://developers.google.com/identity/protocols/oauth2) if you need more granular permission scopes.

---

## Error Handling

If the plugin fails to create or retrieve events, it will throw exceptions or return error messages that you can capture in your HQ logic. Common issues include:

- Missing or invalid credentials.
- Insufficient API permissions.
- Network connectivity problems.

Handle these appropriately in your flow and consider exposing error messages to the orchestration layer so that AI-driven decisions can be made (e.g., retrying or notifying a user).

---

## Contributing

Contributions are welcome! To contribute:

1. [Fork](https://github.com/smartguy05/hq.plugins/fork) this repository.
2. Create a **feature branch** for your changes:
   ```bash
   git checkout -b feature/new-plugin-feature
   ```
3. Commit your changes and push to your fork.
4. Create a **Pull Request** into the main repository. A maintainer will review your changes.

Please follow the existing coding style and patterns when making contributions.

---

## License

This plugin is released under the [MIT License](../LICENSE) (or whichever license is relevant for your repository). See the [LICENSE](../LICENSE) file for details.

---

### Questions or Feedback?

Feel free to open a [GitHub Issue](https://github.com/smartguy05/hq.plugins/issues) for bug reports, feature requests, or general discussion regarding **HQ.Plugins.GoogleCalendar**.

We appreciate your feedback and contributions!

--- 

> **Note**: Make sure to update any file paths (e.g., `../LICENSE`) depending on your project’s actual structure. Also, adapt the instructions for your specific orchestrator environment and deployment requirements.