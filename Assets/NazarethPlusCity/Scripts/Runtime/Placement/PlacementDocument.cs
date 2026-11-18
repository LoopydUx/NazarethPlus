using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NazarethPlusCity
{
    [Serializable]
    public class PlacementRecord
    {
        public string id;
        public string cityId;
        public string assetType;
        public string catalogId;
        public double longitude;
        public double latitude;
        public float groundOffset;
        public float rotationY;
        public float scale = 1f;
        public string createdUtc;

        public PlacementRecord Copy()
        {
            return new PlacementRecord
            {
                id = id,
                cityId = cityId,
                assetType = assetType,
                catalogId = catalogId,
                longitude = longitude,
                latitude = latitude,
                groundOffset = groundOffset,
                rotationY = rotationY,
                scale = scale,
                createdUtc = createdUtc
            };
        }
    }

    [Serializable]
    public class PlacementDocument
    {
        public const int CurrentSchema = 1;

        public int schemaVersion = CurrentSchema;
        public string cityId;
        public double originLongitude;
        public double originLatitude;
        public List<PlacementRecord> placements = new List<PlacementRecord>();
    }

    public static class PlacementJson
    {
        public static string Write(PlacementDocument document)
        {
            var builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append("  \"schemaVersion\": ").Append(document != null ? document.schemaVersion : PlacementDocument.CurrentSchema).Append(",\n");
            builder.Append("  \"cityId\": ").Append(Quote(document != null ? document.cityId : "")).Append(",\n");
            builder.Append("  \"originLongitude\": ").Append(Number(document != null ? document.originLongitude : 0.0)).Append(",\n");
            builder.Append("  \"originLatitude\": ").Append(Number(document != null ? document.originLatitude : 0.0)).Append(",\n");
            builder.Append("  \"placements\": [");
            var list = document != null ? document.placements : null;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0)
                        builder.Append(',');
                    WriteRecord(builder, list[i]);
                }
            }

            builder.Append("\n  ]\n}\n");
            return builder.ToString();
        }

        public static bool TryRead(string json, out PlacementDocument document, out string error)
        {
            document = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The placement file was empty.";
                return false;
            }

            try
            {
                if (json.Length > 0 && json[0] == '\uFEFF')
                    json = json.Substring(1);
                JsonMini root = JsonMini.Parse(json);
                document = new PlacementDocument
                {
                    schemaVersion = root.Try("schemaVersion", out JsonMini version) ? version.Int : PlacementDocument.CurrentSchema,
                    cityId = root.Try("cityId", out JsonMini city) ? city.String : "",
                    originLongitude = root.Try("originLongitude", out JsonMini lon) ? lon.Number : 0.0,
                    originLatitude = root.Try("originLatitude", out JsonMini lat) ? lat.Number : 0.0,
                    placements = new List<PlacementRecord>()
                };
                if (root.Try("placements", out JsonMini list) && list.Items != null)
                {
                    for (int i = 0; i < list.Count; i++)
                        document.placements.Add(ReadRecord(list[i]));
                }

                return true;
            }
            catch (Exception exception)
            {
                error = "The placement file could not be read (" + exception.Message + ").";
                document = null;
                return false;
            }
        }

        static void WriteRecord(StringBuilder builder, PlacementRecord record)
        {
            builder.Append("\n    {\n");
            builder.Append("      \"id\": ").Append(Quote(record != null ? record.id : "")).Append(",\n");
            builder.Append("      \"cityId\": ").Append(Quote(record != null ? record.cityId : "")).Append(",\n");
            builder.Append("      \"assetType\": ").Append(Quote(record != null ? record.assetType : "")).Append(",\n");
            builder.Append("      \"catalogId\": ").Append(Quote(record != null ? record.catalogId : "")).Append(",\n");
            builder.Append("      \"longitude\": ").Append(Number(record != null ? record.longitude : 0.0)).Append(",\n");
            builder.Append("      \"latitude\": ").Append(Number(record != null ? record.latitude : 0.0)).Append(",\n");
            builder.Append("      \"groundOffset\": ").Append(Float(record != null ? record.groundOffset : 0f)).Append(",\n");
            builder.Append("      \"rotationY\": ").Append(Float(record != null ? record.rotationY : 0f)).Append(",\n");
            builder.Append("      \"scale\": ").Append(Float(record != null && record.scale > 0f ? record.scale : 1f)).Append(",\n");
            builder.Append("      \"createdUtc\": ").Append(Quote(record != null ? record.createdUtc : "")).Append("\n    }");
        }

        static PlacementRecord ReadRecord(JsonMini node)
        {
            var record = new PlacementRecord();
            if (node == null)
                return record;
            record.id = Text(node, "id");
            record.cityId = Text(node, "cityId");
            record.assetType = Text(node, "assetType");
            record.catalogId = Text(node, "catalogId");
            record.longitude = Num(node, "longitude");
            record.latitude = Num(node, "latitude");
            record.groundOffset = (float)Num(node, "groundOffset");
            record.rotationY = (float)Num(node, "rotationY");
            record.scale = (float)Num(node, "scale");
            if (record.scale <= 0f)
                record.scale = 1f;
            record.createdUtc = Text(node, "createdUtc");
            return record;
        }

        static string Text(JsonMini node, string key)
        {
            return node.Try(key, out JsonMini value) && value.String != null ? value.String : "";
        }

        static double Num(JsonMini node, string key)
        {
            return node.Try(key, out JsonMini value) ? value.Number : 0.0;
        }

        static string Number(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        static string Float(float value)
        {
            return value.ToString("G9", CultureInfo.InvariantCulture);
        }

        static string Quote(string value)
        {
            var builder = new StringBuilder(value != null ? value.Length + 2 : 2);
            builder.Append('"');
            if (value != null)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    switch (c)
                    {
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        default:
                            builder.Append(c);
                            break;
                    }
                }
            }

            builder.Append('"');
            return builder.ToString();
        }
    }
}
