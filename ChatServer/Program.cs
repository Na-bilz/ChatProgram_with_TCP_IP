// ChatServer/Program.cs
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ChatServer
{
    class Program
    {
        static ConcurrentDictionary<string, Client> clients = new ConcurrentDictionary<string, Client>();

        static async Task Main(string[] args)
        {
            int port = 9000;
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"[Server] started on port {port}");

            _ = Task.Run(async () => {
                while (true)
                {
                    var tcp = await listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(tcp); // fire-and-forget
                }
            });

            Console.WriteLine("Type 'exit' to stop server.");
            while (Console.ReadLine()?.Trim().ToLower() != "exit") { }
            Console.WriteLine("Stopping listener...");
            listener.Stop();
        }

        static async Task HandleClientAsync(TcpClient tcp)
        {
            string username = null;
            NetworkStream ns = tcp.GetStream();
            var reader = new StreamReader(ns, Encoding.UTF8);
            var writer = new StreamWriter(ns, Encoding.UTF8) { AutoFlush = true };

            try
            {
                // Expect first line = join message
                var first = await reader.ReadLineAsync();
                if (first == null) { tcp.Close(); return; }
                var joinMsg = JsonSerializer.Deserialize<Message>(first);
                if (joinMsg == null || joinMsg.type != "join" || string.IsNullOrWhiteSpace(joinMsg.from))
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new Message { type = "sys", text = "First message must be join" }));
                    tcp.Close();
                    return;
                }

                username = joinMsg.from.Trim();
                if (!clients.TryAdd(username, new Client { Username = username, Tcp = tcp, Writer = writer }))
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new Message { type = "sys", text = "Username taken" }));
                    tcp.Close();
                    return;
                }

                Console.WriteLine($"[Server] {username} connected.");
                await BroadcastAsync(new Message { type = "join", from = username, ts = Timestamp() });

                // read loop
                while (true)
                {
                    var raw = await reader.ReadLineAsync();
                    if (raw == null) break; // disconnected
                    var msg = JsonSerializer.Deserialize<Message>(raw);
                    if (msg == null) continue;
                    msg.from = username; // enforce
                    msg.ts = Timestamp();

                    switch (msg.type)
                    {
                        case "msg":
                            await BroadcastAsync(msg);
                            break;
                        case "pm":
                            if (!string.IsNullOrWhiteSpace(msg.to) && clients.TryGetValue(msg.to, out var target))
                            {
                                await SendToAsync(target, msg);
                                // also echo to sender
                                if (clients.TryGetValue(username, out var sender)) await SendToAsync(sender, msg);
                            }
                            else
                            {
                                if (clients.TryGetValue(username, out var sender))
                                {
                                    await SendToAsync(sender, new Message { type = "sys", text = $"User '{msg.to}' not found", ts = Timestamp() });
                                }
                            }
                            break;
                        case "leave":
                            // client signaled leaving
                            break;
                        default:
                            // ignore/extend
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] error for {username}: {ex.Message}");
            }
            finally
            {
                if (username != null)
                {
                    clients.TryRemove(username, out _);
                    Console.WriteLine($"[Server] {username} disconnected.");
                    await BroadcastAsync(new Message { type = "leave", from = username, ts = Timestamp() });
                }
                try { tcp.Close(); } catch { }
            }
        }

        static long Timestamp() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        static async Task BroadcastAsync(Message m)
        {
            var raw = JsonSerializer.Serialize(m);
            foreach (var kv in clients)
            {
                await SendToAsync(kv.Value, m);
            }
        }

        static async Task SendToAsync(Client client, Message m)
        {
            var raw = JsonSerializer.Serialize(m);
            try
            {
                await client.WriteLock.WaitAsync();
                await client.Writer.WriteLineAsync(raw);
            }
            catch
            {
                // ignore write failures
            }
            finally
            {
                client.WriteLock.Release();
            }
        }
    }

    class Client
    {
        public string Username;
        public TcpClient Tcp;
        public StreamWriter Writer;
        public SemaphoreSlim WriteLock = new SemaphoreSlim(1, 1);
    }

    class Message
    {
        public string type { get; set; }
        public string from { get; set; }
        public string to { get; set; }
        public string text { get; set; }
        public long ts { get; set; }
    }
}
