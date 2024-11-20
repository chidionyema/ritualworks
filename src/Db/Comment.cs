using System;
namespace haworks.Db
{
    public class Comment
    {
        public Guid Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public DateTime DateCreated { get; set; }
        public Guid PostId { get; set; }
        public Post? Post { get; set; }
    }
}

