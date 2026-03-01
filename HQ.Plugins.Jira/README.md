# HQ.Plugins.Jira

Jira Cloud integration plugin for HQ. Provides 21 tools for managing issues, projects, sprints, worklogs, and users via the Jira REST API v3 and Agile REST API v1.0.

## Setup

### 1. Create a Jira API Token

1. Go to https://id.atlassian.com/manage-profile/security/api-tokens
2. Click **Create API token**
3. Give it a label (e.g. "HQ Integration") and copy the token

### 2. Configure the Plugin

Add the following config in HQ's plugin configuration:

```json
{
  "Name": "HQ.Plugins.Jira",
  "Description": "Integration with Jira Cloud for project management",
  "Domain": "yourcompany",
  "Email": "your-email@company.com",
  "ApiToken": "your-api-token-here"
}
```

- **Domain**: Your Atlassian subdomain (the `yourcompany` part of `https://yourcompany.atlassian.net`)
- **Email**: The Atlassian account email used for authentication
- **ApiToken**: The API token created in step 1

### 3. Required Permissions

The API token inherits the permissions of the user account. Ensure the account has:
- Browse Projects
- Create/Edit/Delete Issues
- Manage Sprints (for board/sprint tools)
- Log Work (for worklog tools)

## Tools

### Issues (10)

| Tool | Description |
|------|-------------|
| `jira_search_issues` | Search with JQL |
| `jira_get_issue` | Get full issue details |
| `jira_create_issue` | Create a new issue |
| `jira_update_issue` | Update issue fields |
| `jira_delete_issue` | Delete an issue and subtasks |
| `jira_assign_issue` | Assign or unassign an issue |
| `jira_transition_issue` | Change issue status (list or execute transitions) |
| `jira_add_comment` | Add a comment to an issue |
| `jira_get_comments` | Get comments on an issue |
| `jira_link_issues` | Link two issues |

### Projects (2)

| Tool | Description |
|------|-------------|
| `jira_list_projects` | List accessible projects |
| `jira_get_project` | Get project details with issue types and components |

### Boards & Sprints (4)

| Tool | Description |
|------|-------------|
| `jira_list_boards` | List boards, optionally by project |
| `jira_get_sprint` | Get sprints for a board (active/future/closed) |
| `jira_get_sprint_issues` | Get issues in a sprint |
| `jira_move_to_sprint` | Move issues into a sprint |

### Users (1)

| Tool | Description |
|------|-------------|
| `jira_search_users` | Search users by name or email |

### Worklogs (2)

| Tool | Description |
|------|-------------|
| `jira_add_worklog` | Log time on an issue |
| `jira_get_worklogs` | Get worklogs for an issue |

### Metadata (2)

| Tool | Description |
|------|-------------|
| `jira_get_issue_types` | Get issue types for a project |
| `jira_get_statuses` | Get statuses grouped by issue type |

## Architecture

Uses the **service-class annotating pattern** (like GoogleCalendar). Tool annotations live on `JiraService`, and `JiraCommand` delegates via `ServiceExtensions.GetServiceToolCalls<JiraService>()`.

- **JiraClient**: Internal HTTP wrapper handling Basic Auth, standard API (`/rest/api/3/`) and Agile API (`/rest/agile/1.0/`), plus ADF (Atlassian Document Format) conversion.
- **JiraService**: 21 annotated tool methods with JSON parameter schemas.
- **JiraCommand**: Thin command class extending `CommandBase<ServiceRequest, ServiceConfig>`.

## Notes

- Descriptions and comments use Atlassian Document Format (ADF) internally. The plugin converts plain text to/from ADF automatically.
- The `jira_transition_issue` tool serves dual purpose: omit `transitionId` to list available transitions, provide it to execute.
- Uses the new `/rest/api/3/search/jql` endpoint (not the deprecated `/rest/api/3/search`).
