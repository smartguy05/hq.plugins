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

    /// <summary>Returns the request's company id, or resolves the first accessible company via /v1/me.</summary>
    private static async Task<string> CompanyId(GustoClient client, ServiceRequest r)
    {
        if (!string.IsNullOrWhiteSpace(r.CompanyId)) return r.CompanyId;
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
    [Parameters("""{"type":"object","properties":{"companyId":{"type":"string"}},"required":[]}""")]
    public Task<object> GetCompany(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new GustoClient(config);
            var doc = await client.GetAsync($"/v1/companies/{await CompanyId(client, r)}");
            return new { Success = true, Company = doc };
        });

    [Display(Name = GustoMethods.ListEmployees)]
    [Description("List employees for the company.")]
    [Parameters("""{"type":"object","properties":{"companyId":{"type":"string"}},"required":[]}""")]
    public Task<object> ListEmployees(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new GustoClient(config);
            var doc = await client.GetAsync($"/v1/companies/{await CompanyId(client, r)}/employees");
            return new { Success = true, Employees = doc };
        });

    [Display(Name = GustoMethods.GetEmployee)]
    [Description("Get a single employee by ID.")]
    [Parameters("""{"type":"object","properties":{"employeeId":{"type":"string"}},"required":["employeeId"]}""")]
    public Task<object> GetEmployee(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new GustoClient(config);
            var doc = await client.GetAsync($"/v1/employees/{r.EmployeeId}");
            return new { Success = true, Employee = doc };
        });

    [Display(Name = GustoMethods.ListPayrolls)]
    [Description("List payrolls for the company.")]
    [Parameters("""{"type":"object","properties":{"companyId":{"type":"string"}},"required":[]}""")]
    public Task<object> ListPayrolls(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new GustoClient(config);
            var doc = await client.GetAsync($"/v1/companies/{await CompanyId(client, r)}/payrolls");
            return new { Success = true, Payrolls = doc };
        });

    [Display(Name = GustoMethods.GetPayroll)]
    [Description("Get a single payroll by ID for the company.")]
    [Parameters("""{"type":"object","properties":{"companyId":{"type":"string"},"payrollId":{"type":"string"}},"required":["payrollId"]}""")]
    public Task<object> GetPayroll(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new GustoClient(config);
            var doc = await client.GetAsync($"/v1/companies/{await CompanyId(client, r)}/payrolls/{r.PayrollId}");
            return new { Success = true, Payroll = doc };
        });

    [Display(Name = GustoMethods.ListTimeOffRequests)]
    [Description("List time-off requests for the company.")]
    [Parameters("""{"type":"object","properties":{"companyId":{"type":"string"}},"required":[]}""")]
    public Task<object> ListTimeOffRequests(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new GustoClient(config);
            var doc = await client.GetAsync($"/v1/companies/{await CompanyId(client, r)}/time_off_requests");
            return new { Success = true, TimeOffRequests = doc };
        });

    [Display(Name = GustoMethods.ListLocations)]
    [Description("List the company's work locations.")]
    [Parameters("""{"type":"object","properties":{"companyId":{"type":"string"}},"required":[]}""")]
    public Task<object> ListLocations(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new GustoClient(config);
            var doc = await client.GetAsync($"/v1/companies/{await CompanyId(client, r)}/locations");
            return new { Success = true, Locations = doc };
        });

    [Display(Name = GustoMethods.ListPaySchedules)]
    [Description("List the company's pay schedules.")]
    [Parameters("""{"type":"object","properties":{"companyId":{"type":"string"}},"required":[]}""")]
    public Task<object> ListPaySchedules(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new GustoClient(config);
            var doc = await client.GetAsync($"/v1/companies/{await CompanyId(client, r)}/pay_schedules");
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
