using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;

namespace PublishTool
{
    public class DirTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isDir = value is bool b && b;
            if (parameter?.ToString() == "Geometry")
            {
                // 绑定到 Path.Data
                return isDir
                    ? App.Current.FindResource("FolderIcon") as Geometry
                    : App.Current.FindResource("FileIcon") as Geometry;
            }
            if (parameter?.ToString() == "Brush")
            {
                // 绑定到 Path.Fill
                return isDir ? Brushes.Goldenrod : Brushes.SlateGray;
            }
            // 默认返回类型文本
            return isDir ? "（文件夹）" : "（文件）";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
