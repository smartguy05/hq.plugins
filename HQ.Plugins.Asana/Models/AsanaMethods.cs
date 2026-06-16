namespace HQ.Plugins.Asana.Models;

public static class AsanaMethods
{
    // Workspaces
    public const string ListWorkspaces = "list_workspaces";

    // Users
    public const string GetUser = "get_user";

    // Tasks
    public const string CreateTask = "create_task";
    public const string GetTask = "get_task";
    public const string UpdateTask = "update_task";
    public const string DeleteTask = "delete_task";
    public const string SearchTasks = "search_tasks";
    public const string GetTasks = "get_tasks";
    public const string SetParentForTask = "set_parent_for_task";
    public const string AddTaskFollowers = "add_task_followers";

    // Projects
    public const string GetProjects = "get_projects";
    public const string GetProject = "get_project";
    public const string GetProjectSections = "get_project_sections";

    // Stories
    public const string CreateTaskStory = "create_task_story";
    public const string GetStoriesForTask = "get_stories_for_task";

    // Search
    public const string TypeaheadSearch = "typeahead_search";
}
