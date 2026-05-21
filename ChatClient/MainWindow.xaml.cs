using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ChatClient;

public partial class MainWindow : Window
{
    const int ChunkSize = 256 * 1024;

    readonly TcpClient client = new TcpClient();
    readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
    readonly Dictionary<Guid, FileTransferSession> fileTransfers = new Dictionary<Guid, FileTransferSession>();
    readonly Dictionary<Guid, CancellationTokenSource> outgoingTransfers = new Dictionary<Guid, CancellationTokenSource>();
    readonly object transferLock = new object();
    readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    NetworkStream? stream;
    string currentUsername = "";

    public ObservableCollection<ChatMessage> Messages { get; } = new ObservableCollection<ChatMessage>();

    public MainWindow()
    {
        InitializeComponent();

        DataContext = this;
        Messages.CollectionChanged += (_, __) => ScrollChatToBottom();

        _ = ConnectToServerAsync();
    }

    async Task ConnectToServerAsync()
    {
        try
        {
            await client.ConnectAsync("127.0.0.1", 5000);
            stream = client.GetStream();

            AddSystemMessage("Connected to server!");
            _ = ReceiveLoopAsync();
        }
        catch
        {
            MessageBox.Show("Cannot connect to server!");
        }
    }

    void AddSystemMessage(string text)
    {
        Messages.Add(new ChatMessage
        {
            Sender = "System",
            Content = text,
            IsMine = false,
            Time = DateTime.Now,
            Type = MessageType.Text
        });
    }

    async Task ReceiveLoopAsync()
    {
        if (stream == null)
            return;

        try
        {
            // Async read loop keeps the UI thread free while data arrives.
            while (true)
            {
                byte[]? header = await ReadExactAsync(stream, 5);
                if (header == null)
                    break;

                FrameType frameType = (FrameType)header[0];
                int length = BitConverter.ToInt32(header, 1);

                if (length < 0 || length > 1024 * 1024)
                    break;

                byte[]? payload = await ReadExactAsync(stream, length);
                if (payload == null)
                    break;

                switch (frameType)
                {
                    case FrameType.Text:
                        HandleTextPayload(payload);
                        break;
                    case FrameType.FileMeta:
                        HandleFileMetaPayload(payload);
                        break;
                    case FrameType.FileChunk:
                        await HandleFileChunkPayloadAsync(payload);
                        break;
                    case FrameType.FileCancel:
                        HandleFileCancelPayload(payload);
                        break;
                }
            }
        }
        catch
        {
            Dispatcher.Invoke(() => AddSystemMessage("Disconnected from server."));
        }
    }

    void HandleTextPayload(byte[] payload)
    {
        string json = Encoding.UTF8.GetString(payload);
        MessagePacket? packet = JsonSerializer.Deserialize<MessagePacket>(json, jsonOptions);

        if (packet == null)
            return;

        string sender = packet.Sender;
        string content = packet.Content;

        if (string.IsNullOrWhiteSpace(sender))
        {
            int splitIndex = content.IndexOf(": ");
            if (splitIndex > 0)
            {
                sender = content.Substring(0, splitIndex);
                content = content.Substring(splitIndex + 2);
            }
        }

        bool isMine = string.Equals(sender, currentUsername, StringComparison.OrdinalIgnoreCase);
        string displaySender = isMine ? "Me" : sender;

        Dispatcher.Invoke(() =>
        {
            Messages.Add(new ChatMessage
            {
                Sender = displaySender,
                Content = content,
                IsMine = isMine,
                Time = DateTime.Now,
                Type = MessageType.Text
            });
        });
    }

