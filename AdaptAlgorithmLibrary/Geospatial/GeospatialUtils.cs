using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NpgsqlTypes;

namespace UrDeliveries.Utilities.Geospatial
{
    public static class GeospatialFormula
    {

        public static double EarthRadius = 6371000;

        public static double DegreesToRadians(double angle)
        {
            return angle * Math.PI / 180.0;
        }

        public static double RadiansToDegrees(double angle)
        {
            return angle / Math.PI * 180;
        }

        /// <summary>
        /// Get the distance in meters between two points.
        /// </summary>
        /// <param name="P1"></param>
        /// <param name="P2"></param>
        /// <returns></returns>
        public static double Distance(Point P1, Point P2)
        {
            return Distance(P1.Latitude, P1.Longitude, P2.Latitude, P2.Longitude);
        }

        /// <summary>
        /// Get the distance in meters between two points.
        /// </summary>
        /// <param name="latitude1">Latitude of the first point</param>
        /// <param name="longitude1">Longitude of the first point</param>
        /// <param name="latitude2">Latitude of the second point</param>
        /// <param name="longitude2">Longitude of the second point</param>
        /// <returns></returns>
        public static double Distance(double latitude1, double longitude1, double latitude2, double longitude2)
        {
            double lat1 = DegreesToRadians(latitude1);
            double lon1 = DegreesToRadians(longitude1);
            double lat2 = DegreesToRadians(latitude2);
            double lon2 = DegreesToRadians(longitude2);
            double dLat = (lat2 - lat1);
            double dLon = (lon2 - lon1);
            double a =
              Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
              Math.Cos(lat1) * Math.Cos(lat2) *
              Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return EarthRadius * c;
        }

        /*
        public static double Bearing(Point StartPoint, Point EndPoint)
        {
            var y = Math.Sin(EndPoint.Longitude - StartPoint.Longitude) * Math.Cos(EndPoint.Latitude);
            var x = Math.Cos(StartPoint.Latitude) * Math.Sin(EndPoint.Latitude) -
                    Math.Sin(StartPoint.Latitude) * Math.Cos(EndPoint.Latitude) * Math.Cos(EndPoint.Longitude - StartPoint.Longitude);
            var bearing = RadiansToDegrees(Math.Atan2(y, x));
            return bearing;
        }

        public static double CrossTrackDistance(Point P, Point StartPoint, Point EndPoint)
        {
            double distanceSP = Distance(StartPoint, P);
            double bearingSP = Bearing(StartPoint, P);
            double bearingSE = Bearing(StartPoint, EndPoint);
            return Math.Asin(Math.Sin(distanceSP / EarthRadius) * Math.Sin(bearingSP - bearingSE)) * EarthRadius;
        }

        public static double AlongTrackDistance(Point P, Point StartPoint, Point EndPoint)
        {
            double distanceSP = Distance(StartPoint, P);
            double crossTrackDistance = CrossTrackDistance(P, StartPoint, EndPoint);
            return Math.Acos(Math.Cos(distanceSP / EarthRadius) / Math.Cos(crossTrackDistance / EarthRadius)) * EarthRadius;
        }
        */

        public static Point ClosestPointOnLine(Point P, Point LineStartPoint, Point LineEndPoint)
        {

            // Approximation used: using long/lat as x/y

            double v1x = LineEndPoint.Longitude - LineStartPoint.Longitude;
            double v1y = LineEndPoint.Latitude - LineEndPoint.Latitude;
            double v2x = P.Longitude - LineStartPoint.Longitude;
            double v2y = P.Latitude - LineStartPoint.Latitude;
            double projection = v1x * v2x + v1y * v2y;
            double lineLengthSquared = v1x * v1x + v1y * v1y;

            double fraction = -1;

            if (lineLengthSquared != 0)
            {
                fraction = projection / lineLengthSquared;
            }

            double closestX, closestY;

            if (fraction < 0)
            {
                closestX = LineStartPoint.Longitude;
                closestY = LineStartPoint.Latitude;
            }
            else if (fraction > 1)
            {
                closestX = LineEndPoint.Longitude;
                closestY = LineEndPoint.Latitude;
            }
            else
            {
                closestX = LineStartPoint.Longitude + fraction * v1x;
                closestY = LineStartPoint.Latitude + fraction * v1y;
            }
            return new Point
            {
                Latitude = closestY,
                Longitude = closestX
            };
        }

