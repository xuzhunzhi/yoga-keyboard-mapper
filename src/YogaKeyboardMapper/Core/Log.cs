using System.Text;

namespace YogaKeyboardMapper.Core;

/// <summary>Very small file logger, handy when the window misbehaves.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppConfig.Folder, "app.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppConfig.Folder);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1024 * 1024)
                {
                    File.Delete(FilePath);
                }

                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}";
                File.AppendAllText(FilePath, line, new UTF8Encoding(false));
            }
        }
        catch
        {
            // logging must never break the app
        }
    }
}
