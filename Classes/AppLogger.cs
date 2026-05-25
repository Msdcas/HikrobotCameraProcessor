using NLog;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HikrobotCameraProcessor.FromSamples
{ 
    public static class AppLogger
    {
        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Логирует ошибку, автоматически определяя имя метода и класс
        /// </summary>
        public static void LogError(Exception ex, string customMessage = "")
        {
            // Получаем информацию о методе, который вызвал LogError
            StackTrace stackTrace = new StackTrace();
            MethodBase callerMethod = stackTrace.GetFrame(1)?.GetMethod();

            string className = callerMethod?.DeclaringType?.Name ?? "UnknownClass";
            string methodName = callerMethod?.Name ?? "UnknownMethod";

            // Создаем логгер именно для того класса, где произошла ошибка
            ILogger dynamicLogger = LogManager.GetLogger(className);

            string finalMessage = string.IsNullOrEmpty(customMessage)
                ? $"Error in {methodName}: {ex.Message}"
                : $"{customMessage} (Method: {methodName}) | {ex.Message}";

            dynamicLogger.Error(ex, finalMessage);
        }

        /// <summary>
        /// Информационное сообщение
        /// </summary>
        public static void LogInfo(string message, [CallerMemberName] string methodName = "")
        {
            _logger.Info($"[{methodName}] {message}");
        }
    }

}
