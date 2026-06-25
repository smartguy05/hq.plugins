using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.PeopleService.v1;
using Google.Apis.PeopleService.v1.Data;
using Google.Apis.Services;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.GoogleContacts.Models;

namespace HQ.Plugins.GoogleContacts;

/// <summary>Tool surface for Google Contacts (People API). Reuses the GoogleForms refresh-token credential pattern.</summary>
public class GoogleContactsService
{
    private const string Fields = "names,emailAddresses,phoneNumbers,organizations";
    private static readonly string[] Scopes = ["https://www.googleapis.com/auth/contacts"];

    private readonly LogDelegate _logger;

    public GoogleContactsService(LogDelegate logger) => _logger = logger;

    private static PeopleServiceService BuildService(ServiceConfig config)
    {
        var creds = config.Credentials ?? throw new InvalidOperationException("Google Contacts credentials are not configured.");
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = creds.ClientId, ClientSecret = creds.ClientSecret },
            Scopes = Scopes
        });
        var credential = new UserCredential(flow, creds.GoogleUser ?? "user", new TokenResponse { RefreshToken = creds.RefreshToken });
        return new PeopleServiceService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Ai Orchestrator - Google Contacts Plugin"
        });
    }

    /// <summary>Build a People API Person from simple fields (only sets sub-objects for provided values).</summary>
    public static Person BuildPerson(string givenName, string familyName, string email, string phone, string organization)
    {
        var person = new Person();
        if (!string.IsNullOrWhiteSpace(givenName) || !string.IsNullOrWhiteSpace(familyName))
            person.Names = [new Name { GivenName = givenName, FamilyName = familyName }];
        if (!string.IsNullOrWhiteSpace(email))
            person.EmailAddresses = [new EmailAddress { Value = email }];
        if (!string.IsNullOrWhiteSpace(phone))
            person.PhoneNumbers = [new PhoneNumber { Value = phone }];
        if (!string.IsNullOrWhiteSpace(organization))
            person.Organizations = [new Organization { Name = organization }];
        return person;
    }

    [Display(Name = GoogleContactsMethods.ListContacts)]
    [Description("List the contacts in your address book (names, emails, phone numbers, organizations).")]
    [Parameters(typeof(ListContactsArgs))]
    public Task<object> ListContacts(ServiceConfig config, ListContactsArgs r) =>
        Guard(async () =>
        {
            var service = BuildService(config);
            var req = service.People.Connections.List("people/me");
            req.PersonFields = Fields;
            req.PageSize = Math.Clamp(r.PageSize ?? 50, 1, 1000);
            var resp = await req.ExecuteAsync();
            return new { Success = true, Contacts = resp.Connections ?? [], Total = resp.TotalPeople };
        });

    [Display(Name = GoogleContactsMethods.SearchContacts)]
    [Description("Search your contacts by name, email or phone.")]
    [Parameters(typeof(SearchContactsArgs))]
    public Task<object> SearchContacts(ServiceConfig config, SearchContactsArgs r) =>
        Guard(async () =>
        {
            var service = BuildService(config);
            var req = service.People.SearchContacts();
            req.Query = r.Query;
            req.ReadMask = Fields;
            req.PageSize = Math.Clamp(r.PageSize ?? 25, 1, 30);
            var resp = await req.ExecuteAsync();
            return new { Success = true, Results = resp.Results?.Select(x => x.Person) ?? [] };
        });

    [Display(Name = GoogleContactsMethods.GetContact)]
    [Description("Get a single contact by resource name (e.g. 'people/c12345').")]
    [Parameters(typeof(GetContactArgs))]
    public Task<object> GetContact(ServiceConfig config, GetContactArgs r) =>
        Guard(async () =>
        {
            var service = BuildService(config);
            var req = service.People.Get(r.ResourceName);
            req.PersonFields = Fields;
            var person = await req.ExecuteAsync();
            return new { Success = true, Contact = person };
        });

    [Display(Name = GoogleContactsMethods.CreateContact)]
    [Description("Create a new contact.")]
    [Parameters(typeof(CreateContactArgs))]
    public Task<object> CreateContact(ServiceConfig config, CreateContactArgs r) =>
        Guard(async () =>
        {
            var service = BuildService(config);
            var body = BuildPerson(r.GivenName, r.FamilyName, r.Email, r.Phone, r.Organization);
            var person = await service.People.CreateContact(body).ExecuteAsync();
            return new { Success = true, Contact = person };
        });

    [Display(Name = GoogleContactsMethods.UpdateContact)]
    [Description("Update fields on an existing contact. Only the provided fields are changed.")]
    [Parameters(typeof(UpdateContactArgs))]
    public Task<object> UpdateContact(ServiceConfig config, UpdateContactArgs r) =>
        Guard(async () =>
        {
            var service = BuildService(config);
            var getReq = service.People.Get(r.ResourceName);
            getReq.PersonFields = Fields;
            var existing = await getReq.ExecuteAsync();

            var body = new Person { ETag = existing.ETag };
            var updated = new List<string>();
            if (!string.IsNullOrWhiteSpace(r.GivenName) || !string.IsNullOrWhiteSpace(r.FamilyName))
            {
                var cur = existing.Names?.FirstOrDefault();
                body.Names = [new Name { GivenName = r.GivenName ?? cur?.GivenName, FamilyName = r.FamilyName ?? cur?.FamilyName }];
                updated.Add("names");
            }
            if (!string.IsNullOrWhiteSpace(r.Email)) { body.EmailAddresses = [new EmailAddress { Value = r.Email }]; updated.Add("emailAddresses"); }
            if (!string.IsNullOrWhiteSpace(r.Phone)) { body.PhoneNumbers = [new PhoneNumber { Value = r.Phone }]; updated.Add("phoneNumbers"); }
            if (!string.IsNullOrWhiteSpace(r.Organization)) { body.Organizations = [new Organization { Name = r.Organization }]; updated.Add("organizations"); }

            if (updated.Count == 0) return new { Success = false, Error = "No updatable fields provided." };

            var req = service.People.UpdateContact(body, r.ResourceName);
            req.UpdatePersonFields = string.Join(",", updated);
            var person = await req.ExecuteAsync();
            return new { Success = true, Contact = person };
        });

    private async Task<object> Guard(Func<Task<object>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Google Contacts operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
