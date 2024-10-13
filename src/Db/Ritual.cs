using System;
using System.Collections.Generic;

namespace RitualWorks.Db
{
    public class Ritual
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string MediaUrl { get; set; } = string.Empty;
        public string CreatorId { get; set; } = string.Empty;
        public string Preview { get; set; } = string.Empty;
        public string FullTextContent { get; set; } = string.Empty; // For custom uploaded content
        public bool IsExternalMediaUrl { get; set; }
        public decimal TokenAmount { get; set; }
        public User? Creator { get; set; }
        public RitualTypeEnum RitualType { get; set; } // Use the enum directly
        public DateTime Created { get; set; } = DateTime.UtcNow; // Default to current UTC time
        public DateTime? Updated { get; set; }
        public bool IsLocked { get; set; } // To indicate if the ritual is locked
        public bool IsProduct { get; set; } // To indicate if the ritual is locked
        public double Rating { get; set; } // Average rating of the ritual
      }
 }
