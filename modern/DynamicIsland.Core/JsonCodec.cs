using System.Text.Json;
namespace Island;

// Explicit primitive mapping preserves the old JSON wire contract (especially PID ints).
public sealed class JsonCodec
{
    private static readonly JsonSerializerOptions Options=new(){PropertyNameCaseInsensitive=true,IncludeFields=true,MaxDepth=32};
    public string Serialize<T>(T value)=>JsonSerializer.Serialize(value,Options);
    public object DeserializeObject(string json)
    {
        using var document=JsonDocument.Parse(json,new(){MaxDepth=32});
        return Read(document.RootElement);
    }
    public T Deserialize<T>(string json)
    {
        if(typeof(T)==typeof(Dictionary<string,object>)) return (T)DeserializeObject(json);
        return JsonSerializer.Deserialize<T>(json,Options);
    }
    private static object Read(JsonElement value)=>value.ValueKind switch
    {
        JsonValueKind.Object=>value.EnumerateObject().ToDictionary(x=>x.Name,x=>Read(x.Value)),
        JsonValueKind.Array=>value.EnumerateArray().Select(Read).ToArray(),
        JsonValueKind.String=>value.GetString(),
        JsonValueKind.Number=>Number(value),
        JsonValueKind.True=>true,JsonValueKind.False=>false,_=>null
    };
    private static object Number(JsonElement value)
    {
        if(value.TryGetInt32(out int i)) return i;
        if(value.TryGetInt64(out long l)) return l;
        return value.GetDouble();
    }
}
