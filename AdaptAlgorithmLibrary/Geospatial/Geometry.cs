using System.Collections.Generic;

namespace UrDeliveries.Utilities.Geospatial
{
    public class Geometry
    {
        public int Id { get; set; }
    }

    public class Point : Geometry
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    public class LineString : Geometry
    {
        public IEnumerable<Point> Points { get; set; }
    }
}