    void HandleFileMetaPayload(byte[] payload)
    {
        string json = Encoding.UTF8.GetString(payload);
        MessagePacket? packet = JsonSerializer.Deserialize<MessagePacket>(json, jsonOptions);

        if (packet == null || string.IsNullOrWhiteSpace(packet.FileId))
            return;

        Guid fileId = Guid.Parse(packet.FileId);
        string tempPath = Path.Combine(Path.GetTempPath(), $"chatfile_{fileId:N}");

        ChatMessage message = new ChatMessage
        {
            Sender = string.Equals(packet.Sender, currentUsername, StringComparison.OrdinalIgnoreCase) ? "Me" : packet.Sender,
            IsMine = string.Equals(packet.Sender, currentUsername, StringComparison.OrdinalIgnoreCase),
            Time = DateTime.Now,
            Type = MessageType.File,
            FileId = fileId,
            FileName = packet.FileName,
            FileSize = packet.FileSize,
            Progress = 0,
            IsCompleted = false,
            IsCanceled = false,
            CanCancel = true,
            CanRetry = false,
            SpeedText = "0 MB/s",
            EtaText = "ETA --:--:--",
            StatusText = "Transferring",
            LocalPath = tempPath
        };

        Dispatcher.Invoke(() => Messages.Add(message));

        FileStream fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, ChunkSize, true);
        lock (transferLock)
        {
            fileTransfers[fileId] = new FileTransferSession(fileId, packet.FileName, packet.FileSize, fileStream, message);
        }
    }

    void HandleFileCancelPayload(byte[] payload)
    {
        if (payload.Length < 16)
            return;

        Guid fileId = new Guid(payload.AsSpan(0, 16));

        FileTransferSession? session = null;
        CancellationTokenSource? outgoingCts = null;

        lock (transferLock)
        {
            fileTransfers.TryGetValue(fileId, out session);
            outgoingTransfers.TryGetValue(fileId, out outgoingCts);
        }

        if (session != null)
        {
            // Sender canceled; clean up any partial file and update UI.
            CancelIncomingTransfer(session, deleteTempFile: true, statusText: "Canceled by sender");
        }

        if (outgoingCts != null)
        {
            outgoingCts.Cancel();
        }
    }

    async Task HandleFileChunkPayloadAsync(byte[] payload)
    {
        if (payload.Length < 16)
            return;

        Guid fileId = new Guid(payload.AsSpan(0, 16));

        FileTransferSession? session = null;
        lock (transferLock)
        {
            fileTransfers.TryGetValue(fileId, out session);
        }

        if (session == null)
            return;

        if (session.IsCanceled)
            return;

        int chunkLength = payload.Length - 16;
        try
        {
            await session.Stream.WriteAsync(payload, 16, chunkLength, session.CancelSource.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        session.ReceivedBytes += chunkLength;
        long lastSampleBytes = session.LastSampleBytes;
        long lastSampleTicks = session.LastSampleTicks;
        double smoothedSpeedBytes = session.SmoothedSpeedBytes;

        UpdateTransferUi(session.Message, session.ReceivedBytes, session.FileSize, session.Stopwatch,
            ref lastSampleBytes, ref lastSampleTicks, ref smoothedSpeedBytes);

        session.LastSampleBytes = lastSampleBytes;
        session.LastSampleTicks = lastSampleTicks;
        session.SmoothedSpeedBytes = smoothedSpeedBytes;

        if (session.ReceivedBytes >= session.FileSize)
        {
            await session.Stream.FlushAsync();
            session.Stream.Close();
            lock (transferLock)
            {
                fileTransfers.Remove(fileId);
            }
            MarkCompleted(session.Message);
        }
    }

    async Task SendTextAsync(string text)
    {
        if (stream == null)
            return;

        MessagePacket packet = new MessagePacket
        {
            Type = "text",
            Sender = currentUsername,
            Content = text,
            MessageId = Guid.NewGuid().ToString("N")
        };

        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet, jsonOptions));
        await SendFrameAsync(FrameType.Text, payload);

        Dispatcher.Invoke(() =>
        {
            Messages.Add(new ChatMessage
            {
                Sender = "Me",
                Content = text,
                IsMine = true,
                Time = DateTime.Now,
                Type = MessageType.Text
            });
        });
    }

    async Task SendFileMetaAsync(Guid fileId, string fileName, long fileSize)
    {
        if (stream == null)
            return;

        MessagePacket packet = new MessagePacket
        {
            Type = "file",
            Sender = currentUsername,
            FileName = fileName,
            FileSize = fileSize,
            FileId = fileId.ToString("D"),
            MessageId = Guid.NewGuid().ToString("N")
        };

        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet, jsonOptions));
        await SendFrameAsync(FrameType.FileMeta, payload);
    }

    async Task SendFileAsync(Guid fileId, string filePath, long fileSize, ChatMessage message, CancellationToken token)
    {
        // Stream the file in chunks to avoid loading it all into memory.
        await using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, true);
        byte[] buffer = new byte[ChunkSize];

        Stopwatch stopwatch = Stopwatch.StartNew();
        long lastSampleBytes = 0;
        long lastSampleTicks = 0;
        double smoothedSpeedBytes = 0;

        int read;
        long sent = 0;
        try
        {
            while ((read = await fileStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
            {
                // Each chunk is sent asynchronously, so text messages can interleave with file transfer.
                byte[] payload = new byte[16 + read];
                fileId.ToByteArray().CopyTo(payload, 0);
                Buffer.BlockCopy(buffer, 0, payload, 16, read);

                await SendFrameAsync(FrameType.FileChunk, payload);

                sent += read;
                UpdateTransferUi(message, sent, fileSize, stopwatch,
                    ref lastSampleBytes, ref lastSampleTicks, ref smoothedSpeedBytes);

                await Task.Yield();
            }

            MarkCompleted(message);
        }
        catch (OperationCanceledException)
        {
            MarkCanceled(message, "Canceled", canRetry: true);
        }
    }

    async Task SendFrameAsync(FrameType frameType, byte[] payload)
    {
        if (stream == null)
            return;

        byte[] header = new byte[5];
        header[0] = (byte)frameType;
        BitConverter.GetBytes(payload.Length).CopyTo(header, 1);

        // Serialize writes so frames are not interleaved on the same TCP stream.
        await sendLock.WaitAsync();
        try
        {
            await stream.WriteAsync(header, 0, header.Length);
            await stream.WriteAsync(payload, 0, payload.Length);
        }
        finally
        {
            sendLock.Release();
        }
    }

    static async Task<byte[]?> ReadExactAsync(NetworkStream networkStream, int length)
    {
        byte[] buffer = new byte[length];
        int offset = 0;

        while (offset < length)
        {
            int read = await networkStream.ReadAsync(buffer, offset, length - offset);
            if (read <= 0)
                return null;

            offset += read;
        }

        return buffer;
    }

    void ScrollChatToBottom()
    {
        if (ChatList.Items.Count > 0)
        {
            ChatList.ScrollIntoView(ChatList.Items[^1]);
        }
    }

    async Task SendFileCancelAsync(Guid fileId)
    {
        byte[] payload = fileId.ToByteArray();
        await SendFrameAsync(FrameType.FileCancel, payload);
    }

    void UpdateTransferUi(ChatMessage message, long transferredBytes, long totalBytes, Stopwatch stopwatch,
        ref long lastSampleBytes, ref long lastSampleTicks, ref double smoothedSpeedBytes)
    {
        if (message.IsCanceled || message.IsCompleted)
            return;

        double progress = totalBytes == 0 ? 100 : (transferredBytes * 100d / totalBytes);

        long currentTicks = stopwatch.ElapsedTicks;
        if (lastSampleTicks == 0)
        {
            lastSampleTicks = currentTicks;
            lastSampleBytes = transferredBytes;
        }

        double deltaSeconds = (currentTicks - lastSampleTicks) / (double)Stopwatch.Frequency;
        long deltaBytes = transferredBytes - lastSampleBytes;

        if (deltaSeconds >= 0.25 && deltaBytes >= 0)
        {
            double instantSpeed = deltaBytes / Math.Max(0.001, deltaSeconds);
            // Exponential moving average to keep speed/ETA stable for UI display.
            smoothedSpeedBytes = smoothedSpeedBytes <= 0 ? instantSpeed : (smoothedSpeedBytes * 0.8 + instantSpeed * 0.2);
            lastSampleTicks = currentTicks;
            lastSampleBytes = transferredBytes;
        }

        double speedBytes = Math.Max(1, smoothedSpeedBytes);
        double speedMb = speedBytes / (1024 * 1024d);
        double remainingBytes = Math.Max(0, totalBytes - transferredBytes);
        double remainingSeconds = remainingBytes / speedBytes;
        TimeSpan eta = TimeSpan.FromSeconds(remainingSeconds);

        Dispatcher.Invoke(() =>
        {
            message.Progress = Math.Min(100, progress);
            message.SpeedText = $"{speedMb:0.00} MB/s";
            message.EtaText = $"ETA {eta:hh\\:mm\\:ss}";
            message.StatusText = "Transferring";
        });
    }

    void MarkCompleted(ChatMessage message)
    {
        Dispatcher.Invoke(() =>
        {
            message.Progress = 100;
            message.IsCompleted = true;
            message.CanCancel = false;
            message.CanRetry = false;
            message.StatusText = "Completed";
            message.EtaText = "ETA 00:00:00";
        });

        if (message.FileId != Guid.Empty)
        {
            lock (transferLock)
            {
                if (outgoingTransfers.TryGetValue(message.FileId, out CancellationTokenSource? cts))
                {
                    cts.Dispose();
                    outgoingTransfers.Remove(message.FileId);
                }
            }
        }
    }

    void MarkCanceled(ChatMessage message, string statusText, bool canRetry)
    {
        Dispatcher.Invoke(() =>
        {
            message.IsCanceled = true;
            message.CanCancel = false;
            message.CanRetry = canRetry;
            message.StatusText = statusText;
            message.SpeedText = "0 MB/s";
            message.EtaText = "ETA --:--:--";
        });

        if (message.FileId != Guid.Empty)
        {
            lock (transferLock)
            {
                if (outgoingTransfers.TryGetValue(message.FileId, out CancellationTokenSource? cts))
                {
                    cts.Dispose();
                    outgoingTransfers.Remove(message.FileId);
                }
            }
        }
    }

    void CancelIncomingTransfer(FileTransferSession session, bool deleteTempFile, string statusText)
    {
        session.IsCanceled = true;
        session.CancelSource.Cancel();

        try
        {
            session.Stream.Close();
        }
        catch
        {
        }

        if (deleteTempFile)
        {
            try
            {
                if (File.Exists(session.Message.LocalPath))
                    File.Delete(session.Message.LocalPath);
            }
            catch
            {
            }
        }

        lock (transferLock)
        {
            fileTransfers.Remove(session.FileId);
        }
        MarkCanceled(session.Message, statusText, canRetry: false);
    }

    void EnsureUsername()
    {
        if (string.IsNullOrWhiteSpace(currentUsername))
            throw new InvalidOperationException("Set username first!");
    }

    private void SetName_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UsernameBox.Text))
        {
            MessageBox.Show("Enter username!");
            return;
        }

        currentUsername = UsernameBox.Text.Trim();

        UsernameBox.Visibility = Visibility.Collapsed;
        SetNameButton.Visibility = Visibility.Collapsed;
        LoggedInLabel.Text = $"Logged in as: {currentUsername}";
        LoggedInLabel.Visibility = Visibility.Visible;

        MessageBox.Show($"Welcome {currentUsername}");
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureUsername();

            string message = MessageInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(message))
                return;

            await SendTextAsync(message);
            MessageInput.Clear();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
        }
    }

    private void Emoji_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
            MessageInput.Text += btn.Content?.ToString();
    }

    private async void Sticker_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureUsername();

            if (sender is not Button btn)
                return;

            string sticker = btn.Tag?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(sticker))
                return;

            await SendTextAsync($"[STICKER:{sticker}]");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
        }
    }

    private async void SendFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureUsername();

            OpenFileDialog dialog = new OpenFileDialog();
            if (dialog.ShowDialog() != true)
                return;

            FileInfo fileInfo = new FileInfo(dialog.FileName);
            Guid fileId = Guid.NewGuid();
            CancellationTokenSource cts = new CancellationTokenSource();
            lock (transferLock)
            {
                outgoingTransfers[fileId] = cts;
            }

            ChatMessage message = new ChatMessage
            {
                Sender = "Me",
                IsMine = true,
                Time = DateTime.Now,
                Type = MessageType.File,
                FileId = fileId,
                FileName = fileInfo.Name,
                FileSize = fileInfo.Length,
                Progress = 0,
                IsCompleted = false,
                IsCanceled = false,
                CanCancel = true,
                CanRetry = false,
                SpeedText = "0 MB/s",
                EtaText = "ETA --:--:--",
                StatusText = "Transferring",
                LocalPath = fileInfo.FullName
            };

            Messages.Add(message);

            await SendFileMetaAsync(fileId, fileInfo.Name, fileInfo.Length);
            _ = SendFileAsync(fileId, fileInfo.FullName, fileInfo.Length, message, cts.Token);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
        }
    }

    private void SaveFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ChatMessage message)
            return;

        try
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                FileName = message.FileName
            };

            if (dialog.ShowDialog() != true)
                return;

            if (File.Exists(message.LocalPath))
            {
                File.Copy(message.LocalPath, dialog.FileName, true);
                message.LocalPath = dialog.FileName;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
        }
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ChatMessage message)
            return;

        try
        {
            if (!File.Exists(message.LocalPath))
            {
                MessageBox.Show("File not found. Save it first.");
                return;
            }

            Process.Start(new ProcessStartInfo(message.LocalPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
        }
    }

    private async void CancelFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ChatMessage message)
            return;

        if (message.IsCompleted || message.IsCanceled)
            return;

        try
        {
            if (message.FileId == Guid.Empty)
                return;

            CancellationTokenSource? outgoingCts = null;
            FileTransferSession? incomingSession = null;

            lock (transferLock)
            {
                outgoingTransfers.TryGetValue(message.FileId, out outgoingCts);
                fileTransfers.TryGetValue(message.FileId, out incomingSession);
            }

            if (message.IsMine && outgoingCts != null)
            {
                // Cancel only this file transfer; chat continues normally.
                outgoingCts.Cancel();
                await SendFileCancelAsync(message.FileId);
                MarkCanceled(message, "Canceled", canRetry: true);
            }
            else if (incomingSession != null)
            {
                CancelIncomingTransfer(incomingSession, deleteTempFile: true, statusText: "Canceled");
            }
            else
            {
                MarkCanceled(message, "Canceled", canRetry: message.IsMine);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
        }
    }

    private async void RetryFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ChatMessage message)
            return;

        if (!message.IsMine || !message.CanRetry)
            return;

        try
        {
            if (!File.Exists(message.LocalPath))
            {
                MessageBox.Show("File not found for retry.");
                return;
            }

            FileInfo fileInfo = new FileInfo(message.LocalPath);
            Guid newFileId = Guid.NewGuid();
            CancellationTokenSource cts = new CancellationTokenSource();

            lock (transferLock)
            {
                outgoingTransfers[newFileId] = cts;
            }

            // Retry uses a new file id and new CTS to keep transfers independent.
            message.FileId = newFileId;
            message.FileName = fileInfo.Name;
            message.FileSize = fileInfo.Length;
            message.Progress = 0;
            message.IsCompleted = false;
            message.IsCanceled = false;
            message.CanCancel = true;
            message.CanRetry = false;
            message.SpeedText = "0 MB/s";
            message.EtaText = "ETA --:--:--";
            message.StatusText = "Transferring";

            await SendFileMetaAsync(newFileId, fileInfo.Name, fileInfo.Length);
            _ = SendFileAsync(newFileId, fileInfo.FullName, fileInfo.Length, message, cts.Token);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
        }
    }
}

