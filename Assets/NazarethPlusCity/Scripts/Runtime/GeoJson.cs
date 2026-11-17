using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NazarethPlusCity
{
    public struct GeoLonLat
    {
        public double Longitude;
        public double Latitude;
    }

    public sealed class GeoPolygonShape
    {
        public List<List<GeoLonLat>> Rings = new List<List<GeoLonLat>>();
    }

    public sealed class GeoFeature
    {
        public string Id;
        public string GeometryType;
        public string PropertiesJson;
        public Dictionary<string, string> Properties = new Dictionary<string, string>(StringComparer.Ordinal);
        public List<GeoPolygonShape> Polygons = new List<GeoPolygonShape>();
        public List<List<GeoLonLat>> Lines = new List<List<GeoLonLat>>();
    }

    public static class GeoJsonFile
    {
        public static List<GeoFeature> ReadFeatures(string path, out string error)
        {
            error = null;
            string text = File.ReadAllText(path);
            if (text.Length > 0 && text[0] == '\uFEFF')
                text = text.Substring(1);
            var parser = new JsonParser(text);
            JsonValue root;
            try
            {
                root = parser.ParseValue();
            }
            catch (Exception exception)
            {
                error = "JSON parse failed: " + exception.Message;
                return new List<GeoFeature>();
            }

            var features = new List<GeoFeature>();
            var collection = root != null && root.Kind == JsonKind.Object ? root.Get("features") : null;
            if (collection == null || collection.Kind != JsonKind.Array)
            {
                error = "GeoJSON root has no features array.";
                return features;
            }

            for (int i = 0; i < collection.Array.Count; i++)
            {
                var node = collection.Array[i];
                if (node == null || node.Kind != JsonKind.Object)
                {
                    features.Add(Malformed(i, "feature is not an object"));
                    continue;
                }

                features.Add(ReadFeature(node, i));
            }

            return features;
        }

        static GeoFeature Malformed(int index, string reason)
        {
            return new GeoFeature
            {
                Id = "index_" + index.ToString(CultureInfo.InvariantCulture),
                GeometryType = "Invalid",
                PropertiesJson = "{\"parse_error\":\"" + reason + "\"}"
            };
        }

        static GeoFeature ReadFeature(JsonValue feature, int index)
        {
            var result = new GeoFeature();
            var properties = feature.Get("properties");
            if (properties != null && properties.Kind == JsonKind.Object)
            {
                result.PropertiesJson = properties.Raw;
                Flatten(properties, result.Properties);
            }
            else
            {
                result.PropertiesJson = "{}";
            }

            result.Id = FirstId(feature, result.Properties, index);
            var geometry = feature.Get("geometry");
            if (geometry == null || geometry.Kind != JsonKind.Object)
            {
                result.GeometryType = "Invalid";
                result.Properties["geometry_error"] = "missing geometry";
                return result;
            }

            var typeNode = geometry.Get("type");
            result.GeometryType = typeNode != null ? typeNode.AsString() : "";
            var coordinates = geometry.Get("coordinates");
            if (coordinates == null)
            {
                result.GeometryType = "Invalid";
                result.Properties["geometry_error"] = "missing coordinates";
                return result;
            }

            switch (result.GeometryType)
            {
                case "Polygon":
                    var polygon = ReadPolygon(coordinates);
                    if (polygon != null)
                        result.Polygons.Add(polygon);
                    break;
                case "MultiPolygon":
                    if (coordinates.Kind == JsonKind.Array)
                    {
                        for (int i = 0; i < coordinates.Array.Count; i++)
                        {
                            var part = ReadPolygon(coordinates.Array[i]);
                            if (part != null)
                                result.Polygons.Add(part);
                        }
                    }
                    break;
                case "LineString":
                    var line = ReadLine(coordinates);
                    if (line != null)
                        result.Lines.Add(line);
                    break;
                case "MultiLineString":
                    if (coordinates.Kind == JsonKind.Array)
                    {
                        for (int i = 0; i < coordinates.Array.Count; i++)
                        {
                            var part = ReadLine(coordinates.Array[i]);
                            if (part != null)
                                result.Lines.Add(part);
                        }
                    }
                    break;
                default:
                    result.Properties["geometry_error"] = "unsupported geometry " + result.GeometryType;
                    result.GeometryType = "Invalid";
                    break;
            }

            return result;
        }

        static string FirstId(JsonValue feature, Dictionary<string, string> properties, int index)
        {
            var id = feature.Get("id");
            if (id != null && id.Kind != JsonKind.Null && !string.IsNullOrWhiteSpace(id.AsString()))
                return id.AsString();
            if (properties.TryGetValue("@id", out string at) && !string.IsNullOrWhiteSpace(at))
                return at;
            if (properties.TryGetValue("id", out string prop) && !string.IsNullOrWhiteSpace(prop))
                return prop;
            return "index_" + index.ToString(CultureInfo.InvariantCulture);
        }

        static void Flatten(JsonValue node, Dictionary<string, string> into)
        {
            if (node == null || node.Object == null)
                return;
            for (int i = 0; i < node.Object.Count; i++)
            {
                var pair = node.Object[i];
                if (pair.Value == null || pair.Value.Kind == JsonKind.Null)
                    continue;
                if (pair.Value.Kind == JsonKind.Object || pair.Value.Kind == JsonKind.Array)
                    into[pair.Key] = pair.Value.Raw;
                else
                    into[pair.Key] = pair.Value.AsString();
            }
        }

        static GeoPolygonShape ReadPolygon(JsonValue node)
        {
            if (node == null || node.Kind != JsonKind.Array)
                return null;
            var shape = new GeoPolygonShape();
            for (int r = 0; r < node.Array.Count; r++)
            {
                var ring = ReadLine(node.Array[r]);
                if (ring != null && ring.Count > 0)
                    shape.Rings.Add(ring);
            }

            return shape.Rings.Count > 0 ? shape : null;
        }

        static List<GeoLonLat> ReadLine(JsonValue node)
        {
            if (node == null || node.Kind != JsonKind.Array)
                return null;
            var line = new List<GeoLonLat>(node.Array.Count);
            for (int i = 0; i < node.Array.Count; i++)
            {
                var point = node.Array[i];
                if (point == null || point.Kind != JsonKind.Array || point.Array.Count < 2)
                    continue;
                if (point.Array[0].Kind != JsonKind.Number || point.Array[1].Kind != JsonKind.Number)
                    continue;
                double lon = point.Array[0].Number;
                double lat = point.Array[1].Number;
                if (double.IsNaN(lon) || double.IsNaN(lat) || double.IsInfinity(lon) || double.IsInfinity(lat))
                    continue;
                line.Add(new GeoLonLat { Longitude = lon, Latitude = lat });
            }

            return line;
        }

        public static string Property(GeoFeature feature, string key)
        {
            if (feature?.Properties == null)
                return null;
            feature.Properties.TryGetValue(key, out string value);
            return value;
        }
    }

    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    public sealed class JsonValue
    {
        public JsonKind Kind;
        public bool Bool;
        public double Number;
        public string String;
        public List<JsonValue> Array;
        public List<JsonPair> Object;
        public string Raw = "";

        public JsonValue Get(string key)
        {
            if (Object == null)
                return null;
            for (int i = 0; i < Object.Count; i++)
            {
                if (Object[i].Key == key)
                    return Object[i].Value;
            }

            return null;
        }

        public string AsString()
        {
            switch (Kind)
            {
                case JsonKind.String: return String ?? "";
                case JsonKind.Number: return Number.ToString("R", CultureInfo.InvariantCulture);
                case JsonKind.Bool: return Bool ? "true" : "false";
                case JsonKind.Null: return "";
                default: return Raw ?? "";
            }
        }
    }

    public struct JsonPair
    {
        public string Key;
        public JsonValue Value;
    }

    public sealed class JsonParser
    {
        readonly string _text;
        int _index;

        public JsonParser(string text)
        {
            _text = text ?? "";
        }

        public JsonValue ParseValue()
        {
            Skip();
            int start = _index;
            var value = ReadValue();
            if (value != null)
                value.Raw = _text.Substring(start, _index - start);
            return value;
        }

        JsonValue ReadValue()
        {
            Skip();
            int start = _index;
            var value = ReadValueCore();
            if (value != null)
                value.Raw = _text.Substring(start, Math.Max(0, _index - start));
            return value;
        }

        JsonValue ReadValueCore()
        {
            if (_index >= _text.Length)
                throw new InvalidDataException("Unexpected end of JSON at " + _index);
            char c = _text[_index];
            if (c == '{') return ReadObject();
            if (c == '[') return ReadArray();
            if (c == '"') return new JsonValue { Kind = JsonKind.String, String = ReadString() };
            if (c == 't' || c == 'f') return ReadBool();
            if (c == 'n') return ReadNull();
            return ReadNumber();
        }

        JsonValue ReadObject()
        {
            Expect('{');
            var value = new JsonValue { Kind = JsonKind.Object, Object = new List<JsonPair>() };
            Skip();
            if (Peek('}'))
            {
                _index++;
                return value;
            }

            while (true)
            {
                Skip();
                string key = ReadString();
                Skip();
                Expect(':');
                var child = ReadValue();
                value.Object.Add(new JsonPair { Key = key, Value = child });
                Skip();
                if (Peek('}'))
                {
                    _index++;
                    break;
                }

                Expect(',');
            }

            return value;
        }

        JsonValue ReadArray()
        {
            Expect('[');
            var value = new JsonValue { Kind = JsonKind.Array, Array = new List<JsonValue>() };
            Skip();
            if (Peek(']'))
            {
                _index++;
                return value;
            }

            while (true)
            {
                value.Array.Add(ReadValue());
                Skip();
                if (Peek(']'))
                {
                    _index++;
                    break;
                }

                Expect(',');
            }

            return value;
        }

        JsonValue ReadBool()
        {
            if (Match("true"))
                return new JsonValue { Kind = JsonKind.Bool, Bool = true };
            if (Match("false"))
                return new JsonValue { Kind = JsonKind.Bool, Bool = false };
            throw new InvalidDataException("Invalid boolean at " + _index);
        }

        JsonValue ReadNull()
        {
            if (!Match("null"))
                throw new InvalidDataException("Invalid null at " + _index);
            return new JsonValue { Kind = JsonKind.Null };
        }

        JsonValue ReadNumber()
        {
            int start = _index;
            if (Peek('-'))
                _index++;
            while (_index < _text.Length && char.IsDigit(_text[_index]))
                _index++;
            if (Peek('.'))
            {
                _index++;
                while (_index < _text.Length && char.IsDigit(_text[_index]))
                    _index++;
            }

            if (Peek('e') || Peek('E'))
            {
                _index++;
                if (Peek('+') || Peek('-'))
                    _index++;
                while (_index < _text.Length && char.IsDigit(_text[_index]))
                    _index++;
            }

            string slice = _text.Substring(start, _index - start);
            if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                throw new InvalidDataException("Invalid number at " + start);
            return new JsonValue { Kind = JsonKind.Number, Number = number };
        }

        string ReadString()
        {
            Expect('"');
            var builder = new StringBuilder();
            while (_index < _text.Length)
            {
                char c = _text[_index++];
                if (c == '"')
                    return builder.ToString();
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (_index >= _text.Length)
                    break;
                char escape = _text[_index++];
                switch (escape)
                {
                    case '"':
                    case '\\':
                    case '/':
                        builder.Append(escape);
                        break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (_index + 4 > _text.Length)
                            throw new InvalidDataException("Bad unicode escape at " + _index);
                        int code = int.Parse(_text.Substring(_index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        builder.Append((char)code);
                        _index += 4;
                        break;
                    default:
                        builder.Append(escape);
                        break;
                }
            }

            throw new InvalidDataException("Unterminated string at " + _index);
        }

        void Skip()
        {
            while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
                _index++;
        }

        bool Peek(char c)
        {
            return _index < _text.Length && _text[_index] == c;
        }

        void Expect(char c)
        {
            if (!Peek(c))
                throw new InvalidDataException("Expected '" + c + "' at " + _index);
            _index++;
        }

        bool Match(string token)
        {
            if (_index + token.Length > _text.Length)
                return false;
            for (int i = 0; i < token.Length; i++)
            {
                if (_text[_index + i] != token[i])
                    return false;
            }

            _index += token.Length;
            return true;
        }
    }
}
