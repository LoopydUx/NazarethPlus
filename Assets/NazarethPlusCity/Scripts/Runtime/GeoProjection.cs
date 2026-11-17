using System;
using UnityEngine;

namespace NazarethPlusCity
{
    /// <summary>
    /// WGS84 longitude/latitude to a local east-north frame, then Unity meters.
    /// +X is east, +Y is up, +Z is north. Source altitude is discarded.
    /// </summary>
    public struct GeoProjection
    {
        public double OriginLongitude;
        public double OriginLatitude;
        double _x0, _y0, _z0;
        double _sinLat, _cosLat, _sinLon, _cosLon;

        const double EquatorialRadius = 6378137.0;
        const double Flattening = 1.0 / 298.257223563;
        const double EccentricitySquared = Flattening * (2.0 - Flattening);

        public static GeoProjection FromOrigin(double longitude, double latitude)
        {
            var frame = new GeoProjection
            {
                OriginLongitude = longitude,
                OriginLatitude = latitude
            };
            GeodeticToEcef(latitude, longitude, 0.0, out frame._x0, out frame._y0, out frame._z0);
            double lat = latitude * Math.PI / 180.0;
            double lon = longitude * Math.PI / 180.0;
            frame._sinLat = Math.Sin(lat);
            frame._cosLat = Math.Cos(lat);
            frame._sinLon = Math.Sin(lon);
            frame._cosLon = Math.Cos(lon);
            return frame;
        }

        public Vector3 Project(double longitude, double latitude)
        {
            Project(longitude, latitude, out double east, out double north);
            return new Vector3((float)east, 0f, (float)north);
        }

        public void Project(double longitude, double latitude, out double east, out double north)
        {
            GeodeticToEcef(latitude, longitude, 0.0, out double x, out double y, out double z);
            double dx = x - _x0;
            double dy = y - _y0;
            double dz = z - _z0;
            east = -_sinLon * dx + _cosLon * dy;
            north = -_sinLat * _cosLon * dx - _sinLat * _sinLon * dy + _cosLat * dz;
        }

        public static double HaversineMeters(double lon1, double lat1, double lon2, double lat2)
        {
            double rlat1 = lat1 * Math.PI / 180.0;
            double rlat2 = lat2 * Math.PI / 180.0;
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dLat * 0.5) * Math.Sin(dLat * 0.5)
                       + Math.Cos(rlat1) * Math.Cos(rlat2) * Math.Sin(dLon * 0.5) * Math.Sin(dLon * 0.5);
            return EquatorialRadius * 2.0 * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
        }

        static void GeodeticToEcef(double latDeg, double lonDeg, double height, out double x, out double y, out double z)
        {
            double lat = latDeg * Math.PI / 180.0;
            double lon = lonDeg * Math.PI / 180.0;
            double sinLat = Math.Sin(lat);
            double cosLat = Math.Cos(lat);
            double n = EquatorialRadius / Math.Sqrt(1.0 - EccentricitySquared * sinLat * sinLat);
            x = (n + height) * cosLat * Math.Cos(lon);
            y = (n + height) * cosLat * Math.Sin(lon);
            z = (n * (1.0 - EccentricitySquared) + height) * sinLat;
        }
    }
}
