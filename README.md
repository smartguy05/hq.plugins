# HQ.Plugins
Plugins created to be used with HQ

---

## 🚧 Future Work

Planned enhancements include:
- **Telnyx Integration**: A plugin for SMS, voice, and communications via Telnyx.
- Additional external integrations and automation plugins.
- Improved configuration and extensibility for plugin management.

---

## Available Plugins

This repository currently includes plugins for integrating external services and enhancing HQ workflows. The available plugins are:

- **HQ.Plugins.UseMemos**  
  Memo-style storage and retrieval for textual or structured data within orchestrator flows. [Details](./HQ.Plugins.UseMemos/README.md)

- **HQ.Plugins.GoogleCalendar**  
  Integrates Google Calendar for creating, updating, and retrieving events via orchestrator logic. [Details](./HQ.Plugins.GoogleCalendar/README.md)

- **HQ.Plugins.SupportChannelKb**  
  (See plugin directory for support channel knowledge base integration.)

- **HQ.Plugins.WebSearch**  
  Enables web search via Kagi or Google Custom Search API. [Details](./HQ.Plugins.WebSearch/README.md)

- **HQ.Plugins.PythonRunner**  
  Run Python scripts or automation tasks from orchestrator workflows.

- **HQ.Plugins.HomeAssistantAssist**  
  Integrate with Home Assistant for smart home automation.

- **HQ.Plugins.Telegram**  
  Send and receive Telegram messages as part of your orchestration logic.

- **HQ.Logging.FileLogger**  
  File-based logging plugin for HQ events and flows.

> For plugin-specific usage and configuration instructions, see each plugin’s directory and README file.

---

# ICommand

# Config files

# CsProj changes
1. The .csproj file needs to be updated to allow for dynamic loading to do so edit the .csproj file of the project and add the following:

```
<PropertyGroup>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
</PropertyGroup>
```

2. Update the property group with the dotnet version and add EnableDynamicLoading, like below:
```
<PropertyGroup>
    <TargetFramework>net7.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <EnableDynamicLoading>true</EnableDynamicLoading>
</PropertyGroup>
```

3. Update references. Add Private = false and ExcludeAssets = runtime to HQ.Models reference. 

*NOTE: HQ.Common does not work for this yet*
```
<ItemGroup>
  <Reference Include="HQ.Common">
    <HintPath>..\..\hq\HQ\bin\Debug\net7.0\Plugins\HQ.Common.dll</HintPath>
  </Reference>
  <Reference Include="HQ.Models">
    <HintPath>..\..\hq\HQ\bin\Debug\net7.0\HQ.Models.dll</HintPath>
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
  </Reference>
</ItemGroup>
```

---

## Getting Started

Clone the repository and add the desired plugin(s) to your HQ solution:

```bash
git clone https://github.com/smartguy05/hq.plugins.git
cd hq.plugins
```

Each plugin’s folder contains its own README with installation, configuration, and usage instructions.

---

## Contributing

We welcome contributions! For new plugins, improvements, or bug reports, open an issue or pull request. See plugin documentation for guidelines.

---

## License

MIT License. See [LICENSE](./LICENSE).

---

_Note: Some search results are limited. For more plugin details, see the [GitHub repository](https://github.com/smartguy05/hq.plugins)._ 
