using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DAL.Models
{
    public class ActiveTopic
    {
        public int Id { get; set; }
        public string RequestId { get; set; }
        public string MessageThreadId { get; set; }
        public string ParserName { get; set; }
        public int AssistantType { get; set; }
        public string? Article { get; set; }
    }
}
