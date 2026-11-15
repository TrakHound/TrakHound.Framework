using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TrakHound.Api
{
    public struct TrakHoundApiStreamResponse
    {
        public bool Success { get; set; }

        public int StatusCode { get; set; }

        public ITrakHoundStream<TrakHoundApiResponse> Stream { get; set; }

        public Dictionary<string, string> Parameters { get; set; }
    }
}
