using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace DmHttp
{
    public static class DmExtensions
    {
        public static string GetContent(this HttpResponse response, Encoding encoding = null)
        {
            if (response.ResponseContentEncoding != null && encoding == null)
            {
                encoding = response.ResponseContentEncoding;
            }
            else if (encoding == null)
            {
                encoding = Encoding.UTF8;
            }
            using var stream = response.ResponseStream;
            using var reader = new StreamReader(stream, encoding);
            return reader.ReadToEnd();
        }

        public static string HtmlDecode(this string input)
        {
            return WebUtility.HtmlDecode(input);
        }

        public static string UrlEncode(this string input)
        {
            return WebUtility.UrlEncode(input);
        }

        public static string UrlDecode(this string input)
        {
            return WebUtility.UrlDecode(input);
        }
    }
}
