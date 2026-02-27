# HQ.Plugins.UseMemos

## Overview

**HQ.Plugins.UseMemos** is a plugin for [HQ](https://github.com/smartguy05/hq) that enables your agent to seamlessly interact with a [UseMemos](https://usememos.com/) server. With this plugin, your AI agent can:

- **Access** user memos stored on a UseMemos server.
- **Add** new memos and resources.
- **Edit** existing memos and resources.
- **Delete** memos and resources.

This extends your agent's capabilities to manage notes, reminders, and structured resources directly from UseMemos, making it easy to integrate memory and resource management into your AI workflows.

---

## Installation

To use the plugin with HQ, follow these steps:

### 1. Compile or Obtain the Plugin DLL

- Build the project to produce the `HQ.Plugins.UseMemos.dll` file.
- Alternatively, obtain a prebuilt DLL from your CI/CD pipeline or release assets.

### 2. Place the Plugin DLL

- Copy the compiled `HQ.Plugins.UseMemos.dll` file into the `Plugins` directory of your HQ installation.

  ```
  /path/to/hq/Plugins/HQ.Plugins.UseMemos.dll
  ```

### 3. Configure the Plugin

- Place the configuration file `HQ.Plugins.UseMemos.json` into the `Configs` directory of your HQ installation.

  ```
  /path/to/hq/Configs/HQ.Plugins.UseMemos.json
  ```

#### Example `HQ.Plugins.UseMemos.json` Config

```json
{
  "ServerUrl": "https://your-memos-server.com",
  "ApiKey": "your-api-key",
  "AgentUserId": "agent-user-id-or-username"
}
```

- **ServerUrl**: The URL of your UseMemos server.
- **ApiKey**: The authentication token or API key for your UseMemos instance.
- **AgentUserId**: The user ID or username the agent should act as (optional, depends on server setup).

---

## Usage

Once the plugin is installed and configured, it will be automatically loaded by HQ on startup. The agent can then use the following capabilities:

- **List Memos**: Retrieve all memos for the configured user.
- **Add Memo/Resource**: Create new entries.
- **Edit Memo/Resource**: Update existing entries.
- **Delete Memo/Resource**: Remove unneeded entries.

### Supported Actions

The agent can perform actions such as:

- `GetMemos`
- `AddMemo`
- `EditMemo`
- `DeleteMemo`
- `GetResources`
- `AddResource`
- `EditResource`
- `DeleteResource`

These actions are exposed to the Orchestrator and available for use in your agent's workflows and prompts.

---

## Requirements

- **HQ** (latest version recommended)
- **UseMemos server** (self-hosted or cloud)
- Proper configuration in `HQ.Plugins.UseMemos.json`
- .NET runtime compatible with the plugin (see project details)

---

## Troubleshooting

- Ensure both the DLL and JSON config files are in the correct directories.
- Double-check your `ServerUrl` and `ApiKey` in the config file.
- Review logs from HQ for plugin load errors or connection issues.

---

## Contributing & Support

Pull requests and issues are welcome! For questions, please open an issue in the [main HQ repository](https://github.com/smartguy05/hq) or in this plugin's repository.

---

## License

This project is licensed under the MIT License.
