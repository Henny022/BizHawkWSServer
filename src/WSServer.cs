// implemented following https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_server
// also wikipedia

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace WSServer;

class WSServer
{
    private TcpListener _server;

    public WSServer(string ip, int port)
    {
        _server = new TcpListener(IPAddress.Parse(ip), port);
        _server.Start();
    }

    public WSConnection Accept()
    {
        while (true)
        {
            var client = _server.AcceptTcpClient();
            var stream = client.GetStream();

            while (!stream.DataAvailable) ;
            while (client.Available < 3) ; // match against "get"

            byte[] bytes = new byte[client.Available];
            stream.Read(bytes, 0, bytes.Length);
            string s = Encoding.UTF8.GetString(bytes);

            if (Regex.IsMatch(s, "^GET", RegexOptions.IgnoreCase))
            {
                Console.WriteLine("=====Handshaking from client=====\n{0}", s);

                // 1. Obtain the value of the "Sec-WebSocket-Key" request header without any leading or trailing whitespace
                // 2. Concatenate it with "258EAFA5-E914-47DA-95CA-C5AB0DC85B11" (a special GUID specified by RFC 6455)
                // 3. Compute SHA-1 and Base64 hash of the new value
                // 4. Write the hash back as the value of "Sec-WebSocket-Accept" response header in an HTTP response
                string swk = Regex.Match(s, "Sec-WebSocket-Key: (.*)").Groups[1].Value.Trim();
                string swka = swk + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
                byte[] swkaSha1 = System.Security.Cryptography.SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(swka));
                string swkaSha1Base64 = Convert.ToBase64String(swkaSha1);

                // HTTP/1.1 defines the sequence CR LF as the end-of-line marker
                byte[] response = Encoding.UTF8.GetBytes(
                    "HTTP/1.1 101 Switching Protocols\r\n" +
                    "Connection: Upgrade\r\n" +
                    "Upgrade: websocket\r\n" +
                    "Sec-WebSocket-Accept: " + swkaSha1Base64 + "\r\n\r\n");

                stream.Write(response, 0, response.Length);
                return new WSConnection(client, stream);
            }

            client.Close();
        }
    }

    public static (ChannelReader<string>, ChannelWriter<string>) Spawn(string ip, int port)
    {
        var rx = Channel.CreateUnbounded<string>();
        var tx = Channel.CreateUnbounded<string>();

        var task = Task.Run(() =>
        {
            var server = new WSServer(ip, port);
            while (true)
            {
                var connection = server.Accept();
                connection.Run(text => { rx.Writer.TryWrite(text); }, binary => { });
            }
        });

        return (rx.Reader, tx.Writer);
    }
}

class WSConnection
{
    private TcpClient _client;
    private NetworkStream _stream;

    public WSConnection(TcpClient client, NetworkStream stream)
    {
        _client = client;
        _stream = stream;
    }

    public void Run(Action<string> on_text, Action<byte[]> on_binary)
    {
        var buffer = new byte[1024];
        ulong offset = 0;
        while (true)
        {
            if (offset < 2)
            {
                while (!_stream.DataAvailable) ;
                offset += (ulong)_stream.Read(buffer, (int)offset, (int)((ulong)buffer.Length - offset));
            }

            var final_message = (buffer[0] & 0x80) != 0;
            var mask = (buffer[1] & 0x80) != 0;
            var opcode = buffer[0] & 0x0f;

            if (!final_message || opcode == 0)
            {
                Console.WriteLine("cannot handle fragmented message");
            }

            ulong message_length = buffer[1] & 0x7fu;
            ulong data_start = 2;
            if (mask)
            {
                data_start += 4;
            }

            if (message_length == 0)
            {
                Console.WriteLine("msglen == 0");
                goto cleanup;
            }
            else if (message_length == 126)
            {
                message_length = BitConverter.ToUInt16(new[] { buffer[3], buffer[2] }, 0);
                data_start += 2;
            }
            else if (message_length == 127)
            {
                message_length = BitConverter.ToUInt64(
                    new[] { buffer[9], buffer[8], buffer[7], buffer[6], buffer[5], buffer[4], buffer[3], buffer[2] },
                    0);
                data_start += 8;
            }

            if (message_length > ((ulong)buffer.Length - data_start))
            {
                Console.WriteLine("message cant fit buffer");
            }

            while (message_length > offset - data_start)
            {
                while (!_stream.DataAvailable) ;
                offset += (ulong)_stream.Read(buffer, (int)offset, (int)((ulong)buffer.Length - offset));
            }

            if (mask)
            {
                for (ulong i = 0; i < message_length; i++)
                {
                    buffer[data_start + i] ^= buffer[data_start + (i % 4) - 4];
                }
            }

            if (opcode == 1) // text
            {
                string text = Encoding.UTF8.GetString(buffer, (int)data_start, (int)message_length);
                on_text(text);
            }
            else if (opcode == 2) // binary
            {
                var data = new byte[message_length];
                Array.Copy(buffer, (int)data_start, data, 0, (int)message_length);
                on_binary(data);
            }
            else if (opcode == 8) // close
            {
                var reason = BitConverter.ToUInt16(new[] { buffer[data_start + 1], buffer[data_start] }, 0);
                var message = Encoding.UTF8.GetString(buffer, (int)(data_start + 2), (int)(message_length - 2));
                Console.WriteLine("connection closed, reason {0}: {1}", reason, message);
                var reply = new byte[] { 0x88, (byte)message_length };
                _stream.Write(reply, 0, 2);
                _stream.Write(buffer, (int)data_start, (int)message_length);
                _client.Close();
                return;
            }
            else
            {
                Console.WriteLine("opcode: {0}", opcode);
            }

            cleanup:
            for (ulong i = 0; i < offset - (data_start + message_length); i++)
            {
                buffer[i] = buffer[i + (data_start + message_length)];
            }

            offset -= (data_start + message_length);
        }
    }
}