using System;
using System.Collections.Generic;
using System.Text;

namespace DmHttp
{
    public class DmProxy
    {
        public enum ProxyType
        {
            Socks4,
            Socks5,
            Http
        }

        public ProxyType Type { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }

        public DmProxy(string input, ProxyType type)
        {
            var parts = input.Split(':');
            this.Host = parts[0];
            Port = int.Parse(parts[1]);
            Type = type;
        }
    }
}
