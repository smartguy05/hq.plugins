using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.Gusto.Models;

namespace HQ.Plugins.Gusto;

/// <summary>
/// Tool surface for Gusto payroll + HR. v1 is read-only — payroll data is sensitive and Gusto's
/// write surface (e.g. time-off requests) runs through embedded flows, so writes are deferred.
/// </summary>
public class GustoService
{
    private readonly LogDelegate _logger;

    public GustoService(LogDelegate logger) => _logger = logger;

    /// <summary>Returns the supplied company id, or resolves the first accessible company via /v1/me.</summary>
    private static async Task<string> CompanyId(GustoClient client, string companyId)
    {
        if (!string.IsNullOrWhiteSpace(companyId)) return companyId;
        var me = await client.GetAsync("/v1/me");
        if (me.TryGetProperty("roles", out var roles)
            && roles.TryGetProperty("payroll_admin", out var admin)
            && admin.TryGetProperty("companies", out var companies)
            && companies.ValueKind == JsonValueKind.Array
            && companies.GetArrayLength() > 0)
        {
            return companies[0].GetProperty("uuid").GetString();
        }
        throw new InvalidOperationException("Could not resolve a company id — pass companyId explicitly.");
    }

    [Display(Name = GustoMethods.GetCompany)]
    [Description("Get company details. Omit companyId to use the first company on the account.")]
    [Parameters(typeof(GetCompanyArgs))]
    public Task<object> GetCompany(ServiceConfig config, GetCompanyArgs request) =>
        Guard(async () =>
        {
            using var client = new GustoClient(config);
            var doc = await client.GetAsync($"/v1/companies/{await CompanyId(client, request.CompanyId)}");
            return new { Success = true, Company = doc };
        });

    [Display(Name = GustoMethods.ListEmployees)]
    [Description("List employees for the company.")]
    [Parameters(typeof(ListEmployeesArgs))]
    public Task<object> ListEmployees(ServiceConfig config, ListEmployeesArgs request) =>
        Guard(async () =>
        {
            using var client = new GustoClient(config);
            var doc = await client.GetAsync($"/v1/companies/{await CompanyId(client, request.CompanyId)}/employees");
            return new { Success = true, Employees = doc };
        });

    [Display(Name = GustoMethods.GetEmployee)]
    [Description("Get a single employee by ID.")]
    [Parameters(typeof(GetEmployeeArgs))]
    public Task<object> GetEmployee(ServiceConfig config, GetEmployeeArgs request) =>
        Guard(async () =>
        {
            using var client = new GustoClient(config);
            var doc = await client.GetAsync($"/v1/employees/{request.EmployeeId}");
            return new { Success = true, Employee = doc };
        });

    [Display(Name = GustoMethods.ListPayrolls)]
    [Description("List payrolls for the company.")]
    [Parameters(typeof(ListPayrollsArgs))]
    public Task<object> ListPayrolls(ServiceConfig config, ListPayrollsArgs request) =>
        Guard(async () =>
        {
            using var client = new GustoClient(config);
            var doc = await client.GetAsync($"/v1/companies/{await CompanyId(client, request.CompanyId)}/payrolls");
            return new { Success = true, Payrolls = doc };
        });

    [Display(Name = GustoMethods.GetPayroll)]
    [Description("Get a single payroll by ID for the company.")]
    [Parameters(typeof(GetPayrollArgs))]
    public Task<object> GetPayroll(ServiceConfig config, GetPayrollArgs request) =>
        Guard(async () =>
        {
            using var client = new GustoClient(config);
            var doc = await client.GetAsync($"/v1/companies/{await CompanyId(client, request.CompanyId)}/payrolls/{request.PayrollId}");
            return new { Success = true, Payroll = doc };
        });

    [Display(Name = GustoMethods.ListTimeOffRequests)]
    [Description("List time-off requests for the company.")]
    [Parameters(typeof(ListTimeOffRequestsArgs))]
    public Task<object> ListTimeOffRequests(ServiceConfig config, ListTimeOffRequestsArgs request) =>
        Guard(async () =>
        {
            using var client = new GustoClient(config);
            var doc = await client.GetAsync($"/v1/companies/{await CompanyId(client, request.CompanyId)}/time_off_requests");
            return new { Success = true, TimeOffRequests = doc };
        });

    [Display(Name = GustoMethods.ListLocations)]
    [Description("List the company's work locations.")]
    [Parameters(typeof(ListLocationsArgs))]
    public Task<object> ListLocations(ServiceConfig config, ListLocationsArgs request) =>
        Guard(async () =>
        {
            using var client = new GustoClient(config);
            var doc = await client.GetAsync($"/v1/companies/{await CompanyId(client, request.CompanyId)}/locations");
            return new { Success = true, Locations = doc };
        });

    [Display(Name = GustoMethods.ListPaySchedules)]
    [Description("List the company's pay schedules.")]
    [Parameters(typeof(ListPaySchedulesArgs))]
    public Task<object> ListPaySchedules(ServiceConfig config, ListPaySchedulesArgs request) =>
        Guard(async () =>
        {
            using var client = new GustoClient(config);
            var doc = await client.GetAsync($"/v1/companies/{await CompanyId(client, request.CompanyId)}/pay_schedules");
            return new { Success = true, PaySchedules = doc };
        });

    private async Task<object> Guard(Func<Task<object>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Gusto operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
