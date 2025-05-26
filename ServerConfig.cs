using System.ComponentModel;
using System.Security;

public class ServerConfig : INotifyPropertyChanged
{
    private string _name;
    private string _serverIP;
    private string _username;
    private string _password;
    private string _localPath;
    private string _remotePath;
    private string _serviceName;
    private string _exeName;
    private bool _isBackup;
    private int _backupCount=1;


    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            OnPropertyChanged(nameof(Name));
            // 自动更新 ServiceName
            ServiceName = $"API_{_name}";
        }
    }
    /// <summary>
    /// 是否开启备份功能
    /// </summary>
    public bool IsBackup
    {
        get => _isBackup;
        set
        {
            _isBackup = value;
            OnPropertyChanged(nameof(IsBackup));
        }
    }
    /// <summary>
    /// 备份文件数量
    /// </summary>
    public int BackupCount
    {
        get => _backupCount;
        set
        {
            _backupCount = value;
            OnPropertyChanged(nameof(BackupCount));
        }
    }


    public string ServerIP
    {
        get => _serverIP;
        set
        {
            _serverIP = value;
            OnPropertyChanged(nameof(ServerIP));
        }
    }

    public string Username
    {
        get => _username;
        set
        {
            _username = value;
            OnPropertyChanged(nameof(Username));
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            _password = value;
            OnPropertyChanged(nameof(Password));
        }
    }
    public string LocalPath
    {
        get => _localPath;
        set
        {
            _localPath = value;
            OnPropertyChanged(nameof(LocalPath));
        }
    }
    public string RemotePath
    {
        get => _remotePath;
        set
        {
            _remotePath = value;
            OnPropertyChanged(nameof(RemotePath));
        }
    }
    public string ServiceName
    {
        get => _serviceName;
        set
        {
            _serviceName = value;
            OnPropertyChanged(nameof(ServiceName));
        }
    }
    public string ExeName
    {
        get => _exeName;
        set
        {
            _exeName = value;
            OnPropertyChanged(nameof(ExeName));
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}