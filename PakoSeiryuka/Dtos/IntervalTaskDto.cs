using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PakoSeiryuka.Dtos
{
    public class IntervalTaskDto
    {
        public int Id { get; set; }
        public int Mqtt_Interval { get; set; }
        public int Plc_Interval { get; set; }
    }
}
