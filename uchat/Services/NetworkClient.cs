using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using uchat.Protocol;

namespace uchat.Services;

public class NetworkClient : IDisposable
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private bool _isConnected;
    private readonly byte[] _buffer = new byte[4096];
    
    // Use a message queue to prevent burst sending
    private readonly ConcurrentQueue<(ProtocolMessage Message, TaskCompletionSource<bool> Tcs)> _sendQueue = new();
    private readonly SemaphoreSlim _queueSignal = new SemaphoreSlim(0);
    private Task? _sendLoopTask;
    private CancellationTokenSource? _sendCts;

    public event EventHandler<ProtocolMessage>? MessageReceived;
    public event EventHandler? ConnectionLost;

    public bool IsConnected => _isConnected && _tcpClient?.Connected == true;

    private Exception? _lastConnectionError;
    public Exception? LastConnectionError => _lastConnectionError;

    public async Task<bool> ConnectAsync(string serverIp, int port)
    {
        _lastConnectionError = null;
        try
        {
            if (_tcpClient != null)
            {
                Dispose();
            }

            System.Diagnostics.Debug.WriteLine($"Attempting to connect to {serverIp}:{port}");
            _tcpClient = new TcpClient();

            _tcpClient.NoDelay = true;
            _tcpClient.ReceiveBufferSize = 65536;
            _tcpClient.SendBufferSize = 65536;
            _tcpClient.ReceiveTimeout = 0;
            _tcpClient.SendTimeout = 30000;

            await _tcpClient.ConnectAsync(serverIp, port).ConfigureAwait(false);

            System.Diagnostics.Debug.WriteLine($"Connected successfully to {serverIp}:{port}");

            _stream = _tcpClient.GetStream();
            _stream.ReadTimeout = Timeout.Infinite;
            _stream.WriteTimeout = 30000;
            _isConnected = true;

            _ = Task.Run(ReceiveMessagesAsync);
            
            _sendCts = new CancellationTokenSource();
            _sendLoopTask = Task.Run(() => ProcessSendQueueAsync(_sendCts.Token));
            
            return true;
        }
        catch (Exception ex)
        {
            _lastConnectionError = ex;
            System.Diagnostics.Debug.WriteLine($"Connection error: {ex.Message}");
            Dispose();
            return false;
        }
    }

    private async Task ReceiveMessagesAsync()
    {
        System.Diagnostics.Debug.WriteLine("ReceiveMessagesAsync started");
        try
        {
            while (_isConnected && _tcpClient?.Connected == true && _stream != null)
            {
                var lengthBuffer = new byte[4];
                int bytesRead = 0;

                while (bytesRead < 4)
                {
                    int read = await _stream.ReadAsync(lengthBuffer, bytesRead, 4 - bytesRead);
                    if (read == 0) throw new IOException("Connection closed by server (header)");
                    bytesRead += read;
                }

                int messageLength = BitConverter.ToInt32(lengthBuffer, 0);

                if (messageLength <= 0 || messageLength > 100 * 1024 * 1024)
                {
                    throw new IOException($"Invalid message length: {messageLength}");
                }

                var messageBuffer = new byte[messageLength];
                int totalBytesRead = 0;

                while (totalBytesRead < messageLength)
                {
                    int read = await _stream.ReadAsync(messageBuffer, totalBytesRead, messageLength - totalBytesRead);
                    if (read == 0) throw new IOException("Connection closed by server (body)");
                    totalBytesRead += read;
                }

                _ = Task.Run(() =>
                {
                    try
                    {
                        var message = ProtocolMessage.FromBytes(messageBuffer);
                        MessageReceived?.Invoke(this, message);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error parsing message: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Receive loop error: {ex.Message}");
            HandleDisconnect();
        }
    }

    private async Task ProcessSendQueueAsync(CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine("Send queue processor started");
        while (!ct.IsCancellationRequested && _isConnected)
        {
            try
            {
                await _queueSignal.WaitAsync(1000, ct);
                
                while (_sendQueue.TryDequeue(out var item))
                {
                    if (!IsConnected || _stream == null)
                    {
                        item.Tcs.TrySetResult(false);
                        continue;
                    }
                    
                    try
                    {
                        var bytes = item.Message.ToBytes();
                        var lengthBytes = BitConverter.GetBytes(bytes.Length);

                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                        
                        await _stream.WriteAsync(lengthBytes.AsMemory(0, lengthBytes.Length), linkedCts.Token);
                        await _stream.WriteAsync(bytes.AsMemory(0, bytes.Length), linkedCts.Token);
                        await _stream.FlushAsync(linkedCts.Token);

                        item.Tcs.TrySetResult(true);
                        
                        if (_sendQueue.Count > 0)
                        {
                            await Task.Delay(10, ct);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        item.Tcs.TrySetResult(false);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Send error: {ex.Message}");
                        item.Tcs.TrySetResult(false);
                        HandleDisconnect();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Send queue error: {ex.Message}");
            }
        }
        System.Diagnostics.Debug.WriteLine("Send queue processor ended");
    }

    public async Task<bool> SendMessageAsync(ProtocolMessage message)
    {
        if (!IsConnected || _stream == null) return false;

        var tcs = new TaskCompletionSource<bool>();
        _sendQueue.Enqueue((message, tcs));
        _queueSignal.Release();
        
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            var resultTask = tcs.Task;
            var timeoutTask = Task.Delay(Timeout.Infinite, timeoutCts.Token);
            
            var completedTask = await Task.WhenAny(resultTask, timeoutTask);
            if (completedTask == resultTask)
            {
                return await resultTask;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"SendMessageAsync timed out for message type: {message.Type}");
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ReconnectAsync(string serverIp, int port)
    {
        Dispose();
        await Task.Delay(1000);
        return await ConnectAsync(serverIp, port);
    }

    private void HandleDisconnect()
    {
        if (_isConnected)
        {
            _isConnected = false;
            ConnectionLost?.Invoke(this, EventArgs.Empty);
        }
        
        try { _sendCts?.Cancel(); } catch { }
        
        while (_sendQueue.TryDequeue(out var item))
        {
            item.Tcs.TrySetResult(false);
        }
    }

    public void Dispose()
    {
        _isConnected = false;
        try { _sendCts?.Cancel(); } catch { }
        try { _stream?.Dispose(); } catch { }
        try { _tcpClient?.Close(); } catch { }
        _stream = null;
        _tcpClient = null;
        
        while (_sendQueue.TryDequeue(out var item))
        {
            item.Tcs.TrySetResult(false);
        }
    }
}
