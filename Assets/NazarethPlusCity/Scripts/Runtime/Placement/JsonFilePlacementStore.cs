using System;
using System.IO;
using System.Text;

namespace NazarethPlusCity
{
    /// <summary>
    /// Persistence for placed objects. A later backend can replace this without changing placement.
    /// </summary>
    public interface IPlacementStore
    {
        bool TryLoad(string cityId, out PlacementDocument document, out string error);
        bool TrySave(string cityId, PlacementDocument document, out string error);
        string Location(string cityId);
    }

    public sealed class JsonFilePlacementStore : IPlacementStore
    {
        readonly string _root;

        public JsonFilePlacementStore()
        {
        }

        public JsonFilePlacementStore(string root)
        {
            _root = root;
        }

        public string Location(string cityId)
        {
            return Path.Combine(Root(), Safe(cityId) + ".json");
        }

        public bool TryLoad(string cityId, out PlacementDocument document, out string error)
        {
            document = null;
            error = null;
            string path = Location(cityId);
            string backup = path + ".bak";
            bool mainExists = File.Exists(path);
            bool backupExists = File.Exists(backup);
            if (!mainExists && !backupExists)
            {
                document = Empty(cityId);
                return true;
            }

            if (mainExists && PlacementJson.TryRead(ReadText(path), out document, out error))
            {
                if (document.placements == null)
                    document.placements = new System.Collections.Generic.List<PlacementRecord>();
                return true;
            }

            string mainError = error;
            if (backupExists && PlacementJson.TryRead(ReadText(backup), out document, out string backupError))
            {
                if (document.placements == null)
                    document.placements = new System.Collections.Generic.List<PlacementRecord>();
                error = "The placement file could not be read. The backup was loaded and the broken file was left in place. " + (mainError ?? backupError);
                return true;
            }

            document = null;
            if (string.IsNullOrEmpty(error))
                error = "The placement file and its backup could not be read. Nothing was deleted.";
            return false;
        }

        public bool TrySave(string cityId, PlacementDocument document, out string error)
        {
            error = null;
            string path = Location(cityId);
            string temp = path + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(temp, PlacementJson.Write(document), new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(temp, path, path + ".bak", true);
                else
                    File.Move(temp, path);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                if (File.Exists(temp))
                {
                    try
                    {
                        File.Delete(temp);
                    }
                    catch (Exception)
                    {
                    }
                }

                return false;
            }
        }

        string Root()
        {
            if (!string.IsNullOrEmpty(_root))
                return _root;
            return Path.Combine(UnityEngine.Application.persistentDataPath, "NazarethPlusCity", "Placements");
        }

        static PlacementDocument Empty(string cityId)
        {
            return new PlacementDocument
            {
                schemaVersion = PlacementDocument.CurrentSchema,
                cityId = cityId ?? "",
                placements = new System.Collections.Generic.List<PlacementRecord>()
            };
        }

        static string ReadText(string path)
        {
            string text = File.ReadAllText(path);
            if (text.Length > 0 && text[0] == '\uFEFF')
                return text.Substring(1);
            return text;
        }

        static string Safe(string cityId)
        {
            if (string.IsNullOrWhiteSpace(cityId))
                return "city";
            var chars = cityId.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                    chars[i] = '_';
            }

            return new string(chars);
        }
    }
}
