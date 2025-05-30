using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry.Intersect;


namespace SharpShadow
{
    public static class SsUtils
    {
        public static Point3d ComputeTrimmedSurfaceCenter(BrepFace face, List<string> logMessages, int partIndex, double tolerance)
        {
            Surface srf = face.UnderlyingSurface();
            Interval uDomain = srf.Domain(0);
            Interval vDomain = srf.Domain(1);
            logMessages.Add($"Part {partIndex}, surface domain: U[{uDomain.Min}, {uDomain.Max}], V[{vDomain.Min}, {vDomain.Max}]");

            // 获取 Trim 信息并计算 UV 中心
            Point2d uvCenter = Point2d.Unset;
            int uvSampleCount = 0;
            double uSum = 0.0, vSum = 0.0;

            foreach (BrepLoop loop in face.Loops)
            {
                // 计算 loop 总长度和最短 trim 长度
                double loopLength = 0.0;
                double minTrimLength = double.MaxValue;
                List<Curve> trimCurves = new List<Curve>();

                foreach (BrepTrim trim in loop.Trims)
                {
                    Curve trimCurve = trim.DuplicateCurve();
                    if (trimCurve == null)
                        continue;

                    double trimLength = trimCurve.GetLength();
                    loopLength += trimLength;
                    if (trimLength < minTrimLength)
                        minTrimLength = trimLength;
                    trimCurves.Add(trimCurve);
                }

                logMessages.Add($"Part {partIndex}, loop length: {loopLength}, min trim length: {minTrimLength}");

                // 使用 DivideByLength 采样
                double sampleLength = 0.5 * minTrimLength;
                foreach (Curve trimCurve in trimCurves)
                {
                    double[] tParams = trimCurve.DivideByLength(sampleLength, true);
                    if (tParams == null || tParams.Length == 0)
                    {
                        tParams = new[] { trimCurve.Domain.Mid };
                    }

                    foreach (double t in tParams)
                    {
                        Point3d uv = trimCurve.PointAt(t);
                        uSum += uv.X;
                        vSum += uv.Y;
                        uvSampleCount++;
                    }
                }
            }

            if (uvSampleCount > 0)
            {
                double uAvg = uSum / uvSampleCount;
                double vAvg = vSum / uvSampleCount;
                uvCenter = new Point2d(uAvg, vAvg);
                logMessages.Add($"Part {partIndex}, trim UV center: ({uAvg}, {vAvg}), samples: {uvSampleCount}");
            }
            else
            {
                logMessages.Add($"Warning: No valid trim UV points for part {partIndex}, using surface center");
                uvCenter = new Point2d(uDomain.Mid, vDomain.Mid);
            }

            Point3d centerPoint = srf.PointAt(uvCenter.X, uvCenter.Y);
            if (!centerPoint.IsValid)
            {
                logMessages.Add($"Warning: Invalid center point for part {partIndex} at UV ({uvCenter.X}, {uvCenter.Y})");
                return Point3d.Unset;
            }

            return centerPoint;
        }

        // Helper method to check if point is inside curve
        public static bool IsPointInsideCurve(Curve curve, Point3d point, Plane plane, double tolerance)
        {
            if (curve == null || !curve.IsClosed || !curve.IsPlanar())
            {
                return false;
            }

            Point3d projectedPoint = plane.ClosestPoint(point);
            PointContainment containment = curve.Contains(projectedPoint, plane, tolerance);
            return containment == PointContainment.Inside;
        }

    }
}
