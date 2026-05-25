using HikrobotCameraProcessor.FromSamples;
using System;
using System.Collections.Generic;
using System.Text;

namespace HikrobotCameraProcessor.Classes
{
    public static class MethodHelper
    {
        /// <summary>
        /// Универсальный валидатор пути. Проверяет синтаксис и создает папку.
        /// </summary>
        public static bool TryPrepareDirectory(List<LinesParameters> parameters, string paramName, uint lineIndex, out string verifiedPath)
        {
            verifiedPath = string.Empty;

            // Находим параметр по его имени
            var pathParam = parameters?.FirstOrDefault(p => p.Name == paramName);
            string path = pathParam?.Value;

            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    AppLogger.LogError(null, $"[Line {lineIndex}] Ошибка: Путь к папке '{paramName}' не задан в настройках.");
                    return false;
                }

                // Проверяем синтаксис Windows (на наличие запрещенных символов ?, *, |, <, >)
                verifiedPath = Path.GetFullPath(path);

                // Пытаемся создать папку, если её ещё нет
                if (!Directory.Exists(verifiedPath))
                {
                    Directory.CreateDirectory(verifiedPath);
                }

                return true;
            }
            catch (ArgumentException)
            {
                AppLogger.LogError(null, $"[Line {lineIndex}] Ошибка ввода: Указанный путь '{path}' содержит недопустимые символы.");
                return false;
            }
            catch (PathTooLongException)
            {
                AppLogger.LogError(null, $"[Line {lineIndex}] Ошибка ввода: Указанный путь слишком длинный (превышает лимит Windows).");
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.LogError(ex, $"[Line {lineIndex}] Не удалось получить доступ или создать директорию '{path}'");
                return false;
            }
        }

        /// <summary>
        /// Универсальный валидатор задержки. Парсит число и проверяет адекватность диапазона.
        /// </summary>
        public static bool TryGetDelayMs(List<LinesParameters> parameters, string paramName, uint lineIndex, out int delayMs)
        {
            delayMs = 0;

            // Находим параметр задержки по его имени
            var delayParam = parameters?.FirstOrDefault(p => p.Name == paramName);
            if (delayParam == null || string.IsNullOrWhiteSpace(delayParam.Value))
            {
                // Если параметр не найден или пуст — задержка равна 0 (это безопасно)
                return true;
            }

            // Проверяем, что введено именно целое число
            if (!int.TryParse(delayParam.Value, out int parsedValue))
            {
                AppLogger.LogError(null, $"[Line {lineIndex}] Ошибка ввода: Значение '{paramName}' ({delayParam.Value}) не является числом.");
                return false;
            }

            // Защита от дурака: проверяем диапазон (0 - 5000 мс)
            if (parsedValue < 0 || parsedValue > 5000)
            {
                AppLogger.LogError(null, $"[Line {lineIndex}] Ошибка: Значение '{paramName}' ({parsedValue} мс) вне допустимого диапазона (0 - 5000).");
                return false;
            }

            delayMs = parsedValue;
            return true;
        }
    }



}
