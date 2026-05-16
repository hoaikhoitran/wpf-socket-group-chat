using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace ChatClient;

public partial class MainWindow : Window
{
    TcpClient client = new TcpClient();

    NetworkStream? stream;

    string currentUsername = "";

    public MainWindow()
    {
        InitializeComponent();

        ConnectToServer();
    }

    async void ConnectToServer()
    {
        try
        {
            await client.ConnectAsync("127.0.0.1", 5000);

            stream = client.GetStream();

            // Show a friendly system message in the chat UI.
            AddChatMessage("System", "Connected to server!");
            ScrollChatToBottom();

            _ = Task.Run(ReceiveMessages);
        }
        catch
        {
            MessageBox.Show("Cannot connect to server!");
        }
    }

    private void SetName_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UsernameBox.Text))
        {
            MessageBox.Show("Enter username!");
            return;
        }

        currentUsername = UsernameBox.Text;

        // Swap the input UI for a simple "Logged in as" label.
        UsernameBox.Visibility = Visibility.Collapsed;
        SetNameButton.Visibility = Visibility.Collapsed;
        LoggedInLabel.Text = $"Logged in as: {currentUsername}";
        LoggedInLabel.Visibility = Visibility.Visible;

        MessageBox.Show($"Welcome {currentUsername}");
    }

    void ReceiveMessages()
    {
        byte[] buffer = new byte[1024];

        while (true)
        {
            try
            {
                if (stream == null)
                    return;

                int count = stream.Read(buffer, 0, buffer.Length);

                if (count <= 0)
                    break;

                string msg = Encoding.UTF8.GetString(buffer, 0, count);

                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // Route sticker messages to the sticker renderer.
                        if (msg.Contains("[STICKER:"))
                        {
                            ShowSticker(msg);
                        }
                        else
                        {
                            // Regular text messages become chat bubbles.
                            AddChatMessage(msg);
                        }

                        ScrollChatToBottom();
                    }
                    catch (Exception ex)
                    {
                        AddChatMessage("System", ex.Message);
                        ScrollChatToBottom();
                    }
                });
            }
            catch
            {
                break;
            }
        }
    }

    void ShowSticker(string msg)
    {
        int start = msg.IndexOf("[STICKER:") + 9;
        int end = msg.IndexOf("]");

        string stickerName = msg.Substring(start, end - start);

        string username = msg.Split(":")[0];

        // Build a small UI block for the sticker: username + image.
        StackPanel panel = new StackPanel();
        panel.Margin = new Thickness(0, 6, 0, 6);

        TextBlock text = new TextBlock();
        text.Text = username;
        text.FontWeight = FontWeights.SemiBold;
        text.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#F36F21")!;
        text.Margin = new Thickness(6, 0, 0, 6);

        Image img = new Image();

        img.Width = 150;
        img.Height = 150;

        string path =
            $"D:/PRN212_SE1935/SocketChatApp/ChatClient/Stickers/{stickerName}.png";

        BitmapImage bitmap = new BitmapImage();

        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();

        img.Source = bitmap;

        panel.Children.Add(text);
        panel.Children.Add(img);

        ChatList.Items.Add(panel);
        ScrollChatToBottom();
    }

    void AddChatMessage(string rawMessage)
    {
        // Split "username: message" into two parts for cleaner rendering.
        int splitIndex = rawMessage.IndexOf(": ");
        if (splitIndex > 0)
        {
            string username = rawMessage.Substring(0, splitIndex);
            string message = rawMessage.Substring(splitIndex + 2);
            AddChatMessage(username, message);
        }
        else
        {
            // Fallback if the message doesn't contain a username prefix.
            AddChatMessage("User", rawMessage);
        }
    }

    void AddChatMessage(string username, string message)
    {
        // Container stacks username (top) and bubble (bottom).
        StackPanel container = new StackPanel();
        container.Margin = new Thickness(0, 6, 0, 6);

        // Username line (orange accent).
        TextBlock nameText = new TextBlock();
        nameText.Text = username;
        nameText.FontWeight = FontWeights.SemiBold;
        nameText.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#F36F21")!;
        nameText.Margin = new Thickness(6, 0, 0, 4);

        // Bubble background with rounded corners.
        Border bubble = new Border();
        bubble.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FFF7F1")!;
        bubble.BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FFE1D0")!;
        bubble.BorderThickness = new Thickness(1);
        bubble.CornerRadius = new CornerRadius(10);
        bubble.Padding = new Thickness(10, 8, 10, 8);
        bubble.MaxWidth = 640;

        // Message text inside the bubble.
        TextBlock msgText = new TextBlock();
        msgText.Text = message;
        msgText.TextWrapping = TextWrapping.Wrap;
        msgText.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#2C2C2C")!;

        bubble.Child = msgText;

        container.Children.Add(nameText);
        container.Children.Add(bubble);

        ChatList.Items.Add(container);
    }

    void ScrollChatToBottom()
    {
        // Keep the newest message visible by scrolling to the last item.
        if (ChatList.Items.Count > 0)
        {
            ChatList.ScrollIntoView(ChatList.Items[ChatList.Items.Count - 1]);
        }
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (stream == null)
                return;

            string username = currentUsername;

            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show("Set username first!");
                return;
            }

            string message = MessageInput.Text;

            if (string.IsNullOrWhiteSpace(message))
                return;

            string fullMessage = $"{username}: {message}";

            byte[] data = Encoding.UTF8.GetBytes(fullMessage);

            stream.Write(data, 0, data.Length);

            MessageInput.Clear();
        }
        catch
        {
            MessageBox.Show("Send failed!");
        }
    }

    private void Emoji_Click(object sender, RoutedEventArgs e)
    {
        Button btn = (Button)sender;

        MessageInput.Text += btn.Content.ToString();
    }

    private void Sticker_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (stream == null)
                return;

            string username = currentUsername;

            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show("Set username first!");
                return;
            }

            Button btn = (Button)sender;

            string sticker = btn.Tag.ToString()!;

            string msg = $"{username}: [STICKER:{sticker}]";

            byte[] data = Encoding.UTF8.GetBytes(msg);

            stream.Write(data, 0, data.Length);
        }
        catch
        {
            MessageBox.Show("Sticker send failed!");
        }
    }
}