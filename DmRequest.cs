using Starksoft.Aspen.Proxy;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DmHttp
{
    public class DmRequest
    {
        private Dictionary<string, string> headers = new Dictionary<string, string>();
        private string userAgent;
        private int timeout;
        private DmProxy proxy;
        private readonly HashSet<string> encodings = new HashSet<string>();

        public Dictionary<string, string> Headers { get => headers; set => headers = value; }
        public string UserAgent
        {
            get => userAgent;
            set
            {
                userAgent = value;
                SetHeader("User-Agent", userAgent);
            }
        }
        public int Timeout
        {
            get => (int)TimeSpan.FromTicks(timeout).TotalSeconds;
            set => timeout = (int)TimeSpan.FromSeconds(value).TotalMilliseconds;
        }
        public DmProxy Proxy { get => proxy; set => proxy = value; }

        public DmRequest()
        {
            foreach (var encoding in Encoding.GetEncodings())
            {
                encodings.Add(encoding.Name);
            }

            Timeout = 20;
            proxy = null;
            userAgent = "Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.1; WOW64; Trident/6.0)";
            SetHeader("Accept-Encoding", "gzip, deflate");
            SetHeader("User-Agent", userAgent);
        }

        public void SetHeader(string name, string value)
        {
            if (headers.ContainsKey(name))
            {
                headers[name] = value;
            }
            else
            {
                headers.Add(name, value);
            }
        }

        public HttpResponse Request(string url, string method = "GET", string payload = null)
        {
            if ((method == "POST" || method == "PUT") && payload == null)
            {
                throw new ArgumentException("POST or PUT request can not be executed without a payload.");
            }

            var uri = new Uri(url);

            bool isHttps;
            if (uri.Scheme == "http")
            {
                isHttps = false;
            }
            else if (uri.Scheme == "https")
            {
                isHttps = true;
            }
            else throw new UriFormatException("Uri scheme is not valid.");

            var response = new HttpResponse
            {
                Adress = uri
            };

            TcpClient client;
            Stream stream = null;

            if (proxy != null)
            {
                IProxyClient proxyClient;
                switch (proxy.Type)
                {
                    case DmProxy.ProxyType.Socks5:
                        proxyClient = new Socks5ProxyClient(proxy.Host, proxy.Port);
                        client = proxyClient.CreateConnection(uri.Host, uri.Port);
                        break;
                    case DmProxy.ProxyType.Socks4:
                        proxyClient = new Socks4ProxyClient(proxy.Host, proxy.Port);
                        client = proxyClient.CreateConnection(uri.Host, uri.Port);
                        break;
                    case DmProxy.ProxyType.Http:
                        proxyClient = new HttpProxyClient(proxy.Host, proxy.Port);
                        client = proxyClient.CreateConnection(uri.Host, uri.Port);
                        break;
                    default:
                        throw new ProxyException("Unknown proxy type.");
                }
            }
            else
            {
                client = new TcpClient(uri.Host, uri.Port);
            }

            try
            {
                client.SendTimeout = timeout / 2;
                client.ReceiveTimeout = timeout;

                if (isHttps)
                {
                    stream = new SslStream(client.GetStream());
                    (stream as SslStream).AuthenticateAsClient(uri.Host);
                }
                else
                {
                    stream = client.GetStream();
                }

                var messageBuilder = new StringBuilder();

                messageBuilder.Append(method);
                messageBuilder.Append(" ");
                messageBuilder.Append(uri.AbsoluteUri);
                messageBuilder.AppendLine(" HTTP/1.1");

                messageBuilder.Append("Host: ");
                messageBuilder.AppendLine(uri.Host);

                foreach (var header in headers)
                {
                    messageBuilder.Append(header.Key);
                    messageBuilder.Append(": ");
                    messageBuilder.AppendLine(header.Value);
                }

                if (payload != null)
                {
                    messageBuilder.Append("Content-Length: ");
                    messageBuilder.AppendLine(payload.Length.ToString());

                    messageBuilder.AppendLine();

                    messageBuilder.AppendLine(payload);
                }

                messageBuilder.AppendLine();

                var a = messageBuilder.ToString();

                var httpHeader = Encoding.ASCII.GetBytes(messageBuilder.ToString());

                messageBuilder = null;

                stream.Write(httpHeader, 0, httpHeader.Length);

                var pattern = new byte[] { 13, 10, 13, 10 };

                var headerBytes = ReadUntilPattern(stream, pattern);

                response.HeadersString = Encoding.ASCII.GetString(headerBytes, 0, headerBytes.Length);
                ParseHeaders(ref response);

                using var memoryStream = new MemoryStream();
                if (response.ResponseHeaders.ContainsKey("content-length"))
                {
                    var bodyLength = int.Parse(response.ResponseHeaders["content-length"]);

                    ReadStream(stream, memoryStream, bodyLength);
                }
                else
                {
                    var splitSequence = new byte[] { 13, 10 };

                    var chunkHeaderString = Encoding.ASCII.GetString(ReadUntilPattern(stream, splitSequence));
                    var chunkHeaderParts = chunkHeaderString.Split(';');
                    var chunkLength = int.Parse(chunkHeaderParts[0].Trim(), System.Globalization.NumberStyles.HexNumber);

                    while (chunkLength > 0)
                    {
                        ReadStream(stream, memoryStream, chunkLength);

                        stream.ReadByte(); //Read CRLF
                        stream.ReadByte();

                        chunkHeaderString = Encoding.ASCII.GetString(ReadUntilPattern(stream, splitSequence));
                        chunkHeaderParts = chunkHeaderString.Split(';');
                        chunkLength = int.Parse(chunkHeaderParts[0].Trim(), System.Globalization.NumberStyles.HexNumber);
                    }
                }

                stream.Close();

                memoryStream.Position = 0;

                response.ResponseStream = new MemoryStream();

                if (response.ResponseHeaders.ContainsKey("content-encoding") && response.ResponseHeaders["content-encoding"] == "gzip")
                {
                    using GZipStream decompressionStream = new GZipStream(memoryStream, CompressionMode.Decompress);
                    decompressionStream.CopyTo(response.ResponseStream);
                    response.ResponseStream.Position = 0;
                }
                else if (response.ResponseHeaders.ContainsKey("content-encoding") && response.ResponseHeaders["content-encoding"] == "deflate")
                {
                    using DeflateStream decompressionStream = new DeflateStream(memoryStream, CompressionMode.Decompress);
                    decompressionStream.CopyTo(response.ResponseStream);
                    response.ResponseStream.Position = 0;
                }
                else
                {
                    memoryStream.CopyTo(response.ResponseStream);
                    response.ResponseStream.Position = 0;
                }

            }
            finally
            {
                if (stream != null)
                {
                    stream.Dispose();
                }

                client.Dispose();
            }


            return response;
        }

        private void ReadStream(Stream stream, MemoryStream ms, int length)
        {
            var buffer = new byte[2048];
            int totalRead = 0;

            int read;
            do
            {
                var toRead = length - totalRead;
                if (toRead > 0)
                {
                    if (toRead > buffer.Length)
                    {
                        toRead = buffer.Length;
                    }
                    read = stream.Read(buffer, 0, toRead);
                    totalRead += read;
                    ms.Write(buffer, 0, read);
                }
                else
                {
                    break;
                }
            }
            while (read > 0);
        }

        private byte[] ReadUntilPattern(Stream stream, byte[] pattern)
        {
            List<byte> buffer = new List<byte>();

            var patternLast = pattern.Last();

            int nextByte;
            do
            {
                nextByte = stream.ReadByte();
                if (nextByte > -1)
                {
                    buffer.Add((byte)nextByte);
                    if (nextByte == patternLast
                        && buffer.Count >= pattern.Length
                        && Enumerable.SequenceEqual(buffer.GetRange(buffer.Count - pattern.Length, pattern.Length), pattern))
                    {
                        break;
                    }
                }

            }
            while (nextByte > -1);

            return buffer.ToArray();
        }

        private void ParseHeaders(ref HttpResponse httpResponse)
        {
            httpResponse.ResponseHeaders = new Dictionary<string, string>();

            httpResponse.CookieList = new List<string>();

            var parts = httpResponse.HeadersString.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            var code = parts[0].Split(' ')[1];
            httpResponse.StatusCode = (HttpStatusCode)int.Parse(code);

            for (var i = 1; i < parts.Length; i++)
            {
                var sepIndex = parts[i].IndexOf(':');
                var name = parts[i].Substring(0, sepIndex).Trim().ToLower();
                var value = parts[i].Substring(sepIndex + 1).Trim().ToLower();

                if (name == "content-type")
                {
                    var contentTypeParts = value.Split(';');
                    foreach (var part in contentTypeParts)
                    {
                        var trimmedPart = part.Trim();
                        if (trimmedPart.StartsWith("charset="))
                        {
                            var encoding = trimmedPart.Substring(8);

                            if (encodings.Contains(encoding))
                            {
                                httpResponse.ResponseContentEncoding = Encoding.GetEncoding(encoding);
                            }
                            else
                            {
                                httpResponse.ResponseContentEncoding = Encoding.UTF8;
                            }
                            break;
                        }
                    }
                }
                if (name == "set-cookie")
                {
                    httpResponse.CookieList.Add(value);
                }
                if (httpResponse.ResponseHeaders.ContainsKey(name))
                {
                    if (name == "set-cookie")
                    {
                        httpResponse.ResponseHeaders[name] = httpResponse.ResponseHeaders[name] + ", " + value;
                    }
                    else
                    {
                        httpResponse.ResponseHeaders[name] = value;
                    }
                }
                else
                {
                    httpResponse.ResponseHeaders.Add(name, value);
                }
            }
        }
    }
}
