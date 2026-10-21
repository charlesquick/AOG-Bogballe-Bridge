using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AOGBogballeBridge
{
    public sealed class ShapefileMap : FrameworkElement
    {
        public static readonly DependencyProperty FeaturesProperty = DependencyProperty.Register(
            nameof(Features),
            typeof(IReadOnlyList<ShapefileFeature>),
            typeof(ShapefileMap),
            new FrameworkPropertyMetadata(Array.Empty<ShapefileFeature>(), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ValueFieldProperty = DependencyProperty.Register(
            nameof(ValueField),
            typeof(string),
            typeof(ShapefileMap),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IsGeographicProperty = DependencyProperty.Register(
            nameof(IsGeographic),
            typeof(bool),
            typeof(ShapefileMap),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty GpsPositionValidProperty = DependencyProperty.Register(
            nameof(GpsPositionValid),
            typeof(bool),
            typeof(ShapefileMap),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty GpsLatitudeProperty = DependencyProperty.Register(
            nameof(GpsLatitude),
            typeof(double),
            typeof(ShapefileMap),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty GpsLongitudeProperty = DependencyProperty.Register(
            nameof(GpsLongitude),
            typeof(double),
            typeof(ShapefileMap),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

        public IReadOnlyList<ShapefileFeature> Features
        {
            get => (IReadOnlyList<ShapefileFeature>)GetValue(FeaturesProperty);
            set => SetValue(FeaturesProperty, value);
        }

        public string? ValueField
        {
            get => (string?)GetValue(ValueFieldProperty);
            set => SetValue(ValueFieldProperty, value);
        }

        public bool IsGeographic
        {
            get => (bool)GetValue(IsGeographicProperty);
            set => SetValue(IsGeographicProperty, value);
        }

        public bool GpsPositionValid
        {
            get => (bool)GetValue(GpsPositionValidProperty);
            set => SetValue(GpsPositionValidProperty, value);
        }

        public double GpsLatitude
        {
            get => (double)GetValue(GpsLatitudeProperty);
            set => SetValue(GpsLatitudeProperty, value);
        }

        public double GpsLongitude
        {
            get => (double)GetValue(GpsLongitudeProperty);
            set => SetValue(GpsLongitudeProperty, value);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            drawingContext.DrawRectangle(Brushes.White, null, new Rect(RenderSize));

            Point[] points = Features
                .SelectMany(feature => feature.Parts)
                .SelectMany(part => part)
                .ToArray();
            if (points.Length == 0 || ActualWidth <= 0 || ActualHeight <= 0)
                return;

            double centerLongitude = (points.Min(point => point.X) + points.Max(point => point.X)) / 2;
            double centerLatitude = (points.Min(point => point.Y) + points.Max(point => point.Y)) / 2;
            double latitudeScale = Math.Cos(centerLatitude * Math.PI / 180);
            Point ToMap(Point point) => IsGeographic
                ? new Point(
                    6_378_137 * (point.X - centerLongitude) * Math.PI / 180 * latitudeScale,
                    6_378_137 * (point.Y - centerLatitude) * Math.PI / 180)
                : point;

            Point[] mapPoints = points.Select(ToMap).ToArray();
            double minX = mapPoints.Min(point => point.X);
            double maxX = mapPoints.Max(point => point.X);
            double minY = mapPoints.Min(point => point.Y);
            double maxY = mapPoints.Max(point => point.Y);
            double spanX = Math.Max(maxX - minX, 1e-12);
            double spanY = Math.Max(maxY - minY, 1e-12);
            const double margin = 20;
            double scale = Math.Min(
                Math.Max(0, ActualWidth - margin * 2) / spanX,
                Math.Max(0, ActualHeight - margin * 2) / spanY);
            if (!double.IsFinite(scale) || scale <= 0)
                return;

            double contentWidth = spanX * scale;
            double contentHeight = spanY * scale;
            double offsetX = (ActualWidth - contentWidth) / 2 - minX * scale;
            double offsetY = (ActualHeight - contentHeight) / 2 + maxY * scale;
            Point Project(Point point)
            {
                Point mapPoint = ToMap(point);
                return new Point(offsetX + mapPoint.X * scale, offsetY - mapPoint.Y * scale);
            }

            double[]? values = ValueField == null
                ? null
                : Features.Select(feature => feature.Attributes.TryGetValue(ValueField, out string? text) &&
                                              double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
                                              double.IsFinite(value)
                    ? (double?)value
                    : null).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
            double minValue = values is { Length: > 0 } ? values.Min() : 0;
            double maxValue = values is { Length: > 0 } ? values.Max() : 0;

            foreach (ShapefileFeature feature in Features)
            {
                Brush fill = GetFillBrush(feature, minValue, maxValue);
                Pen outline = new(Brushes.SlateGray, 1);
                switch (feature.GeometryType)
                {
                    case ShapefileGeometryType.Polygon:
                        drawingContext.DrawGeometry(fill, outline, CreateGeometry(feature.Parts, Project, close: true));
                        break;
                    case ShapefileGeometryType.Polyline:
                        drawingContext.DrawGeometry(null, outline, CreateGeometry(feature.Parts, Project, close: false));
                        break;
                    case ShapefileGeometryType.Point:
                    case ShapefileGeometryType.MultiPoint:
                        foreach (Point point in feature.Parts.SelectMany(part => part))
                            drawingContext.DrawEllipse(fill, outline, Project(point), 3.5, 3.5);
                        break;
                }
            }

            if (IsGeographic && GpsPositionValid)
            {
                Point location = Project(new Point(GpsLongitude, GpsLatitude));
                if (new Rect(RenderSize).Contains(location))
                    drawingContext.DrawEllipse(Brushes.Red, new Pen(Brushes.White, 2), location, 7, 7);
            }
        }

        private Brush GetFillBrush(ShapefileFeature feature, double minValue, double maxValue)
        {
            if (ValueField == null || !feature.Attributes.TryGetValue(ValueField, out string? text) ||
                !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                !double.IsFinite(value))
                return new SolidColorBrush(Color.FromRgb(115, 177, 216));

            double fraction = maxValue > minValue ? Math.Clamp((value - minValue) / (maxValue - minValue), 0, 1) : 0.5;
            Color color = Color.FromRgb(
                (byte)Math.Round(45 + fraction * 195),
                (byte)Math.Round(125 - fraction * 55),
                (byte)Math.Round(225 - fraction * 180));
            return new SolidColorBrush(color);
        }

        private static StreamGeometry CreateGeometry(
            IReadOnlyList<IReadOnlyList<Point>> parts,
            Func<Point, Point> project,
            bool close)
        {
            StreamGeometry geometry = new() { FillRule = FillRule.EvenOdd };
            using (StreamGeometryContext context = geometry.Open())
            {
                foreach (IReadOnlyList<Point> part in parts)
                {
                    if (part.Count == 0)
                        continue;

                    context.BeginFigure(project(part[0]), close, close);
                    if (part.Count > 1)
                        context.PolyLineTo(part.Skip(1).Select(project).ToArray(), true, false);
                }
            }

            geometry.Freeze();
            return geometry;
        }
    }
}