using System;

namespace OneHealth.Common
{
    public static class Topic
    {
        public const string Exchange = "onehealth.telemetry";

        public static string ToRoutingKey(string zona, DataType dataType, uint sensorId)
        {
            if (string.IsNullOrWhiteSpace(zona)) zona = "ZONA_DESCONHECIDA";
            return $"zone.{Normalize(zona)}.type.{dataType.ToString().ToUpperInvariant()}.sensor.{sensorId}";
        }

        public static string ToRoutingKey(string zona, string dataTypeName, uint sensorId)
        {
            if (string.IsNullOrWhiteSpace(zona)) zona = "ZONA_DESCONHECIDA";
            if (string.IsNullOrWhiteSpace(dataTypeName)) dataTypeName = "UNKNOWN";
            return $"zone.{Normalize(zona)}.type.{dataTypeName.ToUpperInvariant()}.sensor.{sensorId}";
        }

        public static string ZoneBindingPattern(string zona) => $"zone.{Normalize(zona)}.#";

        public static bool TryParse(string routingKey, out string zona, out string dataType, out uint sensorId)
        {
            zona = ""; dataType = ""; sensorId = 0;
            if (string.IsNullOrWhiteSpace(routingKey)) return false;
            var parts = routingKey.Split('.');
            if (parts.Length != 6) return false;
            if (parts[0] != "zone" || parts[2] != "type" || parts[4] != "sensor") return false;
            if (!uint.TryParse(parts[5], out sensorId)) return false;
            zona = parts[1];
            dataType = parts[3];
            return true;
        }

        private static string Normalize(string value) => value.Trim().Replace(' ', '_').ToUpperInvariant();
    }
}
