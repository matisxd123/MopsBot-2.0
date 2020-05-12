using System;
using System.Net.Sockets;
using System.Net;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using Newtonsoft.Json;

namespace MopsBot
{
    public class Server
    {
        private List<TcpClient> clients;
        private static readonly int ServerPort = 11000;
        public List<string> MessageQueue;

        public Server()
        {
            clients = new List<TcpClient>();
            MessageQueue = new List<string>();
        }

        public async Task StartServer()
        {
            IPAddress localAddr = IPAddress.Parse("0.0.0.0");
            var listener = new TcpListener(localAddr, ServerPort);
            listener.Start();

            while (true)
            {
                await Program.MopsLog(new Discord.LogMessage(Discord.LogSeverity.Info, "", $"Waiting for client to connect..."));
                var client = await listener.AcceptTcpClientAsync();
                ClientConversation(client);
                await Program.MopsLog(new Discord.LogMessage(Discord.LogSeverity.Info, "", $"{(client.Client.RemoteEndPoint as IPEndPoint).Address}:{(client.Client.RemoteEndPoint as IPEndPoint).Port} connected!"));
            }
        }

        public async Task ClientConversation(TcpClient client)
        {
            try
            {
                var clientIP = $"{(client.Client.RemoteEndPoint as IPEndPoint).Address}:{(client.Client.RemoteEndPoint as IPEndPoint).Port}";
                while (true)
                {
                    var command = await ObtainFromClient(null, client);

                    //handle command
                    await SendMessage($"Handled command number: {requestId}");
                }
            }
            catch (Exception e)
            {
                await Program.MopsLog(new Discord.LogMessage(Discord.LogSeverity.Critical, "", $"Connection failed", e));
                clients.Remove(client);
                client.Dispose();
            }
        }

        private ulong eventId = 0;
        public async Task SendMessage(object message)
        {
            var bytes = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(message));

            foreach(var client in clients){
                var stream = client.GetStream();
                await stream.WriteAsync(bytes);
            }
        }

        public async Task SendMessage(string message)
        {
            var bytes = Encoding.ASCII.GetBytes($"Event number: {eventId++}\n" + message);

            foreach(var client in clients){
                var stream = client.GetStream();
                await stream.WriteAsync(bytes);
            }
        }

        public async Task SendMessage(string message, TcpClient client)
        {
            var bytes = Encoding.ASCII.GetBytes(message);

            var stream = client.GetStream();
            await stream.WriteAsync(bytes);
        }

        private ulong requestId = 0;
        public async Task<string> ObtainFromClient(string request, TcpClient client)
        {
            string str;
            NetworkStream stream = client.GetStream();

            while (!stream.DataAvailable)
            {
                await Task.Delay(100);
            }

            byte[] buffer = new byte[1024];
            using (MemoryStream ms = new MemoryStream())
            {

                int numBytesRead;
                while (stream.DataAvailable && (numBytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, numBytesRead);
                }
                str = Encoding.ASCII.GetString(ms.ToArray(), 0, (int)ms.Length);
            }

            return str;
        }
    }
}