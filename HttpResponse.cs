using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace DmHttp
{
    public class HttpResponse
    {
        public List<string> CookieList { get; set; }
        public string HeadersString { get; set; }
        public Dictionary<string, string> ResponseHeaders { get; set; }
        public Stream ResponseStream { get; set; }
        public Encoding ResponseContentEncoding { get; set; }
        public Uri Adress { get; set; }
        public HttpStatusCode StatusCode { get; set; }
    }
}
