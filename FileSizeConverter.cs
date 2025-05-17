using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Data;

namespace PublishTool
{
    public class FileSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null)
                return "";

            if (value is long size)
            {
                if (size == 0)
                    return "0";
                if (size < 1024)
                    return size + " B";
                if (size < 1024 * 1024)
                    return (size / 1024.0).ToString("F2") + " KB";
                if (size < 1024 * 1024 * 1024)
                    return (size / 1024.0 / 1024.0).ToString("F2") + " MB";
                return (size / 1024.0 / 1024.0 / 1024.0).ToString("F2") + " GB";
            }
            return value.ToString();
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
    }

}