enum FrameType : byte
{
    Text = 1,
    FileMeta = 2,
    FileChunk = 3,
    FileCancel = 4
}

sealed class FileTransferSession
{
    public Guid FileId { get; }
    public string FileName { get; }
    public long FileSize { get; }
    public FileStream Stream { get; }
    public ChatMessage Message { get; }
    public long ReceivedBytes { get; set; }
    public bool IsCanceled { get; set; }
    public CancellationTokenSource CancelSource { get; }
    public Stopwatch Stopwatch { get; }
    public long LastSampleBytes { get; set; }
    public long LastSampleTicks { get; set; }
    public double SmoothedSpeedBytes { get; set; }

    public FileTransferSession(Guid fileId, string fileName, long fileSize, FileStream stream, ChatMessage message)
    {
        FileId = fileId;
        FileName = fileName;
        FileSize = fileSize;
        Stream = stream;
        Message = message;
        ReceivedBytes = 0;
        IsCanceled = false;
        CancelSource = new CancellationTokenSource();
        Stopwatch = Stopwatch.StartNew();
        LastSampleBytes = 0;
        LastSampleTicks = 0;
        SmoothedSpeedBytes = 0;
    }
}

public sealed class MessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TextTemplate { get; set; }
    public DataTemplate? FileTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is ChatMessage message && message.Type == MessageType.File)
            return FileTemplate;

        return TextTemplate;
    }
}

public sealed class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not long size)
            return "0 B";

        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = size;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class StickerVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        string? content = value as string;
        return IsSticker(content) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    public static bool IsSticker(string? content)
    {
        return !string.IsNullOrWhiteSpace(content) && content.StartsWith("[STICKER:", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class TextVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        string? content = value as string;
        return StickerVisibilityConverter.IsSticker(content) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class StickerImageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        string? content = value as string;
        if (!StickerVisibilityConverter.IsSticker(content))
            return DependencyProperty.UnsetValue;

        int start = content!.IndexOf("[STICKER:", StringComparison.OrdinalIgnoreCase) + 9;
        int end = content.IndexOf("]", StringComparison.OrdinalIgnoreCase);

        if (end <= start)
            return DependencyProperty.UnsetValue;

        string stickerName = content.Substring(start, end - start);
        string path = Path.Combine(AppContext.BaseDirectory, "Stickers", $"{stickerName}.png");

        if (!File.Exists(path))
            return DependencyProperty.UnsetValue;

        BitmapImage bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();

        return bitmap;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}