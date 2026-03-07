namespace HQ.Plugins.Email.Models;

public record LocalEmail
{
    public long Id { get; set; }
    public string AccountName { get; set; }
    public string Folder { get; set; }
    public uint Uid { get; set; }
    public string MessageId { get; set; }
    public string Subject { get; set; }
    public string FromAddress { get; set; }
    public string FromName { get; set; }
    public string ToAddress { get; set; }
    public string CcAddress { get; set; }
    public string BccAddress { get; set; }
    public string ReplyTo { get; set; }
    public DateTimeOffset DateSent { get; set; }
    public string BodyText { get; set; }
    public string BodyHtml { get; set; }
    public bool IsRead { get; set; }
    public bool IsFlagged { get; set; }
    public bool HasAttachments { get; set; }
    public string AttachmentNames { get; set; }
    public string VectorId { get; set; }
    public DateTime SyncedAt { get; set; }
}
