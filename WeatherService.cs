using System.Net.Http;
using System.Text.Json;

namespace WeatherTrayApp;

/// <summary>
/// Service for fetching weather data from Open-Meteo API.
/// </summary>
public class WeatherService : IDisposable
{
    private readonly HttpClient _httpClient;
    private const string WEATHER_API_BASE = "https://api.open-meteo.com/v1/forecast";
    private const string GEOCODING_API_BASE = "https://geocoding-api.open-meteo.com/v1/search";

    public WeatherService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "WeatherTrayApp/1.0");
    }

    /// <summary>
    /// Fetches extended weather data (current, hourly, daily) for the given coordinates.
    /// </summary>
    public async Task<WeatherData> FetchExtendedWeatherAsync(double latitude, double longitude, string temperatureUnit = "fahrenheit")
    {
        var hourlyParams = "temperature_2m,weather_code,precipitation_probability,wind_speed_10m";
        var dailyParams = "weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,precipitation_sum,sunrise,sunset";
        var currentParams = "temperature_2m,weather_code,wind_speed_10m,wind_direction_10m,relative_humidity_2m,apparent_temperature,uv_index";

        string url = $"{WEATHER_API_BASE}?latitude={latitude}&longitude={longitude}" +
                     $"&current={currentParams}" +
                     $"&hourly={hourlyParams}" +
                     $"&daily={dailyParams}" +
                     $"&temperature_unit={temperatureUnit}" +
                     $"&wind_speed_unit=mph" +
                     $"&precipitation_unit=inch" +
                     $"&timezone=auto" +
                     $"&forecast_days=7";

        var response = await _httpClient.GetStringAsync(url);
        using var doc = JsonDocument.Parse(response);

        var data = new WeatherData();

        // Parse current weather
        if (doc.RootElement.TryGetProperty("current", out var current))
        {
            data.Current = new CurrentWeather
            {
                Temperature = current.GetProperty("temperature_2m").GetDouble(),
                WeatherCode = current.GetProperty("weather_code").GetInt32(),
                WindSpeed = current.GetProperty("wind_speed_10m").GetDouble(),
                WindDirection = current.GetProperty("wind_direction_10m").GetInt32(),
                Humidity = current.GetProperty("relative_humidity_2m").GetInt32(),
                FeelsLike = current.GetProperty("apparent_temperature").GetDouble(),
                UVIndex = current.TryGetProperty("uv_index", out var uv) ? uv.GetDouble() : 0,
                Time = DateTime.Parse(current.GetProperty("time").GetString() ?? DateTime.Now.ToString())
            };
        }

        // Parse hourly forecast (next 24 hours)
        if (doc.RootElement.TryGetProperty("hourly", out var hourly))
        {
            var times = hourly.GetProperty("time").EnumerateArray().ToList();
            var temps = hourly.GetProperty("temperature_2m").EnumerateArray().ToList();
            var codes = hourly.GetProperty("weather_code").EnumerateArray().ToList();
            var precip = hourly.GetProperty("precipitation_probability").EnumerateArray().ToList();
            var winds = hourly.GetProperty("wind_speed_10m").EnumerateArray().ToList();

            // Get next 24 hours from current time
            var now = DateTime.Now;
            for (int i = 0; i < Math.Min(48, times.Count); i++) // 48 to have buffer for finding current hour
            {
                var time = DateTime.Parse(times[i].GetString() ?? "");
                if (time < now.AddHours(-1)) continue;
                if (data.Hourly.Count >= 24) break;

                data.Hourly.Add(new HourlyForecast
                {
                    Time = time,
                    Temperature = temps[i].GetDouble(),
                    WeatherCode = codes[i].GetInt32(),
                    PrecipitationProbability = precip[i].TryGetInt32(out var p) ? p : 0,
                    WindSpeed = winds[i].GetDouble()
                });
            }
        }

        // Parse daily forecast
        if (doc.RootElement.TryGetProperty("daily", out var daily))
        {
            var dates = daily.GetProperty("time").EnumerateArray().ToList();
            var maxTemps = daily.GetProperty("temperature_2m_max").EnumerateArray().ToList();
            var minTemps = daily.GetProperty("temperature_2m_min").EnumerateArray().ToList();
            var codes = daily.GetProperty("weather_code").EnumerateArray().ToList();
            var precip = daily.GetProperty("precipitation_probability_max").EnumerateArray().ToList();
            var precipSum = daily.GetProperty("precipitation_sum").EnumerateArray().ToList();
            var sunrises = daily.GetProperty("sunrise").EnumerateArray().ToList();
            var sunsets = daily.GetProperty("sunset").EnumerateArray().ToList();

            for (int i = 0; i < dates.Count; i++)
            {
                data.Daily.Add(new DailyForecast
                {
                    Date = DateTime.Parse(dates[i].GetString() ?? ""),
                    TemperatureMax = maxTemps[i].GetDouble(),
                    TemperatureMin = minTemps[i].GetDouble(),
                    WeatherCode = codes[i].GetInt32(),
                    PrecipitationProbability = precip[i].TryGetInt32(out var p) ? p : 0,
                    PrecipitationSum = precipSum[i].TryGetDouble(out var ps) ? ps : 0,
                    Sunrise = DateTime.Parse(sunrises[i].GetString() ?? ""),
                    Sunset = DateTime.Parse(sunsets[i].GetString() ?? "")
                });
            }
        }

        return data;
    }

    /// <summary>
    /// Search for location by name and return coordinates.
    /// </summary>
    public async Task<LocationResult?> SearchLocationAsync(string query)
    {
        string url = $"{GEOCODING_API_BASE}?name={Uri.EscapeDataString(query)}&count=1&language=en&format=json";

        var response = await _httpClient.GetStringAsync(url);
        using var doc = JsonDocument.Parse(response);

        if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
        {
            var first = results[0];
            return new LocationResult
            {
                Latitude = first.GetProperty("latitude").GetDouble(),
                Longitude = first.GetProperty("longitude").GetDouble(),
                Name = first.GetProperty("name").GetString() ?? "",
                CountryCode = first.TryGetProperty("country_code", out var cc) ? cc.GetString() ?? "" : "",
                Admin1 = first.TryGetProperty("admin1", out var a1) ? a1.GetString() ?? "" : ""
            };
        }
        return null;
    }

    /// <summary>
    /// Get approximate location from IP address using ip-api.com (free, no auth).
    /// </summary>
    public async Task<LocationResult?> GetLocationFromIPAsync()
    {
        try
        {
            string url = "http://ip-api.com/json/?fields=status,lat,lon,city,regionName,countryCode";
            var response = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);

            if (doc.RootElement.TryGetProperty("status", out var status) && status.GetString() == "success")
            {
                return new LocationResult
                {
                    Latitude = doc.RootElement.GetProperty("lat").GetDouble(),
                    Longitude = doc.RootElement.GetProperty("lon").GetDouble(),
                    Name = doc.RootElement.TryGetProperty("city", out var city) ? city.GetString() ?? "" : "",
                    Admin1 = doc.RootElement.TryGetProperty("regionName", out var region) ? region.GetString() ?? "" : "",
                    CountryCode = doc.RootElement.TryGetProperty("countryCode", out var cc) ? cc.GetString() ?? "" : ""
                };
            }
        }
        catch { }
        return null;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

/// <summary>
/// Location search result.
/// </summary>
public class LocationResult
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Name { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string Admin1 { get; set; } = "";

    public string DisplayName => !string.IsNullOrEmpty(Admin1) && CountryCode == "US"
        ? $"{Name}, {Admin1}"
        : $"{Name}, {CountryCode}";
}
