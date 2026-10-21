using System;
using System.Windows;

namespace PaperTodo;

internal static class ReadingWindowStyles
{
    internal static ResourceDictionary Create()
    {
        var assemblyName = typeof(ReadingWindowStyles).Assembly.GetName().Name;
        return new ResourceDictionary
        {
            Source = new Uri(
                $"/{assemblyName};component/src/ReadingWindow.Styles.xaml",
                UriKind.Relative)
        };
    }
}
