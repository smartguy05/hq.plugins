# HQ.Plugins.Telegram

**HQ.Plugins.Telegram** is a plugin for [HQ](https://github.com/smartguy05/hq) that enables communication with your AI Agent through the Telegram messaging service. This makes it possible to send commands and receive responses from your AI Agent using Telegram, perfect for remote control, monitoring, and interactive conversations.

## Features

- **Telegram Integration:** Send messages to your AI Agent and receive responses directly via Telegram.
- **Secure Communication:** Interact privately and securely with your Agent.
- **Simple Setup:** Minimal configuration required to get started.

## Installation

1. **Compile the Plugin**
    - Build the `HQ.Plugins.Telegram` project using your preferred .NET build tool (e.g., Visual Studio, `dotnet build`).

2. **Copy the Compiled DLL**
    - After building, locate the compiled DLL file (e.g., `HQ.Plugins.Telegram.dll`).
    - Copy this DLL into the `Plugins` directory of your main **HQ** application.

3. **Configure the Plugin**
    - Create or edit the configuration file `HQ.Plugins.Telegram.json`.
    - Place this configuration file in the `Configs` directory of your **HQ** application.

## Configuration

The configuration file should look similar to the following example:

```json
{
  "BotToken": "YOUR_TELEGRAM_BOT_TOKEN",
  "AllowedUserIds": [123456789, 987654321]
}
```

- **BotToken**: Your Telegram Bot API token. You can obtain this by creating a bot through [BotFather](https://core.telegram.org/bots#botfather).
- **AllowedUserIds**: An array of Telegram user IDs that are permitted to interact with your Agent.

## Usage

1. **Start HQ**
    - Run the main HQ application. The plugin will be automatically detected and loaded if the DLL and config file are in the correct directories.

2. **Interact via Telegram**
    - Send messages to your Telegram bot from an allowed account.
    - Your messages will be routed to the AI Agent, and the Agent's responses will be sent back to you via Telegram.

## Troubleshooting

- **Plugin Not Detected:** Ensure the DLL is in the `Plugins` directory and the config file is in the `Configs` directory.
- **No Response in Telegram:** Double-check your bot token and allowed user IDs in the configuration file.
- **Permissions:** Make sure the bot has permission to read messages and send replies in your Telegram chat.

## Contributing

If you wish to add features or fix bugs, please submit a pull request or open an issue on the [GitHub repository](https://github.com/smartguy05/hq.plugins).

## License

This project is licensed under the MIT License.

---

**HQ.Plugins.Telegram** makes it easy to manage your AI Agent from anywhere using Telegram. Enjoy seamless communication and control!
