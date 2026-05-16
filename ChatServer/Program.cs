using System.Net;
using System.Net.Sockets;
using System.Text;

TcpListener server = new TcpListener(IPAddress.Any, 5000);

server.Start();

Console.WriteLine("SERVER STARTED");

List<TcpClient> clients = new();

while (true)
{
    TcpClient client = server.AcceptTcpClient();

    clients.Add(client);

    Console.WriteLine("Client connected");

    _ = Task.Run(() =>
    {
        try
        {
            NetworkStream stream = client.GetStream();

            byte[] buffer = new byte[1024];

            while (true)
            {
                int count = stream.Read(buffer, 0, buffer.Length);

                if (count <= 0)
                    break;

                string msg = Encoding.UTF8.GetString(buffer, 0, count);

                Console.WriteLine(msg);

                foreach (var c in clients.ToList())
                {
                    try
                    {
                        if (!c.Connected)
                            continue;

                        NetworkStream clientStream = c.GetStream();

                        byte[] data = Encoding.UTF8.GetBytes(msg);

                        clientStream.Write(data, 0, data.Length);
                    }
                    catch
                    {
                        clients.Remove(c);
                    }
                }
            }
        }
        catch
        {
            clients.Remove(client);
        }
    });
}