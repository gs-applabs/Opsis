using System;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;

namespace MotionDesk.Services
{
    /// <summary>
    /// Opt-in approximate location from the Windows Location service (plan 0B.5). Only called after an explicit click + confirmation in the UI.
    /// The result is rounded to about 1 km before it leaves this class and is never logged; the caller stores only the rounded point.
    /// </summary>
    public static class DeviceLocation
    {
        public static async Task<(double Lat, double Lon)?> GetApproximateAsync()
        {
            try
            {
                if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed) return null;
                var locator = new Geolocator { DesiredAccuracyInMeters = 5000 };   // reduced accuracy on purpose: weather does not need more
                var pos = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(10));
                var p = pos.Coordinate.Point.Position;
                if (!WeatherCoord.Plausible(p.Latitude, p.Longitude)) return null;
                return (WeatherCoord.Round(p.Latitude), WeatherCoord.Round(p.Longitude));
            }
            catch (Exception) { return null; }
        }
    }
}
