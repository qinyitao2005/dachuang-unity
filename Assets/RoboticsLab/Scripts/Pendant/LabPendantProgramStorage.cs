using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace RoboticsLab
{
    /// <summary>
    /// Station-isolated pendant program files. Parsing and validating program
    /// contents belongs to LabPendantProgramModel; this class only owns storage.
    /// </summary>
    public static class LabPendantProgramStorage
    {
        public const int MaximumFileBytes = 524288;
        const int FormatVersion = 1;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        [Serializable]
        sealed class Envelope
        {
            public int version;
            public int stationIndex;
            public string name;
            public string programJson;
        }

        public static string DirectoryFor(int stationIndex)
        {
            if (stationIndex < 0 || stationIndex > 2)
                throw new ArgumentOutOfRangeException("stationIndex", "工位编号必须为 0、1 或 2。");
            if (string.IsNullOrWhiteSpace(Application.persistentDataPath))
                throw new InvalidOperationException("当前平台没有可用的程序保存目录。");
            return Path.GetFullPath(Path.Combine(Application.persistentDataPath, "PendantPrograms",
                "Station" + (stationIndex + 1).ToString("00", CultureInfo.InvariantCulture)));
        }

        public static string PathFor(int stationIndex, string name)
        {
            string canonicalName = ValidatedName(name);
            string directory = DirectoryFor(stationIndex);
            string path = Path.GetFullPath(Path.Combine(directory, canonicalName + ".json"));
            if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("程序路径超出当前工位目录。", "name");
            return path;
        }

        public static bool Save(int stationIndex, string name, LabPendantProgramModel model, out string error)
        {
            error = null;
            string temporaryPath = null;
            try
            {
                string canonicalName = ValidatedName(name);
                string path = PathFor(stationIndex, canonicalName);
                if (model == null)
                {
                    error = "没有可保存的示教程序。";
                    return false;
                }

                string programJson = model.ToJson();
                if (string.IsNullOrWhiteSpace(programJson))
                {
                    error = "程序数据为空，未保存。";
                    return false;
                }
                if (Utf8.GetByteCount(programJson) > MaximumFileBytes)
                {
                    error = "程序文件超过 512 KiB，未保存。";
                    return false;
                }

                string json = JsonUtility.ToJson(new Envelope {
                    version = FormatVersion,
                    stationIndex = stationIndex,
                    name = canonicalName,
                    programJson = programJson
                }, true);
                if (Utf8.GetByteCount(json) > MaximumFileBytes)
                {
                    error = "程序文件超过 512 KiB，未保存。";
                    return false;
                }

                byte[] bytes = Utf8.GetBytes(json);
                string directory = DirectoryFor(stationIndex);
                Directory.CreateDirectory(directory);
                temporaryPath = Path.Combine(directory, "." + canonicalName + "."
                    + Guid.NewGuid().ToString("N") + ".tmp");
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                // Never delete the previous user file as a replacement fallback.
                // Both operations are atomic within this same-directory volume.
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
                return true;
            }
            catch (Exception exception) when (StorageException(exception))
            {
                error = "保存程序失败：" + exception.Message;
                return false;
            }
            finally
            {
                // This invocation only cleans up its own uniquely named file.
                if (temporaryPath != null)
                {
                    try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                    catch (Exception exception) when (StorageException(exception)) { }
                }
            }
        }

        public static bool Load(int stationIndex, string name,
            out LabPendantProgramModel model, out string error)
        {
            model = null;
            error = null;
            try
            {
                string canonicalName = ValidatedName(name);
                string path = PathFor(stationIndex, canonicalName);
                string json;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length <= 0 || stream.Length > MaximumFileBytes)
                    {
                        error = "程序文件为空或超过 512 KiB，无法加载。";
                        return false;
                    }
                    var bytes = new byte[(int)stream.Length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int count = stream.Read(bytes, offset, bytes.Length - offset);
                        if (count <= 0) throw new IOException("程序文件读取不完整。");
                        offset += count;
                    }
                    if (stream.ReadByte() != -1) throw new IOException("程序文件在读取期间发生变化。");
                    int bom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
                    json = Utf8.GetString(bytes, bom, bytes.Length - bom);
                }

                // Overwrite preserves sentinels for missing fields. In particular,
                // a missing stationIndex must not silently become Station01 (0).
                var envelope = new Envelope { stationIndex = -1 };
                JsonUtility.FromJsonOverwrite(json, envelope);
                if (envelope.version != FormatVersion)
                {
                    error = "程序文件版本不受支持，无法加载。";
                    return false;
                }
                if (envelope.stationIndex != stationIndex)
                {
                    error = "该程序属于其他工位，无法在当前工位加载。";
                    return false;
                }
                if (envelope.name == null || envelope.name != envelope.name.Trim()
                    || !string.Equals(ValidatedName(envelope.name), canonicalName, StringComparison.OrdinalIgnoreCase))
                {
                    error = "程序文件名称与内容不匹配，无法加载。";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(envelope.programJson)
                    || Utf8.GetByteCount(envelope.programJson) > MaximumFileBytes)
                {
                    error = "程序文件中的编程数据为空或过大，无法加载。";
                    return false;
                }

                LabPendantProgramModel candidate;
                if (!LabPendantProgramModel.TryFromJson(envelope.programJson, out candidate, out error)
                    || candidate == null)
                {
                    if (string.IsNullOrWhiteSpace(error)) error = "程序内容无效，无法加载。";
                    return false;
                }
                // Never expose a partially parsed program to the live controller.
                model = candidate;
                return true;
            }
            catch (Exception exception) when (StorageException(exception))
            {
                model = null;
                error = "加载程序失败：" + exception.Message;
                return false;
            }
        }

        public static string[] Names(int stationIndex)
        {
            var names = new List<string>();
            try
            {
                string directory = DirectoryFor(stationIndex);
                if (!Directory.Exists(directory)) return names.ToArray();
                foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    try { if (ValidatedName(name) == name) names.Add(name); }
                    catch (ArgumentException) { }
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (StorageException(exception)) { }
            return names.ToArray();
        }

        static string ValidatedName(string name)
        {
            if (name == null) throw new ArgumentException("请输入程序名称。", "name");
            name = name.Trim();
            if (name.Length < 1 || name.Length > 32)
                throw new ArgumentException("程序名称需为 1 至 32 个字符。", "name");
            foreach (char character in name)
                if (!char.IsLetterOrDigit(character) && character != ' ' && character != '_' && character != '-')
                    throw new ArgumentException("程序名称只能包含文字、数字、空格、下划线和短横线。", "name");
            string upper = name.ToUpperInvariant();
            if (upper == "CON" || upper == "PRN" || upper == "AUX" || upper == "NUL"
                || (upper.Length == 4 && (upper.StartsWith("COM", StringComparison.Ordinal)
                    || upper.StartsWith("LPT", StringComparison.Ordinal)) && upper[3] >= '1' && upper[3] <= '9'))
                throw new ArgumentException("该程序名称为系统保留名称，请换一个名称。", "name");
            return name;
        }

        static bool StorageException(Exception exception)
        {
            return exception is IOException || exception is UnauthorizedAccessException
                || exception is ArgumentException || exception is InvalidOperationException
                || exception is NotSupportedException || exception is FormatException
                || exception is System.Security.SecurityException;
        }
    }
}
