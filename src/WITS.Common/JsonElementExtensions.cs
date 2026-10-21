using System.Text.Json;

namespace WITS.Common;

public static class JsonElementExtensions
{
    public static object? ToPrimitive(this JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt32(out int intValue))
                {
                    return intValue;
                }
                if (element.TryGetInt64(out long longValue))
                {
                    return longValue;
                }
                if (element.TryGetDecimal(out decimal decimalValue))
                {
                    return decimalValue;
                }
                if (element.TryGetDouble(out double doubleValue))
                {
                    return doubleValue;
                }
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetBoolean();
            case JsonValueKind.Null:
                return null;
            case JsonValueKind.Object:
            case JsonValueKind.Array:
                throw new InvalidOperationException("Cannot convert a JSON object or array to a primitive type.");
            default:
                throw new InvalidOperationException($"Unsupported JsonValueKind: {element.ValueKind}");
        }

        throw new InvalidOperationException("Unable to convert JsonElement to a primitive type.");
    }
}
