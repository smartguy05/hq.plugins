# HQ.Plugins.Slack

Slack integration plugin for the HQ orchestrator. Enables agents to send/receive messages, upload/download files, and act as a notification channel for confirmation flows via Slack's Socket Mode API.

## Slack App Setup

### 1. Create the App

1. Go to [api.slack.com/apps](https://api.slack.com/apps) and click **Create New App**
2. Choose **From an app manifest** (or from scratch)
3. Select your workspace

### 2. Enable Socket Mode

1. Navigate to **Socket Mode** in the sidebar
2. Toggle **Enable Socket Mode** on
3. Create an app-level token with the `connections:write` scope
4. Save the generated `xapp-...` token — this is your `AppLevelToken`

### 3. Configure Bot Token Scopes

Navigate to **OAuth & Permissions** and add these **Bot Token Scopes**:

| Scope | Purpose |
|-------|---------|
| `chat:write` | Send messages |
| `channels:read` | List public channels |
| `groups:read` | List private channels |
| `channels:history` | Read messages in public channels |
| `groups:history` | Read messages in private channels |
| `im:history` | Read direct messages |
| `files:read` | Download files |
| `files:write` | Upload files |

### 4. Enable Event Subscriptions

1. Navigate to **Event Subscriptions**
2. Toggle **Enable Events** on (Socket Mode handles the URL automatically)
3. Under **Subscribe to bot events**, add:
   - `message.channels` — messages in public channels
   - `message.groups` — messages in private channels
   - `message.im` — direct messages

### 5. Enable Interactivity

1. Navigate to **Interactivity & Shortcuts**
2. Toggle **Interactivity** on (Socket Mode handles the URL automatically)
3. This enables the Block Kit confirmation buttons

### 6. Install to Workspace

1. Navigate to **Install App**
2. Click **Install to Workspace** and authorize
3. Copy the **Bot User OAuth Token** (`xoxb-...`) — this is your `BotToken`

### 7. Invite the Bot

Invite the bot to any channel it should monitor:
```
/invite @YourBotName
```

## Plugin Configuration

The plugin config is stored as encrypted JSON in HQ's database. Required fields:

```json
{
  "Name": "HQ.Plugins.Slack",
  "Description": "Slack integration",
  "AppLevelToken": "xapp-1-...",
  "BotToken": "xoxb-...",
  "AiPlugin": "HQ.Plugins.OpenAi",
  "NotificationChannelId": "C0123456789"
}
```

| Field | Description |
|-------|-------------|
| `AppLevelToken` | App-level token (`xapp-...`) for Socket Mode WebSocket connection |
| `BotToken` | Bot User OAuth Token (`xoxb-...`) for Web API calls |
| `AiPlugin` | Name of the AI plugin to route inbound messages to (e.g. `HQ.Plugins.OpenAi`) |
| `NotificationChannelId` | Channel ID where confirmation messages are sent |

To find a channel ID: right-click a channel in Slack, select **View channel details**, and the ID is at the bottom of the modal.

## Available Tools

### `send_slack_message`
Send a text message to a channel or DM. Optionally attach a file.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `messageText` | string | Yes | The message text to send |
| `channelId` | string | No | Target channel/DM ID. Defaults to `NotificationChannelId` |
| `fileContent` | string | No | Base64-encoded file content to attach |
| `fileName` | string | No | Filename for the attachment |
| `fileType` | string | No | MIME type for the attachment |

### `upload_slack_file`
Upload a file to a channel using Slack's V2 upload API.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `channelId` | string | Yes | Target channel ID |
| `fileContent` | string | Yes | Base64-encoded file content |
| `fileName` | string | Yes | The filename |
| `fileType` | string | No | MIME type (defaults to `application/octet-stream`) |

### `download_slack_file`
Download a file by its Slack file ID and return the content as base64.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `fileId` | string | Yes | Slack file ID (starts with `F`) |

### `list_slack_channels`
List all non-archived public and private channels the bot can access. No parameters.

## Notification & Confirmation Flows

This plugin implements `INotificationPlugin`, enabling HQ's confirmation system. When another plugin requests confirmation:

1. A Block Kit message is posted to `NotificationChannelId` with buttons for each option
2. The user can either click a button or type the option name in a message
3. After selection, the buttons are removed from the original message (preventing double-clicks)
4. The confirmation result is sent back to the requesting plugin

## Special Commands

| Command | Description |
|---------|-------------|
| `/reset` | Clears the message cache for the current conversation |

## Build

```bash
dotnet build HQ.Plugins.Slack/HQ.Plugins.Slack.csproj
```

## Dependencies

- [SlackNet](https://github.com/soxtoby/SlackNet) v0.17.9 — Socket Mode client, API client, Block Kit types, event handlers
- `System.Net.Http.Json` v8.0.1
- `System.Text.Json` v8.0.5
