using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace eft_dma_shared.Common.Misc.Config
{
    public class SafeEnumConverter : JsonConverter<object>
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsEnum;
        }

        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var enumString = reader.GetString();
                if (Enum.TryParse(typeToConvert, enumString, true, out var result))
                {
                    return result;
                }

                var enumValues = Enum.GetValues(typeToConvert);
                if (enumValues.Length > 0)
                {
                    return enumValues.GetValue(0);
                }
            }
            else if (reader.TokenType == JsonTokenType.Number && typeToConvert.IsEnum)
            {
                try
                {
                    var underlyingType = Enum.GetUnderlyingType(typeToConvert);
                    object enumValue;

                    if (underlyingType == typeof(int))
                    {
                        enumValue = reader.GetInt32();
                    }
                    else if (underlyingType == typeof(uint))
                    {
                        enumValue = reader.GetUInt32();
                    }
                    else if (underlyingType == typeof(long))
                    {
                        enumValue = reader.GetInt64();
                    }
                    else if (underlyingType == typeof(ulong))
                    {
                        enumValue = reader.GetUInt64();
                    }
                    else if (underlyingType == typeof(short))
                    {
                        enumValue = reader.GetInt16();
                    }
                    else if (underlyingType == typeof(ushort))
                    {
                        enumValue = reader.GetUInt16();
                    }
                    else if (underlyingType == typeof(byte))
                    {
                        enumValue = reader.GetByte();
                    }
                    else if (underlyingType == typeof(sbyte))
                    {
                        enumValue = reader.GetSByte();
                    }
                    else
                    {
                        enumValue = reader.GetInt32();
                    }

                    try
                    {
                        return Enum.ToObject(typeToConvert, enumValue);
                    }
                    catch
                    {
                        var enumValues = Enum.GetValues(typeToConvert);
                        if (enumValues.Length > 0)
                        {
                            return enumValues.GetValue(0);
                        }
                    }
                }
                catch
                {
                    var enumValues = Enum.GetValues(typeToConvert);
                    if (enumValues.Length > 0)
                    {
                        return enumValues.GetValue(0);
                    }
                }
            }

            try
            {
                return Activator.CreateInstance(typeToConvert);
            }
            catch
            {
                var enumValues = Enum.GetValues(typeToConvert);
                if (enumValues.Length > 0)
                {
                    return enumValues.GetValue(0);
                }
                return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(value.ToString());
        }

        public override object ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var enumString = reader.GetString();
                if (Enum.TryParse(typeToConvert, enumString, true, out var result))
                {
                    return result;
                }

                var enumValues = Enum.GetValues(typeToConvert);
                if (enumValues.Length > 0)
                {
                    return enumValues.GetValue(0);
                }
            }

            return Activator.CreateInstance(typeToConvert);
        }

        public override void WriteAsPropertyName(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WritePropertyName("null");
                return;
            }

            writer.WritePropertyName(value.ToString());
        }
    }
}