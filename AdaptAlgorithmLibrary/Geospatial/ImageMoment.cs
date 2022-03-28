using System;
using System.Collections.Generic;
using System.Linq;

namespace UrDeliveries.Utilities.Geospatial
{
    /// <summary>
    /// Computes the orientation of a set of points
    /// TODO: support weights
    /// https://en.wikipedia.org/wiki/Image_moment
    /// </summary>
    public class ImageMoment
    {
        private double? _avgX { get; set; }
        private double? _avgY { get; set; }
        
        private List<double> X { get; set; }
        private List<double> Y { get; set; }
        
        public ImageMoment(List<double> x, List<double> y)
        {
            X = x.ToList();
            Y = y.ToList();
        }

        public double AvgX
        {
            get
            {
                if (_avgX == null)
                {
                    _avgX = X.Average();
                }
                return _avgX ?? 0;
            }
        }

        public double AvgY
        {
            get
            {
                if (_avgY == null)
                {
                    _avgY = Y.Average();
                }
                return _avgY ?? 0;
            }
        }

        public double GetAngle()
        {
            var M00 = GetMoment(0, 0);
            var M11 = GetMoment(1, 1);
            var M20 = GetMoment(2, 0);
            var M02 = GetMoment(0, 2);
            var xb = AvgX / M00;
            var yb = AvgY / M00;
            var mp20 = M20 / M00 - xb * xb;
            var mp02 = M02 / M00 - yb * yb;
            var mp11 = M11 / M00 - xb * yb;
            var offset = Math.Abs(M20) < Math.Abs(M02) ? Math.PI / 2 : 0;
            return 0.5 * Math.Atan(2 * mp11 / (mp20 - mp02)) + offset;
        }

        public double GetMoment(int p, int q)
        {
            return X.Select((t, i) => Math.Pow(t - AvgX, p) * Math.Pow(Y[i] - AvgY, q)).Sum();
        }
        
    }
}