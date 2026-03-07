using System.ClientModel;
using ChromaDB.Client;
using ChromaDB.Client.Models;
using HQ.Models.Enums;
using HQ.Models.Interfaces;
using HQ.Plugins.Email.Models;
using OpenAI;

namespace HQ.Plugins.Email.Data;

public class EmailVectorService : IDisposable
{
    private readonly ChromaClient _chromaClient;
    private readonly ChromaConfigurationOptions _chromaConfigOptions;
    private readonly HttpClient _httpClient;
    private readonly string _collectionName;
    private readonly OpenAIClient _openAiClient;
    private readonly string _embeddingModel;
    private readonly LogDelegate _logger;
    private readonly int _maxBodyChars;

    public EmailVectorService(ServiceConfig config, LogDelegate logger, HttpClient httpClient = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(config.ChromaUrl))
            throw new ArgumentException("ChromaUrl is required for vector search.");
        if (string.IsNullOrWhiteSpace(config.OpenAiApiKey))
            throw new ArgumentException("OpenAiApiKey is required for vector search.");

        _chromaConfigOptions = new ChromaConfigurationOptions(uri: config.ChromaUrl);
        _httpClient = httpClient ?? new HttpClient();
        _chromaClient = new ChromaClient(_chromaConfigOptions, _httpClient);

        _collectionName = !string.IsNullOrEmpty(config.AgentId)
            ? $"agent-{config.AgentId}-emails"
            : config.ChromaCollectionName ?? "email-vectors";

        var openAiOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(config.OpenAiUrl))
            openAiOptions.Endpoint = new Uri(config.OpenAiUrl);
        _openAiClient = new OpenAIClient(new ApiKeyCredential(config.OpenAiApiKey), openAiOptions);
        _embeddingModel = config.EmbeddingModel ?? "text-embedding-3-small";
        _maxBodyChars = config.MaxEmailBodyChars > 0 ? config.MaxEmailBodyChars : 50000;
    }

    public async Task<string> IndexEmailAsync(LocalEmail email)
    {
        var text = BuildEmbeddingText(email);
        if (string.IsNullOrWhiteSpace(text)) return null;

        var vectorId = $"{email.MessageId ?? email.Id.ToString()}";
        var chunks = ChunkText(text);

        var collectionClient = await GetOrCreateCollectionAsync();
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunkId = chunks.Count == 1 ? vectorId : $"{vectorId}_chunk_{i}";
            var embedding = await GenerateEmbeddingAsync(chunks[i]);
            var metadata = new Dictionary<string, object>
            {
                ["account_name"] = email.AccountName ?? "",
                ["folder"] = email.Folder ?? "",
                ["message_id"] = email.MessageId ?? "",
                ["from_address"] = email.FromAddress ?? "",
                ["date_sent"] = email.DateSent.ToString("O"),
                ["subject"] = email.Subject ?? ""
            };

            await collectionClient.Upsert(
                ids: [chunkId],
                embeddings: [embedding],
                metadatas: [metadata],
                documents: [chunks[i]]
            );
        }

        return vectorId;
    }

    public async Task<List<(string MessageId, string Subject, string Snippet, float Distance)>> SearchAsync(string query, int maxResults = 10)
    {
        var embedding = await GenerateEmbeddingAsync(query);
        var collectionClient = await GetOrCreateCollectionAsync();

        var results = await collectionClient.Query(
            queryEmbeddings: [embedding],
            nResults: maxResults * 2, // over-fetch for dedup
            include: ChromaQueryInclude.Metadatas | ChromaQueryInclude.Documents | ChromaQueryInclude.Distances
        );

        var seen = new HashSet<string>();
        var output = new List<(string, string, string, float)>();

        foreach (var resultSet in results)
        {
            if (resultSet == null) continue;
            foreach (var item in resultSet)
            {
                if (item?.Metadata == null) continue;
                var messageId = item.Metadata.TryGetValue("message_id", out var mid) ? mid?.ToString() : null;
                if (string.IsNullOrEmpty(messageId) || !seen.Add(messageId)) continue;

                var subject = item.Metadata.TryGetValue("subject", out var sub) ? sub?.ToString() : "";
                var snippet = item.Document?.Length > 200 ? item.Document[..200] + "..." : item.Document ?? "";

                output.Add((messageId, subject, snippet, item.Distance));
                if (output.Count >= maxResults) break;
            }
            if (output.Count >= maxResults) break;
        }

        return output;
    }

    public async Task DeleteByVectorIdAsync(string vectorId)
    {
        if (string.IsNullOrWhiteSpace(vectorId)) return;
        try
        {
            var collectionClient = await GetOrCreateCollectionAsync();
            // Delete main + any chunks
            var ids = new List<string> { vectorId };
            for (int i = 0; i < 100; i++)
                ids.Add($"{vectorId}_chunk_{i}");
            await collectionClient.Delete(ids);
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Warning, $"Failed to delete vector {vectorId}: {ex.Message}");
        }
    }

    public async Task DeleteByVectorIdsAsync(IEnumerable<string> vectorIds)
    {
        foreach (var id in vectorIds)
            await DeleteByVectorIdAsync(id);
    }

    private string BuildEmbeddingText(LocalEmail email)
    {
        var body = email.BodyText ?? "";
        if (body.Length > _maxBodyChars)
            body = body[.._maxBodyChars];

        return $"Subject: {email.Subject}\nFrom: {email.FromName} <{email.FromAddress}>\nDate: {email.DateSent:yyyy-MM-dd}\n\n{body}";
    }

    private List<string> ChunkText(string text)
    {
        const int maxChunkChars = 24000;
        const int overlapChars = 2000;

        if (text.Length <= maxChunkChars)
            return [text];

        var chunks = new List<string>();
        var pos = 0;
        while (pos < text.Length)
        {
            var end = Math.Min(pos + maxChunkChars, text.Length);
            // Try to break at paragraph boundary
            if (end < text.Length)
            {
                var paraBreak = text.LastIndexOf("\n\n", end, Math.Min(end - pos, 2000));
                if (paraBreak > pos + maxChunkChars / 2)
                    end = paraBreak;
            }
            chunks.Add(text[pos..end]);
            pos = end - overlapChars;
            if (pos < 0) pos = 0;
            if (end >= text.Length) break;
        }
        return chunks;
    }

    private async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text)
    {
        var embeddingClient = _openAiClient.GetEmbeddingClient(_embeddingModel);
        var result = await embeddingClient.GenerateEmbeddingAsync(text);
        return result.Value.ToFloats();
    }

    private async Task<ChromaCollectionClient> GetOrCreateCollectionAsync()
    {
        ChromaCollection collection;
        try
        {
            collection = await _chromaClient.GetCollection(_collectionName);
        }
        catch
        {
            collection = await _chromaClient.GetOrCreateCollection(
                _collectionName,
                new Dictionary<string, object> { ["description"] = "Email vector index" });
        }
        return new ChromaCollectionClient(collection, _chromaConfigOptions, _httpClient);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
