namespace HQ.Plugins.FileStorage.Models;

public static class FileStorageMethods
{
    public const string WorkspaceCreate = "workspace_create";
    public const string WorkspaceDestroy = "workspace_destroy";
    public const string WorkspaceList = "workspace_list";
    public const string WorkspaceStatus = "workspace_status";
    public const string WorkspaceWriteFile = "workspace_write_file";
    public const string WorkspaceReadFile = "workspace_read_file";
    public const string WorkspaceListFiles = "workspace_list_files";
    public const string WorkspaceDeleteFile = "workspace_delete_file";
    public const string WorkspaceExec = "workspace_exec";
    public const string WorkspaceExecScript = "workspace_exec_script";
    public const string WorkspaceCopyBetween = "workspace_copy_between";
}
