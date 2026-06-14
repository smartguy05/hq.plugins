using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace HQ.Plugins.Tasks.Entities;

[Table("comments", Schema = "plugin_localtasks")]
public class Comment
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string Author { get; set; }
    public string Text { get; set; }
    public DateTime CreatedAt { get; set; }
}
