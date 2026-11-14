using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;



namespace Island
{
    public sealed class WeatherSnapshot
    {
        public double Uv=double.NaN, Rain=double.NaN;
        public DateTime Sunrise,Sunset;
        public string Hourly="Прогноз недоступен";
        public bool Available;
        public double Temperature, FeelsLike;
        public double Humidity = double.NaN, Wind = double.NaN, Pressure = double.NaN;
        public int Code;
        public bool Day;
        public DateTime ObservedUtc, ReceivedUtc;
        public string Status = "Подключение к погоде";
        public bool IsStale { get { return Available && (DateTime.UtcNow - ObservedUtc > TimeSpan.FromHours(1) || DateTime.UtcNow - ReceivedUtc > TimeSpan.FromMinutes(30)); } }
        public string TemperatureText { get { return Available ? Degrees(Temperature) : "—°"; } }
        public static string Degrees(double value) { return Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture).Replace("-", "−") + "°"; }
        public string Description
        {
            get
            {
                if (!Available) return Status;
                if (Code == 0) return Day ? "Ясно" : "Ясная ночь";
                if (Code == 1) return "Преимущественно ясно";
                if (Code == 2) return "Переменная облачность";
                if (Code == 3) return "Пасмурно";
                if (Code == 45 || Code == 48) return "Туман";
                if (Code >= 51 && Code <= 57) return "Морось";
                if (Code >= 61 && Code <= 67) return "Дождь";
                if (Code >= 71 && Code <= 77) return "Снег";
                if (Code >= 80 && Code <= 82) return "Ливень";
                if (Code == 85 || Code == 86) return "Снегопад";
                if (Code >= 95) return "Гроза";
                return "Текущая погода";
            }
        }
        private static double Optional(Dictionary<string, object> values, string key, double min, double max)
        {
            object raw; double value;
            return values.TryGetValue(key, out raw) && raw != null && double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max ? value : double.NaN;
        }
        public static WeatherSnapshot Parse(string json, DateTime received)
        {
            var root = new JsonCodec().Deserialize<Dictionary<string, object>>(json);
            if (root == null || !root.ContainsKey("current") || !(root["current"] is Dictionary<string, object>)) throw new FormatException("Missing current weather");
            var current = (Dictionary<string, object>)root["current"];
            foreach (string name in new[] { "temperature_2m", "apparent_temperature", "weather_code", "is_day", "time" })
                if (!current.ContainsKey(name) || current[name] == null) throw new FormatException("Missing weather field");
            double temperature = Convert.ToDouble(current["temperature_2m"], CultureInfo.InvariantCulture);
            double feels = Convert.ToDouble(current["apparent_temperature"], CultureInfo.InvariantCulture);
            if (current["temperature_2m"] == null || current["apparent_temperature"] == null || double.IsNaN(temperature) || temperature < -100 || temperature > 70 || double.IsNaN(feels) || feels < -130 || feels > 100)
                throw new FormatException("Invalid weather data");
            return Enrich(new WeatherSnapshot
            {
                Available = true, Temperature = temperature, FeelsLike = feels,
                Humidity = Optional(current, "relative_humidity_2m", 0, 100), Wind = Optional(current, "wind_speed_10m", 0, 150), Pressure = Optional(current, "surface_pressure", 300, 1200),
                Code = Convert.ToInt32(current["weather_code"], CultureInfo.InvariantCulture),
                Day = Convert.ToInt32(current["is_day"], CultureInfo.InvariantCulture) == 1,
                ObservedUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(Convert.ToInt64(current["time"], CultureInfo.InvariantCulture)),
                ReceivedUtc = received
            },json);
        }
        private static WeatherSnapshot Enrich(WeatherSnapshot value,string json)
        {
            using var doc=System.Text.Json.JsonDocument.Parse(json);var root=doc.RootElement;
            if(root.TryGetProperty("daily",out var daily)){
                double First(string key)=>daily.TryGetProperty(key,out var array)&&array.GetArrayLength()>0&&array[0].TryGetDouble(out var n)?n:double.NaN;
                value.Uv=First("uv_index_max");value.Rain=First("precipitation_sum");
                var rise=First("sunrise");var set=First("sunset");if(double.IsFinite(rise))value.Sunrise=DateTime.UnixEpoch.AddSeconds(rise).ToLocalTime();if(double.IsFinite(set))value.Sunset=DateTime.UnixEpoch.AddSeconds(set).ToLocalTime();
            }
            if(root.TryGetProperty("hourly",out var hourly)&&hourly.TryGetProperty("time",out var times)&&hourly.TryGetProperty("temperature_2m",out var temps)){
                var rows=new List<string>();for(int k=0;k<Math.Min(times.GetArrayLength(),temps.GetArrayLength())&&rows.Count<4;k++){if(!times[k].TryGetInt64(out var stamp)||!temps[k].TryGetDouble(out var t))continue;var time=DateTime.UnixEpoch.AddSeconds(stamp).ToLocalTime();if(time<DateTime.Now.AddMinutes(-30))continue;rows.Add(time.ToString("HH:mm")+"   "+Degrees(t));}value.Hourly=rows.Count>0?string.Join("\n",rows):"Прогноз на сегодня завершён";
            }
            return value;
        }
    }
}
