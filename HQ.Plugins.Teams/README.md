# HQ.Plugins.Teams

Microsoft Teams plugin for HQ — enables AI agents to send/receive messages, manage channels, and handle files in Teams.

## Azure Setup

### 1. Azure AD App Registration

1. Go to [Azure Portal](https://portal.azure.com) → Azure Active Directory → App registrations → New registration
2. Name: `HQ Teams Bot`
3. Supported account types: Single tenant (or multi-tenant if needed)
4. Note the **Application (client) ID** and **Directory (tenant) ID**
5. Go to Certificates & secrets → New client secret → copy the **Value**

### 2. API Permissions

Add the following Microsoft Graph **Application** permissions:

| Permission | Type | Description |
|---|---|---|
| `Team.ReadBasic.All` | Application | List joined teams |
| `Channel.ReadBasic.All` | Application | List channels |
| `ChannelMessage.Send` | Application | Send channel messages |
| `Files.ReadWrite.All` | Application | Upload/download files |

Grant admin consent for all permissions.

### 3. Azure Bot Registration

1. Go to Azure Portal → Create a resource → Azure Bot
2. Bot handle: `hq-teams-bot`
3. Pricing: Free (F0) for development
4. Microsoft App ID: Use the App Registration from step 1
5. Under Channels → Microsoft Teams → Enable
6. Under Configuration → Messaging endpoint: `https://<your-host>:3978/api/messages`

### 4. Local Development

For local dev, use [dev tunnels](https://learn.microsoft.com/en-us/azure/developer/dev-tunnels/) or ngrok:

```bash
# Using ngrok
ngrok http 3978

# Update Azure Bot messaging endpoint to the ngrok URL
# https://<random>.ngrok.io/api/messages
```

## ServiceConfig

| Field | Description | Default |
|---|---|---|
| `TenantId` | Azure AD tenant ID | — |
| `ClientId` | Azure AD app client ID | — |
| `ClientSecret` | Azure AD app client secret | — |
| `BotAppId` | Bot Framework app ID (same as ClientId typically) | — |
| `BotAppPassword` | Bot Framework app password (same as ClientSecret typically) | — |
| `ListenerPort` | HTTP listener port for Bot Framework | `3978` |
| `ListenerPath` | HTTP listener path | `/api/messages` |
| `AiPlugin` | Name of the AI service to route inbound messages to | — |
| `NotificationChannelId` | Default channel ID for outbound messages | — |
| `NotificationTeamId` | Default team ID for outbound messages | — |

## Tools

| Tool | Description |
|---|---|
| `send_teams_message` | Send a message to a Teams channel |
| `list_teams` | List teams the app has access to |
| `list_teams_channels` | List channels in a team |
| `send_teams_file` | Upload a file to a channel's SharePoint folder |
| `download_teams_file` | Download a file by drive item ID |

## Architecture

- **Outbound**: Microsoft Graph API via `TeamsGraphClient` (Azure.Identity `ClientSecretCredential`)
- **Inbound**: Embedded `HttpListener` → Bot Framework `BotFrameworkAdapter` → `TeamsBot` (extends `TeamsActivityHandler`)
- **Confirmations**: Adaptive Cards with `Action.Submit` buttons
- **Message routing**: Inbound messages routed to orchestrator via `ServiceResolver.GetOrchestrator()`
