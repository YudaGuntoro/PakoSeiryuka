using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PakoSeiryuka.Model.KEYENCE
{
    public class DetailsData
    {
        private static readonly Lazy<DetailsData> lazy = new Lazy<DetailsData>(() => new DetailsData());
        public static DetailsData Instance => lazy.Value;
        public string firstScanner { get; set; }
        public string secondScanner { get; set; }
    }
}
