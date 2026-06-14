using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Forms.v1;
using Google.Apis.Forms.v1.Data;
using Google.Apis.Services;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.GoogleForms.Models;

namespace HQ.Plugins.GoogleForms;

/// <summary>Tool surface for Google Forms (create forms, add questions, read responses).</summary>
public class GoogleFormsService
{
    private static readonly string[] Scopes =
    [
        "https://www.googleapis.com/auth/forms.body",
        "https://www.googleapis.com/auth/forms.responses.readonly"
    ];

    private readonly LogDelegate _logger;

    public GoogleFormsService(LogDelegate logger) => _logger = logger;

    private static FormsService BuildService(ServiceConfig config)
    {
        var creds = config.Credentials ?? throw new InvalidOperationException("Google Forms credentials are not configured.");
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = creds.ClientId, ClientSecret = creds.ClientSecret },
            Scopes = Scopes
        });
        var credential = new UserCredential(flow, creds.GoogleUser ?? "user", new TokenResponse { RefreshToken = creds.RefreshToken });
        return new FormsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Ai Orchestrator - Google Forms Plugin"
        });
    }

    [Display(Name = GoogleFormsMethods.CreateForm)]
    [Description("Create a new Google Form with a title and optional description.")]
    [Parameters("""{"type":"object","properties":{"title":{"type":"string"},"description":{"type":"string"}},"required":["title"]}""")]
    public Task<object> CreateForm(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            var service = BuildService(config);
            // The create call only accepts info.title/documentTitle; other fields go through batchUpdate.
            var form = await service.Forms.Create(new Form { Info = new Info { Title = r.Title, DocumentTitle = r.Title } }).ExecuteAsync();

            if (!string.IsNullOrWhiteSpace(r.Description))
            {
                await service.Forms.BatchUpdate(new BatchUpdateFormRequest
                {
                    Requests =
                    [
                        new Request
                        {
                            UpdateFormInfo = new UpdateFormInfoRequest
                            {
                                Info = new Info { Description = r.Description },
                                UpdateMask = "description"
                            }
                        }
                    ]
                }, form.FormId).ExecuteAsync();
            }

            return new
            {
                Success = true,
                form.FormId,
                form.ResponderUri,
                EditUri = $"https://docs.google.com/forms/d/{form.FormId}/edit"
            };
        });

    [Display(Name = GoogleFormsMethods.GetForm)]
    [Description("Get a form's title, description and items.")]
    [Parameters("""{"type":"object","properties":{"formId":{"type":"string"}},"required":["formId"]}""")]
    public Task<object> GetForm(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            var service = BuildService(config);
            var form = await service.Forms.Get(r.FormId).ExecuteAsync();
            return new { Success = true, Form = form };
        });

    [Display(Name = GoogleFormsMethods.AddQuestions)]
    [Description("Append one or more questions to a form. Question types: TEXT, PARAGRAPH, RADIO, CHECKBOX, DROPDOWN (choice types need options).")]
    [Parameters("""{"type":"object","properties":{"formId":{"type":"string"},"questions":{"type":"array","items":{"type":"object","properties":{"title":{"type":"string"},"type":{"type":"string","description":"TEXT | PARAGRAPH | RADIO | CHECKBOX | DROPDOWN"},"options":{"type":"array","items":{"type":"string"}},"required":{"type":"boolean"}},"required":["title","type"]}}},"required":["formId","questions"]}""")]
    public Task<object> AddQuestions(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            if (r.Questions is null || r.Questions.Count == 0) return new { Success = false, Error = "questions are required" };
            var service = BuildService(config);

            var requests = new List<Request>();
            for (var i = 0; i < r.Questions.Count; i++)
                requests.Add(new Request { CreateItem = new CreateItemRequest { Item = BuildItem(r.Questions[i]), Location = new Location { Index = i } } });

            await service.Forms.BatchUpdate(new BatchUpdateFormRequest { Requests = requests }, r.FormId).ExecuteAsync();
            return new { Success = true, r.FormId, Added = r.Questions.Count };
        });

    [Display(Name = GoogleFormsMethods.ListResponses)]
    [Description("List the responses submitted to a form.")]
    [Parameters("""{"type":"object","properties":{"formId":{"type":"string"}},"required":["formId"]}""")]
    public Task<object> ListResponses(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            var service = BuildService(config);
            var responses = await service.Forms.Responses.List(r.FormId).ExecuteAsync();
            return new { Success = true, Responses = responses.Responses ?? [] };
        });

    [Display(Name = GoogleFormsMethods.GetResponse)]
    [Description("Get a single form response by ID.")]
    [Parameters("""{"type":"object","properties":{"formId":{"type":"string"},"responseId":{"type":"string"}},"required":["formId","responseId"]}""")]
    public Task<object> GetResponse(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            var service = BuildService(config);
            var response = await service.Forms.Responses.Get(r.FormId, r.ResponseId).ExecuteAsync();
            return new { Success = true, Response = response };
        });

    /// <summary>Maps a QuestionSpec to a Forms API Item. Public for unit testing.</summary>
    public static Item BuildItem(QuestionSpec q)
    {
        var question = new Question { Required = q.Required ?? false };
        var type = (q.Type ?? "TEXT").Trim().ToUpperInvariant();
        switch (type)
        {
            case "PARAGRAPH":
                question.TextQuestion = new TextQuestion { Paragraph = true };
                break;
            case "RADIO":
            case "CHECKBOX":
            case "DROPDOWN":
                question.ChoiceQuestion = new ChoiceQuestion
                {
                    Type = type == "DROPDOWN" ? "DROP_DOWN" : type,
                    Options = (q.Options ?? []).Select(o => new Option { Value = o }).ToList()
                };
                break;
            default: // TEXT
                question.TextQuestion = new TextQuestion { Paragraph = false };
                break;
        }
        return new Item { Title = q.Title, QuestionItem = new QuestionItem { Question = question } };
    }

    private async Task<object> Guard(Func<Task<object>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Google Forms operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
