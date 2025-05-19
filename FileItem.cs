using Renci.SshNet.Messages;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace PublishTool
{
    public class FileItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }
        private string _fullPath;
        public string FullPath
        {
            get => _fullPath;
            set
            {
                _fullPath = value;
                OnPropertyChanged(nameof(FullPath));
            }
        }
        private bool _isDirectory;
        public bool IsDirectory
        {
            get => _isDirectory;
            set
            {
                _isDirectory = value;
                OnPropertyChanged(nameof(IsDirectory));
            }
        }
        private DateTime _lastWriteTime;
        public DateTime LastWriteTime
        {
            get => _lastWriteTime;
            set
            {
                _lastWriteTime = value;
                OnPropertyChanged(nameof(LastWriteTime));
            }
        }

        private long _size;
        public long Size
        {
            get => _size;
            set
            {
                _size = value;
                OnPropertyChanged(nameof(Size));
            }
        }

        public string SizeDisplay
        {
            get
            {
                if (IsDirectory) return "";
                long size = Size;
                if (size < 1024) return $"{size} B";
                if (size < 1024 * 1024) return $"{size / 1024.0:F2} KB";
                if (size < 1024 * 1024 * 1024) return $"{size / 1024.0 / 1024.0:F2} MB";
                return $"{size / 1024.0 / 1024.0 / 1024.0:F2} GB";
            }
        }
    }
}
