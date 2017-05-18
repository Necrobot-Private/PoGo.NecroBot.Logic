#region using directives

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.State;

#endregion

namespace PoGo.NecroBot.Logic.Logging
{
    /// <summary>
    ///     The FileLogger is a simple logger which writes all logs to the Console.
    /// </summary>
    public class FileLogger : ILogger
    {
        private readonly LogLevel _maxLogLevel;
        private string logPath;
        private ConcurrentQueue<LogEvent> _messageQueue = new ConcurrentQueue<LogEvent>();

        public void TurnOffLogBuffering()
        {
            // No buffering for file logger.
        }

        public void SetSession(ISession session)
        {
            // No need for session
        }

        /// <summary>
        ///     To create a FileLogger, we must define a maximum log level.
        ///     All levels above won't be logged.
        /// </summary>
        /// <param name="maxLogLevel"></param>
        public FileLogger(LogLevel maxLogLevel, string fileName = "", string subPath = "")
        {
            _maxLogLevel = maxLogLevel;

            string path = Path.Combine(Directory.GetCurrentDirectory(), subPath, "Logs");
            Directory.CreateDirectory(path);

            if (string.IsNullOrEmpty(fileName))
                fileName = $"NecroBot2-{DateTime.Today.ToString("yyyy-MM-dd")}-{DateTime.Now.ToString("HH-mm-ss")}.txt";

            logPath = Path.Combine(path, fileName);
        }

        private object ioLocker = new object();

        /// <summary>
        ///     Log a specific message by LogLevel. Won't log if the LogLevel is greater than the maxLogLevel set.
        /// </summary>
        /// <param name="message">The message to log. The current time will be prepended.</param>
        /// <param name="level">Optional. Default <see cref="LogLevel.Info" />.</param>
        /// <param name="color">Optional. Default is auotmatic</param>
        public void Write(string message, LogLevel level = LogLevel.Info, ConsoleColor color = ConsoleColor.Black)
        {
            if (level > _maxLogLevel)
                return;

            var finalMessage = Logger.GetFinalMessage(message, level, color);

            lock (ioLocker)
            {
                // Add message to the queue
                _messageQueue.Enqueue(new LogEvent
                {
                    Message = finalMessage,
                    Color = Logger.GetHexColor(Console.ForegroundColor)
                });

                using (StreamWriter sw = File.AppendText(logPath))
                {
                    while (_messageQueue.TryDequeue(out LogEvent logEventToSend))
                    {
                        sw.WriteLine(finalMessage);
                    }
                }
            }
        }

        public void LineSelect(int lineChar = 0, int linesUp = 1)
        {
            // No line select for file logger.
        }
    }
}