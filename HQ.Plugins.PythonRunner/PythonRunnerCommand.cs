using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.PythonRunner.Models;

namespace HQ.Plugins.PythonRunner;

public class PythonRunnerCommand: CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "HQ.Plugins.PythonRunner";
    public override string Description => "A plugin to run a python script";
    protected override INotificationService NotificationService { get; set; }

    public override List<ToolCall> GetToolDefinitions()
    {
        return this.GetServiceToolCalls();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        return await this.ProcessRequest(serviceRequest, config, NotificationService);
    }

    [Display(Name = "run_python_script")]
    [Description("Executes a Python script and returns the output. The script is written to a temporary file, executed, and the file is cleaned up afterward.")]
    [Parameters("""{"type":"object","properties":{"pythonScript":{"type":"string","description":"The Python script code to execute"}},"required":["pythonScript"]}""")]
    public async Task<object> RunPythonScriptTool(ServiceConfig config, ServiceRequest serviceRequest)
    {
        var tempFile = Path.GetTempFileName() + ".py";

        try
        {
            await File.WriteAllTextAsync(tempFile, serviceRequest.PythonScript);
            return await RunPythonScript(tempFile);
        }
        catch (Exception e)
        {
            await Log(LogLevel.Error, e.Message, e);
            return new { Success = false };
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private async Task<object> RunPythonScript(string tempFile)
    {
        var start = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"\"{tempFile}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        string output = null;
        try
        {
            using var process = Process.Start(start);

            if (process is null)
            {
                throw new Exception("Could not start python script");
            }

            output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            await Log(LogLevel.Info, $"Output: {output}");

            if (!string.IsNullOrWhiteSpace(error))
            {
                await Log(LogLevel.Warning, $"Error: {error}");

                return new { Success = false, Error = error };
            }

        }
        catch (Exception ex)
        {
            await Log(LogLevel.Error, "An error occurred while running the Python script:", ex);
            return new { Success = false, Error = ex.Message };
        }

        return new { Success = true, Result = output };
    }
}
