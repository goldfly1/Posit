using System.Text.Json;
namespace Posit.Contracts.Serialization;
public static class __Compat
{
    public static JsonSerializerOptions PhaseOptions => PositJson.Options;
}
