using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ChatClient;

public enum MessageType
{
    Text,
    File
}

public sealed class ChatMessage : INotifyPropertyChanged
{
    Guid fileId;
    string sender = string.Empty;
    string content = string.Empty;
    bool isMine;
    DateTime time = DateTime.Now;
    MessageType type;
    string fileName = string.Empty;
    long fileSize;
    double progress;
    bool isCompleted;
    bool isCanceled;
    bool canCancel;
    bool canRetry;
    string speedText = "";
    string etaText = "";
    string statusText = "";
    string localPath = string.Empty;

    public Guid FileId
    {
        get => fileId;
        set => SetField(ref fileId, value);
    }

    public string Sender
    {
        get => sender;
        set => SetField(ref sender, value);
    }

    public string Content
    {
        get => content;
        set => SetField(ref content, value);
    }

    public bool IsMine
    {
        get => isMine;
        set => SetField(ref isMine, value);
    }

    public DateTime Time
    {
        get => time;
        set => SetField(ref time, value);
    }

    public MessageType Type
    {
        get => type;
        set => SetField(ref type, value);
    }

    public string FileName
    {
        get => fileName;
        set => SetField(ref fileName, value);
    }

    public long FileSize
    {
        get => fileSize;
        set => SetField(ref fileSize, value);
    }

    public double Progress
    {
        get => progress;
        set => SetField(ref progress, value);
    }

    public bool IsCompleted
    {
        get => isCompleted;
        set => SetField(ref isCompleted, value);
    }

    public bool IsCanceled
    {
        get => isCanceled;
        set => SetField(ref isCanceled, value);
    }

    public bool CanCancel
    {
        get => canCancel;
        set => SetField(ref canCancel, value);
    }

    public bool CanRetry
    {
        get => canRetry;
        set => SetField(ref canRetry, value);
    }

    public string SpeedText
    {
        get => speedText;
        set => SetField(ref speedText, value);
    }

    public string EtaText
    {
        get => etaText;
        set => SetField(ref etaText, value);
    }

    public string StatusText
    {
        get => statusText;
        set => SetField(ref statusText, value);
    }

    public string LocalPath
    {
        get => localPath;
        set => SetField(ref localPath, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
