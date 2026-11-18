using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NazarethPlusCity
{
    /// <summary>
    /// Small JSON reader for glTF and the placement file. Numbers stay as text so coordinates are not rounded.
    /// </summary>
    public sealed class JsonMini
    {
        public enum Kind
        {
            Null,
            Bool,
            Number,
            String,
            Array,
            Object
        }

        public Kind Type;
        public bool Bool;
        public string Text;
        public List<JsonMini> Items;
        public Dictionary<string, JsonMini> Map;

        public bool IsNull => Type == Kind.Null;

        public double Number
        {
            get
            {
                if (Type != Kind.Number || string.IsNullOrEmpty(Text))
                    return 0.0;
                double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value);
                return value;
            }
        }

        public int Int => (int)Number;

        public string String => Type == Kind.String ? Text : null;

        public int Count => Items != null ? Items.Count : 0;

        public JsonMini this[int index] => Items[index];

        public JsonMini Get(string key)
        {
            if (Map != null && Map.TryGetValue(key, out JsonMini node))
                return node;
            return null;
        }

        public bool Try(string key, out JsonMini node)
        {
            node = Get(key);
            return node != null && !node.IsNull;
        }

        public static JsonMini Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new FormatException("JSON text was empty.");
            var parser = new Parser(json);
            JsonMini value = parser.ParseValue();
            parser.Skip();
            return value;
        }

        sealed class Parser
        {
            readonly string _text;
            int _index;

            public Parser(string text)
            {
                _text = text;
                if (_text.Length > 0 && _text[0] == '\uFEFF')
                    _index = 1;
            }

            public void Skip()
            {
                while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
                    _index++;
            }

            public JsonMini ParseValue()
            {
                Skip();
                if (_index >= _text.Length)
                    throw Error("Unexpected end of JSON.");
                char c = _text[_index];
                if (c == '{')
                    return ParseObject();
                if (c == '[')
                    return ParseArray();
                if (c == '"')
                    return new JsonMini { Type = Kind.String, Text = ParseString() };
                if (c == 't' || c == 'f')
                    return ParseBool();
                if (c == 'n')
                    return ParseNull();
                return ParseNumber();
            }

            JsonMini ParseObject()
            {
                Expect('{');
                var map = new Dictionary<string, JsonMini>();
                Skip();
                if (Peek('}'))
                {
                    _index++;
                    return new JsonMini { Type = Kind.Object, Map = map };
                }

                while (true)
                {
                    Skip();
                    string key = ParseString();
                    Skip();
                    Expect(':');
                    map[key] = ParseValue();
                    Skip();
                    if (Peek('}'))
                    {
                        _index++;
                        break;
                    }

                    Expect(',');
                }

                return new JsonMini { Type = Kind.Object, Map = map };
            }

            JsonMini ParseArray()
            {
                Expect('[');
                var items = new List<JsonMini>();
                Skip();
                if (Peek(']'))
                {
                    _index++;
                    return new JsonMini { Type = Kind.Array, Items = items };
                }

                while (true)
                {
                    items.Add(ParseValue());
                    Skip();
                    if (Peek(']'))
                    {
                        _index++;
                        break;
                    }

                    Expect(',');
                }

                return new JsonMini { Type = Kind.Array, Items = items };
            }

            JsonMini ParseBool()
            {
                if (Match("true"))
                    return new JsonMini { Type = Kind.Bool, Bool = true, Text = "true" };
                if (Match("false"))
                    return new JsonMini { Type = Kind.Bool, Bool = false, Text = "false" };
                throw Error("Expected true or false.");
            }

            JsonMini ParseNull()
            {
                if (!Match("null"))
                    throw Error("Expected null.");
                return new JsonMini { Type = Kind.Null, Text = "null" };
            }

            JsonMini ParseNumber()
            {
                int start = _index;
                if (Peek('-') || Peek('+'))
                    _index++;
                bool digit = false;
                while (_index < _text.Length && char.IsDigit(_text[_index]))
                {
                    digit = true;
                    _index++;
                }

                if (_index < _text.Length && _text[_index] == '.')
                {
                    _index++;
                    while (_index < _text.Length && char.IsDigit(_text[_index]))
                    {
                        digit = true;
                        _index++;
                    }
                }

                if (_index < _text.Length && (_text[_index] == 'e' || _text[_index] == 'E'))
                {
                    _index++;
                    if (_index < _text.Length && (_text[_index] == '+' || _text[_index] == '-'))
                        _index++;
                    while (_index < _text.Length && char.IsDigit(_text[_index]))
                        _index++;
                }

                if (!digit)
                    throw Error("Expected a number.");
                return new JsonMini { Type = Kind.Number, Text = _text.Substring(start, _index - start) };
            }

            string ParseString()
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
                        throw Error("Unfinished escape in a string.");
                    char escape = _text[_index++];
                    switch (escape)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            builder.Append(escape);
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'u':
                            if (_index + 4 > _text.Length)
                                throw Error("Unfinished unicode escape.");
                            int code = int.Parse(_text.Substring(_index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            builder.Append((char)code);
                            _index += 4;
                            break;
                        default:
                            throw Error("Unknown string escape.");
                    }
                }

                throw Error("Unfinished string.");
            }

            bool Match(string word)
            {
                if (_index + word.Length > _text.Length)
                    return false;
                if (string.CompareOrdinal(_text, _index, word, 0, word.Length) != 0)
                    return false;
                _index += word.Length;
                return true;
            }

            bool Peek(char c)
            {
                return _index < _text.Length && _text[_index] == c;
            }

            void Expect(char c)
            {
                Skip();
                if (_index >= _text.Length || _text[_index] != c)
                    throw Error("Expected '" + c + "'.");
                _index++;
            }

            FormatException Error(string message)
            {
                return new FormatException(message + " at character " + _index + ".");
            }
        }
    }
}
