using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DAL.Models
{
    public class NewDataEntry
    {
        public int Id { get; set; }
        public string ParserName { get; set; } = null!;
        public string SourceRecordId { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public bool Processed { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }
}
