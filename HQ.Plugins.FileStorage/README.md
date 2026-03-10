# HQ.Plugins.FileStorage

Docker-based sandboxed file workspaces for HQ agents. Provides persistent, isolated filesystems with Python 3 and Node.js pre-installed, no network access, and comprehensive file access logging.

## Docker Setup

Build the workspace image (required before first use):

```bash
cd HQ.Plugins.FileStorage
docker build -t hq-workspace:latest -f Dockerfile .
```

## Configuration

| Field | Default | Description |
|-------|---------|-------------|
| `DockerHost` | auto-detect | Docker socket URI. Auto-detects npipe (Windows) or unix socket (Linux). |
| `DefaultImage` | `hq-workspace:latest` | Docker image for workspaces |
| `MemoryLimitMb` | `512` | Memory limit per workspace |
| `CpuShares` | `1024` | CPU scheduling weight |
| `PidsLimit` | `100` | Maximum process count (prevents fork bombs) |
| `WorkspaceSizeMb` | `256` | Size of /tmp tmpfs mount |

## Tools

| Tool | Description |
|------|-------------|
| `workspace_create` | Create a persistent workspace container |
| `workspace_destroy` | Stop and remove a workspace + its volume |
| `workspace_list` | List all HQ workspaces |
| `workspace_status` | Get workspace container details |
| `workspace_write_file` | Write text or binary files |
| `workspace_read_file` | Read files (returns base64) |
| `workspace_list_files` | List directory contents |
| `workspace_delete_file` | Delete files or directories |
| `workspace_exec` | Execute shell commands |
| `workspace_exec_script` | Write + execute Python/Node scripts |
| `workspace_copy_between` | Copy files between workspaces |

## Security Model

- **No network**: `NetworkMode=none` — zero network access
- **Read-only rootfs**: Only `/workspace`, `/shared`, `/tmp`, `/run` are writable
- **No capabilities**: All Linux capabilities dropped
- **Non-root user**: Runs as `agent` (uid 1000)
- **Resource limits**: Memory cap, PID limit, CPU shares
- **No privilege escalation**: `no-new-privileges=true`
- **Protected paths**: Cannot delete system directories
- **Input validation**: Workspace IDs must be alphanumeric + hyphens
- **Audit logging**: Every file operation logged with `[FileAccess]` prefix
