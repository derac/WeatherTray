namespace WeatherTray;

/// <summary>
/// Contains all weather data for a location - current, hourly, and daily forecasts.
/// </summary>
public class WeatherData
{
    public CurrentWeather Current { get; set; } = new();
    public List<HourlyForecast> Hourly { get; set; } = new();
    public List<DailyForecast> Daily { get; set; } = new();
}

/// <summary>
/// Current weather conditions.
/// </summary>
public class CurrentWeather
{
    public double Temperature { get; set; }
    public int WeatherCode { get; set; }
    public double WindSpeed { get; set; }
    public int WindDirection { get; set; }
    public int Humidity { get; set; }
    public double UVIndex { get; set; }
    public double FeelsLike { get; set; }
    public DateTime Time { get; set; }

    public string WeatherDescription => GetWeatherDescription(WeatherCode);
    public string WeatherIcon => GetWeatherIcon(WeatherCode);

    private static string GetWeatherDescription(int code) => code switch
    {
        0 => "Clear sky",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Foggy",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 or 63 or 65 => "Rain",
        66 or 67 => "Freezing rain",
        71 or 73 or 75 => "Snow",
        77 => "Snow grains",
        80 or 81 or 82 => "Rain showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm with hail",
        _ => "Unknown"
    };

    private static string GetWeatherIcon(int code) => code switch
    {
        0 => "☀️",
        1 or 2 => "⛅",
        3 => "☁️",
        45 or 48 => "🌫️",
        51 or 53 or 55 or 61 or 63 or 65 or 80 or 81 or 82 => "🌧️",
        56 or 57 or 66 or 67 => "🌨️",
        71 or 73 or 75 or 77 or 85 or 86 => "❄️",
        95 or 96 or 99 => "⛈️",
        _ => "❓"
    };
}

/// <summary>
/// Hourly forecast data point.
/// </summary>
public class HourlyForecast
{
    public DateTime Time { get; set; }
    public double Temperature { get; set; }
    public int WeatherCode { get; set; }
    public int PrecipitationProbability { get; set; }
    public double WindSpeed { get; set; }

    public string WeatherIcon => WeatherCode switch
    {
        0 => "☀️",
        1 or 2 => "⛅",
        3 => "☁️",
        45 or 48 => "🌫️",
        51 or 53 or 55 or 61 or 63 or 65 or 80 or 81 or 82 => "🌧️",
        56 or 57 or 66 or 67 => "🌨️",
        71 or 73 or 75 or 77 or 85 or 86 => "❄️",
        95 or 96 or 99 => "⛈️",
        _ => "❓"
    };
}

/// <summary>
/// Daily forecast data point.
/// </summary>
public class DailyForecast
{
    public DateTime Date { get; set; }
    public double TemperatureMax { get; set; }
    public double TemperatureMin { get; set; }
    public int WeatherCode { get; set; }
    public int PrecipitationProbability { get; set; }
    public double PrecipitationSum { get; set; }
    public DateTime Sunrise { get; set; }
    public DateTime Sunset { get; set; }

    public string WeatherDescription => WeatherCode switch
    {
        0 => "Clear sky",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Foggy",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 or 63 or 65 => "Rain",
        66 or 67 => "Freezing rain",
        71 or 73 or 75 => "Snow",
        77 => "Snow grains",
        80 or 81 or 82 => "Rain showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm with hail",
        _ => "Unknown"
    };

    public string WeatherIcon => WeatherCode switch
    {
        0 => "☀️",
        1 or 2 => "⛅",
        3 => "☁️",
        45 or 48 => "🌫️",
        51 or 53 or 55 or 61 or 63 or 65 or 80 or 81 or 82 => "🌧️",
        56 or 57 or 66 or 67 => "🌨️",
        71 or 73 or 75 or 77 or 85 or 86 => "❄️",
        95 or 96 or 99 => "⛈️",
        _ => "❓"
    };
}
