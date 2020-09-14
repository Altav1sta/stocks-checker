using System;

namespace WebApp.Models
{
    public class Chat
    {
        public long Id { get; set; }

        public string Text { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