        public static double Distance(Point P, Point LineStartPoint, Point LineEndPoint)
        {
            Point closestPoint = ClosestPointOnLine(P, LineStartPoint, LineEndPoint);
            return Distance(P, closestPoint);
        }

        public static double Distance(Point P, LineString line)
        {
            double minDistance = double.PositiveInfinity;
            for (int i = 1; i < line.Points.Count(); i++)
            {
                Point start = line.Points.ElementAt(i - 1);
                Point end = line.Points.ElementAt(i);
                double distance = Distance(P, start, end);
                if (distance < minDistance)
                {
                    minDistance = distance;
                }
            }
            return minDistance;
        }

        public static int ClosestSegmentIndex(Point P, double[,] segments)
        {
            int index = -1;
            double minDistance = double.PositiveInfinity;
            Point start, end;
            int numSegments = segments.Length / 2;
            for (int i = 1; i < numSegments; i++)
            {
                start = new Point { Latitude = segments[i - 1, 0], Longitude = segments[i - 1, 1] };
                end = new Point { Latitude = segments[i, 0], Longitude = segments[i, 1] };
                var distance = Distance(P, start, end);
                if (distance < minDistance)
                {
                    index = i;
                    minDistance = distance;
                    if (minDistance < 20.0) // Early termination if the min distance is below a tolerance
                    {
                        return index;
                    }
                }
            }
            return index;
        }

        public static double DistanceAlongLine(Point point, double[,] line, double[] cumulativeSegmentLengths = null)
        {
            double distance = 0;

            // 1) Find closest segment index
            int index = ClosestSegmentIndex(point, line);

            // 2) Compute length of the segments before matched segment
            distance = cumulativeSegmentLengths[index - 1];

            // 3) Compute projection of the point on the matched segment
            Point segmentStart = new Point { Latitude = line[index, 0], Longitude = line[index, 1] };
            Point segmentEnd = new Point { Latitude = line[index + 1, 0], Longitude = line[index + 1, 1] };
            Point projected = ClosestPointOnLine(point, segmentStart, segmentEnd);

            // 4) Add distance from the starting node of the matched segment to the projected point
            distance += Distance(segmentStart, projected);

            return distance;
        }
    }

    /// <summary>
    /// Google Polyline Converter (Encoder and Decoder)
    /// From: https://stackoverflow.com/questions/3852268/c-sharp-implementation-of-googles-encoded-polyline-algorithm
    /// </summary>
    public static class GooglePolylineConverter
    {
        /// <summary>
        /// Decodes the specified polyline string.
        /// </summary>
        /// <param name="polylineString">The polyline string.</param>
        /// <returns>A list with Points</returns>
        public static IEnumerable<Point> Decode(string polylineString)
        {
            if (string.IsNullOrEmpty(polylineString))
                throw new ArgumentNullException(nameof(polylineString));

            var polylineChars = polylineString.ToCharArray();
            var index = 0;

            var currentLat = 0;
            var currentLng = 0;

            while (index < polylineChars.Length)
            {
                // Next lat
                var sum = 0;
                var shifter = 0;
                int nextFiveBits;
                do
                {
                    nextFiveBits = polylineChars[index++] - 63;
                    sum |= (nextFiveBits & 31) << shifter;
                    shifter += 5;
                } while (nextFiveBits >= 32 && index < polylineChars.Length);

                if (index >= polylineChars.Length)
                    break;

                currentLat += (sum & 1) == 1 ? ~(sum >> 1) : (sum >> 1);

                // Next lng
                sum = 0;
                shifter = 0;
                do
                {
                    nextFiveBits = polylineChars[index++] - 63;
                    sum |= (nextFiveBits & 31) << shifter;
                    shifter += 5;
                } while (nextFiveBits >= 32 && index < polylineChars.Length);

                if (index >= polylineChars.Length && nextFiveBits >= 32)
                    break;

                currentLng += (sum & 1) == 1 ? ~(sum >> 1) : (sum >> 1);

                yield return new Point
                {
                    Latitude = Convert.ToDouble(currentLat) / 1E5,
                    Longitude = Convert.ToDouble(currentLng) / 1E5
                };
            }
        }

