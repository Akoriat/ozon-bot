using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace DAL.Models
{
    [Index(nameof(SourceRecordId), IsUnique = true)]
    public class NewDataEntry
    {
        [Key]
        public int Id { get; set; }
        public string ParserName { get; set; } = null!;
        public string SourceRecordId { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public bool Processed { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }
}
