using System;
using System.Collections.Generic;

namespace RitualWorks.Db
{
    public class Post
    {
        public Post()
        {
            Comments = [];
        }

        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public DateTime DateCreated { get; set; }
        public ICollection<Comment> Comments { get; set; }
    }
}