        /// <summary>
        /// Encodes the specified points list.
        /// </summary>
        /// <param name="points">The points.</param>
        /// <returns>The polyline string.</returns>
        public static string Encode(IEnumerable<Point> points)
        {
            var str = new StringBuilder();

            var encodeDiff = (Action<int>)(diff =>
            {
                var shifted = diff << 1;
                if (diff < 0)
                    shifted = ~shifted;

                var rem = shifted;

                while (rem >= 0x20)
                {
                    str.Append((char)((0x20 | (rem & 0x1f)) + 63));

                    rem >>= 5;
                }

                str.Append((char)(rem + 63));
            });

            var lastLat = 0;
            var lastLng = 0;

            foreach (var point in points)
            {
                var lat = (int)Math.Round(point.Latitude * 1E5);
                var lng = (int)Math.Round(point.Longitude * 1E5);

                encodeDiff(lat - lastLat);
                encodeDiff(lng - lastLng);

                lastLat = lat;
                lastLng = lng;
            }

            return str.ToString();
        }
    }

    /// <summary>
    /// Service class for operations with the NPGSQL library for Geospatial data
    /// </summary>
    public static class NpgsqlServices
    {
        /// <summary>
        /// Converts a Google Polyline route to an NpgsqlPath
        /// </summary>
        /// <param name="googleRoutePolyline">A string containing a Google encoded polyline</param>
        /// <returns></returns>
        public static NpgsqlPath GetPathFromGoogleRoutePolyline(string googleRoutePolyline)
        {
            IEnumerable<Point> points =
                GooglePolylineConverter.Decode(googleRoutePolyline).ToList();
            IEnumerable<NpgsqlPoint> npgsqlPoints =
                points.Select(p => new NpgsqlPoint(p.Latitude, p.Longitude));
            NpgsqlPath path = new NpgsqlPath(npgsqlPoints);
            return path;
        }
    }

    /// <summary>
    /// TODO: Check if it works properly with lat/lng
    /// Source: https://stackoverflow.com/questions/14671206/convex-hull-library
    /// </summary>
    public static class ConvexHull
    {
        public static double cross(Point O, Point A, Point B)
        {
            return (A.Longitude - O.Longitude) * (B.Latitude - O.Latitude) - (A.Latitude - O.Latitude) * (B.Longitude - O.Longitude);
        }

        public static List<Point> GetConvexHull(List<Point> points)
        {
            if (points == null)
                return null;

            if (points.Count() <= 1)
                return points;

            int n = points.Count(), k = 0;
            List<Point> H = new List<Point>(new Point[2 * n]);

            points.Sort((a, b) =>
                a.Longitude == b.Longitude ? a.Latitude.CompareTo(b.Latitude) : a.Longitude.CompareTo(b.Longitude));

            // Build lower hull
            for (int i = 0; i < n; ++i)
            {
                while (k >= 2 && cross(H[k - 2], H[k - 1], points[i]) <= 0)
                    k--;
                H[k++] = points[i];
            }

            // Build upper hull
            for (int i = n - 2, t = k + 1; i >= 0; i--)
            {
                while (k >= t && cross(H[k - 2], H[k - 1], points[i]) <= 0)
                    k--;
                H[k++] = points[i];
            }

            return H.Take(k - 1).ToList();
        }
    }
    
}
