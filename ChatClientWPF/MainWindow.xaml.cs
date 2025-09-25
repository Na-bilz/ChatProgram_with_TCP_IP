using System;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ChatClient
{
    public partial class MainWindow : Window
    {
        private TcpClient? _tcp;
        private StreamWriter? _writer;
        private StreamReader? _reader;
        private CancellationTokenSource? _cts;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_tcp != null && _tcp.Connected)
                {
                    MessageBox.Show("Already connected to server.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(TxtUsername.Text))
                {
                    MessageBox.Show("Blank username is not allowed.");
                    return;
                }

                string ip = TxtIp.Text;
                int port = int.Parse(TxtPort.Text);

                _tcp = new TcpClient();
                await _tcp.ConnectAsync(ip, port);

                var stream = _tcp.GetStream();
                _writer = new StreamWriter(stream) { AutoFlush = true };
                _reader = new StreamReader(stream);
                _cts = new CancellationTokenSource();

                ListMessages.Items.Add("Connected to server.");
                _ = Task.Run(() => ListenAsync(_cts.Token));

                // Kirim username ke server (optional tergantung protokol)
                await _writer.WriteLineAsync(TxtUsername.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Connection failed: " + ex.Message);
            }
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _tcp?.Close();
            ListUsers.Items.Clear();
            ListMessages.Items.Add("Disconnected from server.");
        }

        private async void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            if (_writer != null && _tcp?.Connected == true)
            {
                string msg = TxtMessage.Text;
                if (string.IsNullOrWhiteSpace(msg))
                {
                    ListMessages.Items.Add("Blank messages not allowed");
                    return;
                }
                await _writer.WriteLineAsync(msg);
                TxtMessage.Clear();
            }
        }

        private async Task ListenAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _reader != null)
                {
                    string? line = await _reader.ReadLineAsync();
                    if (line == null) break;

                    var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    string type = root.GetProperty("type").GetString() ?? "";

                    Dispatcher.Invoke(() =>
                    {
                        switch (type)
                        {
                            case "msg":
                                string from = root.GetProperty("from").GetString() ?? "?";
                                string text = root.GetProperty("text").GetString() ?? "";
                                ListMessages.Items.Add($"{from}: {text}");
                                break;

                            case "join":
                                ListMessages.Items.Add($"*** {root.GetProperty("from").GetString()} joined ***");
                                break;

                            case "leave":
                                ListMessages.Items.Add($"*** {root.GetProperty("from").GetString()} left ***");
                                break;

                            case "userlist":
                                ListUsers.Items.Clear();
                                foreach (var u in root.GetProperty("users").EnumerateArray())
                                {
                                    ListUsers.Items.Add(u.GetString());
                                }
                                break;
                        }
                    });
                }
            }
            catch (IOException)
            {
                if (!token.IsCancellationRequested)
                {
                    Dispatcher.Invoke(() =>
                        ListMessages.Items.Add("Lost connection to server."));
                }
            }
            catch (SocketException)
            {
                if (!token.IsCancellationRequested)
                {
                    Dispatcher.Invoke(() =>
                        ListMessages.Items.Add("Server unreachable or closed unexpectedly."));
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Dispatcher.Invoke(() =>
                    {
                        ListMessages.Items.Add("Error: " + ex.Message);
                    });
                }
            }
        }
    }
}
