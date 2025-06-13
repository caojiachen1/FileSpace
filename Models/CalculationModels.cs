using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileSpace.Models
{
    public class SizeCalculationRequest
    {
        public string FolderPath { get; set; } = string.Empty;
        public object? Context { get; set; }
        public int Priority { get; set; }
        public DateTime RequestTime { get; set; }
    }
}
