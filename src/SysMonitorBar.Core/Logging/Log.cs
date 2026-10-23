using System.Globalization;
using System.Text;

namespace SysMonitorBar.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

public interface IAppLog
{
    void Write(LogLevel level, string message, Exception? exception = null);
}

/// <summary>Process-wide log. Writes nowhere until <see cref="Current"/> is set at startup.</summary>
public static class Log
{
    private sealed class NullLog : IAppLog
    {
        public void Write(LogLevel level, string message, Exception? exception = null)
        {
        }
    }

    public static IAppLog Current { get; set; } = new NullLog();

    public static void Info(string message) => Current.Write(LogLevel.Info, message);

    public static void Warning(string message, Exception? exception = null) => Current.Write(LogLevel.Warning, message, exception);

    public static void Error(string message, Exception? exception = null) => Current.Write(LogLevel.Error, message, exception);
}

/// <summary>
/// Appends lines to <c>SysMonitorBar.log</c> and rotates it when it exceeds <c>maxBytes</c>,
/// keeping <c>maxFiles</c> files in total (<c>SysMonitorBar.log</c>, <c>SysMonitorBar.1.log</c>, ...).
/// Thread-safe. Never throws: a log that cannot be written must not take the app down.
/// </summary>
public sealed class RollingFileLog : IAppLog, IDisposable
{
    public const string BaseName = "SysMonitorBar";

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private readonly LogLevel _minLevel;
    private readonly TimeProvider _time;
    private StreamWriter? _writer;

    public RollingFileLog(string directory, long maxBytes = 2 * 1024 * 1024, int maxFiles = 5,
        LogLevel minLevel = LogLevel.Info, TimeProvider? time = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFiles, 1);
        _directory = directory;
        _maxBytes = maxBytes;
        _maxFiles = maxFiles;
        _minLevel = minLevel;
        _time = time ?? TimeProvider.System;
    }

    public string FilePath => PathFor(0);

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        if (level < _minLevel)
        {
            return;
        }

        var line = new StringBuilder()
            .Append(_time.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(level switch { LogLevel.Debug => "DBG", LogLevel.Info => "INF", LogLevel.Warning => "WRN", _ => "ERR" })
            .Append(' ')
            .Append(message);
        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }

        lock (_gate)
        {
            try
            {
                _writer ??= Open();
                _writer.WriteLine(line.ToString());
                if (_writer.BaseStream.Length >= _maxBytes)
                {
                    Rotate();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private StreamWriter Open()
    {
        Directory.CreateDirectory(_directory);
        var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
    }

    private void Rotate()
    {
        _writer?.Dispose();
        _writer = null;
        File.Delete(PathFor(_maxFiles - 1));
        for (var i = _maxFiles - 2; i >= 0; i--)
        {
            if (File.Exists(PathFor(i)))
            {
                File.Move(PathFor(i), PathFor(i + 1));
            }
        }
    }

    private string PathFor(int index) =>
        Path.Combine(_directory, index == 0 ? BaseName + ".log" : $"{BaseName}.{index}.log");
}
