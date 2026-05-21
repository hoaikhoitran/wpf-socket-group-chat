using System.Net;
using System.Net.Sockets;
using System.Text;

TcpListener server = new TcpListener(IPAddress.Any, 5000);
server.Start();

Console.WriteLine("SERVER STARTED");

List<ClientConnection> clients = new();
object clientsLock = new();

while (true)
{
    TcpClient client = await server.AcceptTcpClientAsync();
    ClientConnection connection = new ClientConnection(client);

    lock (clientsLock)
    {
        clients.Add(connection);
    }

    Console.WriteLine("Client connected");

    _ = HandleClientAsync(connection);
}

async Task HandleClientAsync(ClientConnection connection)
{
    try
    {
        NetworkStream stream = connection.Stream;

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

            await BroadcastFrameAsync(connection, frameType, payload);
        }
    }
    catch
    {
        // Ignore per-client failures and clean up.
    }
    finally
    {
        lock (clientsLock)
        {
            clients.Remove(connection);
        }

        try
        {
            connection.Client.Close();
        }
        catch
        {
        }
    }
}

async Task BroadcastFrameAsync(ClientConnection sender, FrameType frameType, byte[] payload)
{
    List<ClientConnection> snapshot;
    lock (clientsLock)
    {
        snapshot = clients.ToList();
    }

    foreach (var client in snapshot)
    {
        try
        {
            if (ReferenceEquals(client, sender))
                continue;

            await client.SendFrameAsync(frameType, payload);
        }
        catch
        {
            lock (clientsLock)
            {
                clients.Remove(client);
            }
        }
    }
}

static async Task<byte[]?> ReadExactAsync(NetworkStream stream, int length)
{
    byte[] buffer = new byte[length];
    int offset = 0;

    while (offset < length)
    {
        int read = await stream.ReadAsync(buffer, offset, length - offset);
        if (read <= 0)
            return null;

        offset += read;
    }

    return buffer;
}

enum FrameType : byte
{
    Text = 1,
    FileMeta = 2,
    FileChunk = 3,
    FileCancel = 4
}

sealed class ClientConnection
{
    readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

    public TcpClient Client { get; }
    public NetworkStream Stream { get; }

    public ClientConnection(TcpClient client)
    {
        Client = client;
        Stream = client.GetStream();
    }

    public async Task SendFrameAsync(FrameType frameType, byte[] payload)
    {
        byte[] header = new byte[5];
        header[0] = (byte)frameType;
        BitConverter.GetBytes(payload.Length).CopyTo(header, 1);

        await sendLock.WaitAsync();
        try
        {
            await Stream.WriteAsync(header, 0, header.Length);
            await Stream.WriteAsync(payload, 0, payload.Length);
        }
        finally
        {
            sendLock.Release();
        }
    }
}