using System.Net;
using System.Net.Sockets;
using System.Text.Json;

var clients = new List<ClientHandler>();

TcpListener listener = new TcpListener(IPAddress.Any, 9000);
listener.Start();
Console.WriteLine("Server started on port 9000");

while (true)
{
    var tcp = await listener.AcceptTcpClientAsync();
    var handler = new ClientHandler(tcp, clients);
    clients.Add(handler);
    _ = handler.ProcessAsync();
}

class ClientHandler
{
    private TcpClient _tcp;
    private StreamReader _reader;
    private StreamWriter _writer;
    private List<ClientHandler> _clients;
    public string Username { get; private set; } = "";

    public ClientHandler(TcpClient tcp, List<ClientHandler> clients)
    {
        _tcp = tcp;
        _clients = clients;
        var stream = tcp.GetStream();
        _reader = new StreamReader(stream);
        _writer = new StreamWriter(stream) { AutoFlush = true };
    }

    public async Task ProcessAsync()
    {
        try
        {
            // Baca username pertama kali
            Username = await _reader.ReadLineAsync() ?? "unknown";

            Console.WriteLine($"{Username} joined");
            await BroadcastAsync(new { type = "join", from = Username });
            await SendUserListAsync();

            string? line;
            while ((line = await _reader.ReadLineAsync()) != null)
            {
                Console.WriteLine($"{Username}: {line}");
                await BroadcastAsync(new { type = "msg", from = Username, text = line });
            }
        }
        catch { }
        finally
        {
            _clients.Remove(this);
            Console.WriteLine($"{Username} left");
            await BroadcastAsync(new { type = "leave", from = Username });
            await SendUserListAsync();
            _tcp.Close();
        }
    }

    private async Task BroadcastAsync(object msg)
    {
        string json = JsonSerializer.Serialize(msg);
        foreach (var c in _clients)
        {
            try { await c._writer.WriteLineAsync(json); } catch { }
        }
    }

    private async Task SendUserListAsync()
    {
        var users = _clients.Select(c => c.Username).ToArray();
        var msg = new { type = "userlist", users };
        string json = JsonSerializer.Serialize(msg);
        foreach (var c in _clients)
        {
            try { await c._writer.WriteLineAsync(json); } catch { }
        }
    }
}
