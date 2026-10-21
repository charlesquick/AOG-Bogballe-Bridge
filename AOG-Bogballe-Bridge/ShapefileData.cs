using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace AOGBogballeBridge
{
    public enum ShapefileGeometryType
    {
        Point,
        MultiPoint,
        Polyline,
        Polygon
    }

    public sealed class ShapefileFeature
    {
        public ShapefileGeometryType GeometryType { get; }
        public IReadOnlyList<IReadOnlyList<Point>> Parts { get; }
        public IReadOnlyDictionary<string, string> Attributes { get; }

        internal ShapefileFeature(
            ShapefileGeometryType geometryType,
            IReadOnlyList<IReadOnlyList<Point>> parts,
            IReadOnlyDictionary<string, string> attributes)
        {
            GeometryType = geometryType;
            Parts = parts;
            Attributes = attributes;
        }

        public bool Contains(Point point)
        {
            if (GeometryType != ShapefileGeometryType.Polygon)
                return false;

            bool inside = false;
            foreach (IReadOnlyList<Point> ring in Parts)
            {
                if (ring.Count < 3)
                    continue;

                bool ringInside = false;
                for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
                {
                    Point a = ring[j];
                    Point b = ring[i];
                    if (IsOnSegment(point, a, b))
                        return !inside;

                    if ((a.Y > point.Y) != (b.Y > point.Y) &&
                        point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                        ringInside = !ringInside;
                }

                if (ringInside)
                    inside = !inside;
            }

            return inside;
        }

        private static bool IsOnSegment(Point point, Point a, Point b)
        {
            double cross = (point.Y - a.Y) * (b.X - a.X) - (point.X - a.X) * (b.Y - a.Y);
            double tolerance = 1e-12 * Math.Max(1, Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)));
            if (Math.Abs(cross) > tolerance)
                return false;

            return point.X >= Math.Min(a.X, b.X) - tolerance && point.X <= Math.Max(a.X, b.X) + tolerance &&
                   point.Y >= Math.Min(a.Y, b.Y) - tolerance && point.Y <= Math.Max(a.Y, b.Y) + tolerance;
        }
    }

    public sealed class ShapefileData
    {
        public IReadOnlyList<ShapefileFeature> Features { get; }
        public IReadOnlyList<string> Fields { get; }
        public bool IsGeographic { get; }
        public string ProjectionSummary { get; }

        internal ShapefileData(
            IReadOnlyList<ShapefileFeature> features,
            IReadOnlyList<string> fields,
            bool isGeographic,
            string projectionSummary)
        {
            Features = features;
            Fields = fields;
            IsGeographic = isGeographic;
            ProjectionSummary = projectionSummary;
        }
    }

    public static class ShapefileReader
    {
        public static ShapefileData Read(string shapefilePath)
        {
            byte[] shapeBytes = File.ReadAllBytes(shapefilePath);
            if (shapeBytes.Length < 100 || ReadInt32BigEndian(shapeBytes, 0) != 9994)
                throw new InvalidDataException("The selected file is not a valid ESRI shapefile.");

            int shapeType = ReadInt32LittleEndian(shapeBytes, 32);
            List<(ShapefileGeometryType Type, IReadOnlyList<IReadOnlyList<Point>> Parts)> geometries = new();
            int position = 100;
            while (position < shapeBytes.Length)
            {
                if (shapeBytes.Length - position < 8)
                    throw new InvalidDataException("The shapefile contains an incomplete record header.");

                int contentLengthWords = ReadInt32BigEndian(shapeBytes, position + 4);
                if (contentLengthWords <= 0 || contentLengthWords > int.MaxValue / 2)
                    throw new InvalidDataException("The shapefile contains an invalid record length.");
                int contentLength = contentLengthWords * 2;
                position += 8;
                if (contentLength < 4 || contentLength > shapeBytes.Length - position)
                    throw new InvalidDataException("The shapefile contains an invalid record length.");

                ReadOnlySpan<byte> record = shapeBytes.AsSpan(position, contentLength);
                position += contentLength;
                int recordType = ReadInt32LittleEndian(record, 0);
                if (recordType == 0)
                {
                    geometries.Add((ShapefileGeometryType.Point, Array.Empty<IReadOnlyList<Point>>()));
                    continue;
                }

                int baseType = recordType switch
                {
                    11 or 21 => 1,
                    13 or 23 => 3,
                    15 or 25 => 5,
                    18 or 28 => 8,
                    _ => recordType
                };
                if (baseType != shapeType && NormalizeShapeType(shapeType) != baseType)
                    throw new InvalidDataException("The shapefile contains a record with an inconsistent geometry type.");

                geometries.Add(ParseGeometry(record, baseType));
            }

            string dbfPath = Path.ChangeExtension(shapefilePath, ".dbf");
            DbfData dbf = File.Exists(dbfPath) ? ReadDbf(dbfPath) : DbfData.Empty;
            (bool isGeographic, string projectionSummary) = ReadProjection(shapefilePath);
            ValidateCoordinates(geometries, isGeographic);
            List<ShapefileFeature> features = new(geometries.Count);
            for (int i = 0; i < geometries.Count; i++)
            {
                (ShapefileGeometryType type, IReadOnlyList<IReadOnlyList<Point>> parts) = geometries[i];
                if (parts.Count == 0)
                    continue;

                IReadOnlyDictionary<string, string> attributes =
                    i < dbf.Records.Count ? dbf.Records[i] : EmptyAttributes;
                features.Add(new ShapefileFeature(type, parts, attributes));
            }

            return new ShapefileData(features, dbf.Fields, isGeographic, projectionSummary);
        }

        private static (bool IsGeographic, string Summary) ReadProjection(string shapefilePath)
        {
            string projectionPath = Path.ChangeExtension(shapefilePath, ".prj");
            if (!File.Exists(projectionPath))
                return (false, "No .prj found; coordinates are shown in their raw units.");

            string wkt = File.ReadAllText(projectionPath).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (wkt.StartsWith("GEOGCS[", StringComparison.OrdinalIgnoreCase) ||
                wkt.StartsWith("GEOGCRS[", StringComparison.OrdinalIgnoreCase) ||
                wkt.StartsWith("GEOGRAPHICCRS[", StringComparison.OrdinalIgnoreCase))
            {
                if (!Regex.IsMatch(wkt, @"(?:UNIT|ANGLEUNIT)\s*\[\s*""(?:degree|degrees)""", RegexOptions.IgnoreCase))
                    throw new InvalidDataException("The geographic .prj must use degree units.");

                return (true, "Geographic coordinates projected locally to meters for display.");
            }

            if (wkt.StartsWith("PROJCS[", StringComparison.OrdinalIgnoreCase) ||
                wkt.StartsWith("PROJCRS[", StringComparison.OrdinalIgnoreCase))
                return (false, "Projected coordinates from .prj shown in source units.");

            throw new InvalidDataException("The .prj coordinate system is not a supported geographic or projected CRS.");
        }

        private static void ValidateCoordinates(
            IEnumerable<(ShapefileGeometryType Type, IReadOnlyList<IReadOnlyList<Point>> Parts)> geometries,
            bool isGeographic)
        {
            foreach (Point point in geometries.SelectMany(geometry => geometry.Parts).SelectMany(part => part))
            {
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                    throw new InvalidDataException("The shapefile contains a non-finite coordinate.");
                if (isGeographic && (point.Y < -90 || point.Y > 90))
                    throw new InvalidDataException("The geographic shapefile contains a latitude outside -90 to 90 degrees.");
            }
        }

        private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
            new Dictionary<string, string>();

        private static int NormalizeShapeType(int type) => type switch
        {
            11 or 21 => 1,
            13 or 23 => 3,
            15 or 25 => 5,
            18 or 28 => 8,
            _ => type
        };

        private static (ShapefileGeometryType Type, IReadOnlyList<IReadOnlyList<Point>> Parts) ParseGeometry(
            ReadOnlySpan<byte> record,
            int shapeType)
        {
            if (shapeType == 1)
            {
                EnsureLength(record, 20);
                return (ShapefileGeometryType.Point,
                    new IReadOnlyList<Point>[] { new[] { new Point(ReadDouble(record, 4), ReadDouble(record, 12)) } });
            }

            if (shapeType == 8)
            {
                EnsureLength(record, 40);
                int pointCount = ReadInt32LittleEndian(record, 36);
                ValidateCount(pointCount, record.Length - 40, 16);
                Point[] points = new Point[pointCount];
                for (int i = 0; i < pointCount; i++)
                    points[i] = ReadPoint(record, 40 + i * 16);
                return (ShapefileGeometryType.MultiPoint, new IReadOnlyList<Point>[] { points });
            }

            if (shapeType is not (3 or 5))
                throw new InvalidDataException($"Shapefile geometry type {shapeType} is not supported.");

            EnsureLength(record, 44);
            int partCount = ReadInt32LittleEndian(record, 36);
            int totalPointCount = ReadInt32LittleEndian(record, 40);
            if (partCount < 0 || totalPointCount < 0 || partCount > totalPointCount ||
                44L + partCount * 4L + totalPointCount * 16L > record.Length)
                throw new InvalidDataException("The shapefile contains invalid geometry data.");

            int pointsOffset = 44 + partCount * 4;
            List<IReadOnlyList<Point>> parts = new(partCount);
            for (int partIndex = 0; partIndex < partCount; partIndex++)
            {
                int start = ReadInt32LittleEndian(record, 44 + partIndex * 4);
                int end = partIndex + 1 < partCount
                    ? ReadInt32LittleEndian(record, 44 + (partIndex + 1) * 4)
                    : totalPointCount;
                if (start < 0 || end < start || end > totalPointCount)
                    throw new InvalidDataException("The shapefile contains invalid part indexes.");

                Point[] points = new Point[end - start];
                for (int i = start; i < end; i++)
                    points[i - start] = ReadPoint(record, pointsOffset + i * 16);
                parts.Add(points);
            }

            return (shapeType == 5 ? ShapefileGeometryType.Polygon : ShapefileGeometryType.Polyline, parts);
        }

        private static DbfData ReadDbf(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 32)
                throw new InvalidDataException("The matching DBF file has an invalid header.");

            uint recordCountRaw = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4));
            int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2));
            int recordLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10, 2));
            if (recordCountRaw > int.MaxValue || headerLength < 33 || headerLength > bytes.Length || recordLength < 1)
                throw new InvalidDataException("The matching DBF file has invalid dimensions.");

            List<DbfField> fields = new();
            int fieldPosition = 32;
            while (fieldPosition < headerLength && bytes[fieldPosition] != 0x0D)
            {
                if (headerLength - fieldPosition < 32)
                    throw new InvalidDataException("The matching DBF file has an incomplete field descriptor.");

                ReadOnlySpan<byte> descriptor = bytes.AsSpan(fieldPosition, 32);
                int nameLength = descriptor[..11].IndexOf((byte)0);
                if (nameLength < 0)
                    nameLength = 11;
                string name = Encoding.ASCII.GetString(descriptor[..nameLength]).Trim();
                int length = descriptor[16];
                if (string.IsNullOrEmpty(name) || length == 0)
                    throw new InvalidDataException("The matching DBF file contains an invalid field descriptor.");
                fields.Add(new DbfField(name, length));
                fieldPosition += 32;
            }

            if (fields.Sum(field => field.Length) + 1 > recordLength)
                throw new InvalidDataException("The matching DBF file's fields exceed its record length.");

            int recordCount = (int)recordCountRaw;
            long requiredLength = (long)headerLength + (long)recordCount * recordLength;
            if (requiredLength > bytes.Length)
                throw new InvalidDataException("The matching DBF file ends before all records are present.");

            List<IReadOnlyDictionary<string, string>> records = new(recordCount);
            for (int row = 0; row < recordCount; row++)
            {
                int offset = headerLength + row * recordLength;
                Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
                if (bytes[offset] != (byte)'*')
                {
                    int fieldOffset = offset + 1;
                    foreach (DbfField field in fields)
                    {
                        string value = Encoding.ASCII.GetString(bytes, fieldOffset, field.Length).Trim();
                        values[field.Name] = value;
                        fieldOffset += field.Length;
                    }
                }
                records.Add(values);
            }

            return new DbfData(fields.Select(field => field.Name).ToArray(), records);
        }

        private static void EnsureLength(ReadOnlySpan<byte> bytes, int requiredLength)
        {
            if (bytes.Length < requiredLength)
                throw new InvalidDataException("The shapefile contains an incomplete geometry record.");
        }

        private static void ValidateCount(int count, int availableBytes, int bytesPerItem)
        {
            if (count < 0 || (long)count * bytesPerItem > availableBytes)
                throw new InvalidDataException("The shapefile contains an invalid point count.");
        }

        private static Point ReadPoint(ReadOnlySpan<byte> bytes, int offset) =>
            new(ReadDouble(bytes, offset), ReadDouble(bytes, offset + 8));

        private static double ReadDouble(ReadOnlySpan<byte> bytes, int offset) =>
            BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(offset, 8)));

        private static int ReadInt32LittleEndian(ReadOnlySpan<byte> bytes, int offset) =>
            BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));

        private static int ReadInt32BigEndian(ReadOnlySpan<byte> bytes, int offset) =>
            BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(offset, 4));

        private sealed record DbfField(string Name, int Length);

        private sealed record DbfData(
            IReadOnlyList<string> Fields,
            IReadOnlyList<IReadOnlyDictionary<string, string>> Records)
        {
            public static DbfData Empty { get; } = new(Array.Empty<string>(), Array.Empty<IReadOnlyDictionary<string, string>>());
        }
    }
}