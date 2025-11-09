using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartAssistant.Core.Entities
{
    public class Reminder
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public DateTimeOffset ReminderTime { get; set; }    
        public bool Completed { get; set; }
        public DateTime CreatedOn { get; set; }
    }
}
